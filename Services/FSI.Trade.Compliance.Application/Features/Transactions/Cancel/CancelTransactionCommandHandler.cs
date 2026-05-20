using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Cancel;

/// <summary>
/// Cancels a transaction. Three things happen:
///
/// <list type="number">
///   <item><b>Best-effort engine SetState</b> to <see cref="CancelledStateName"/>.
///         This is the "proper" cancel path — works only for schemes that
///         actually declare a Cancelled state (none of today's deployed
///         schemes do; <c>ImportWF</c> etc. don't have one). If the engine
///         rejects the state, we log and continue. Future scheme updates
///         can add a Cancelled state and the cancel will then transition
///         the engine without code changes.</item>
///   <item><b>Inbox cleanup (always)</b> — every <c>WorkflowInbox</c> row
///         for this process is DELETEd. The rule provider's
///         <c>CheckRole</c> gates on inbox membership, so once the rows are
///         gone, <c>GetAvailableCommandsAsync</c> returns an empty list
///         for any actor. The transaction is functionally cancelled from
///         every user's perspective.</item>
///   <item><b>Status flip</b> — <c>Transaction_Status_Lkp</c> is set to
///         the "Application Cancelled" lookup id. The trade-repo grid
///         filters + the detail page status both reflect the cancellation.</item>
/// </list>
///
/// Idempotent: cancelling an already-cancelled transaction is a no-op
/// (inbox already empty, status already cancelled).
/// </summary>
public class CancelTransactionCommandHandler
    : IRequestHandler<CancelTransactionCommand, TransactionDetailDto>
{
    /// <summary>Workflow scheme state name forced via SetState (best-effort).</summary>
    private const string CancelledStateName = "Application Cancelled";

    /// <summary>
    /// <c>TmX_Lookup.Visible_Value</c> we resolve to the cancelled
    /// <c>Lookup_ID</c>. Matches legacy
    /// <c>Constants.APPLICATION_STATUS + Constants.ApplicationCancelled</c>.
    /// </summary>
    private const string CancelledStatusLookupType   = "APPLICATION_STATUS";
    private const string CancelledStatusVisibleValue = "Application Cancelled";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IWorkflowEngine       _engine;
    private readonly IMediator             _mediator;
    private readonly ILogger<CancelTransactionCommandHandler> _log;

    public CancelTransactionCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService   current,
        IWorkflowEngine       engine,
        IMediator             mediator,
        ILogger<CancelTransactionCommandHandler> log)
    {
        _db       = db;
        _current  = current;
        _engine   = engine;
        _mediator = mediator;
        _log      = log;
    }

    public async Task<TransactionDetailDto> Handle(CancelTransactionCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated",
                            "Transaction cancel requires an authenticated caller.");

        // 1. Load the transaction.
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == req.transactionId, ct)
                 ?? throw new NotFoundException("transaction_not_found",
                        $"Transaction {req.transactionId} does not exist.");

        // 2. Workflow-side cancellation: best-effort SetState + always-on inbox cleanup.
        if (tx.IsWorkflowAttached == true && tx.ProcessInstanceId.HasValue)
        {
            var processId = tx.ProcessInstanceId.Value;

            // (a) Best-effort SetState. Schemes that declare an
            //     "Application Cancelled" state will transition cleanly here.
            //     Schemes that don't (the common case in this deployment) will
            //     throw — caught and logged at INFO since it's an expected
            //     outcome until the schemes are updated.
            try
            {
                await _engine.SetStateAsync(
                    processId:  processId,
                    state:      CancelledStateName,
                    identityId: userId,
                    reason:     req.reason,
                    ct:         ct);

                _log.LogInformation(
                    "Transaction {TransactionId} workflow moved to '{State}' via SetState.",
                    tx.TransactionId, CancelledStateName);
            }
            catch (Exception ex)
            {
                _log.LogInformation(
                    "SetState to '{State}' rejected by the engine for transaction {TransactionId} " +
                    "(typically because the scheme doesn't declare that state). " +
                    "Proceeding with manual inbox cleanup. Engine message: {Message}",
                    CancelledStateName, tx.TransactionId, ex.Message);
            }

            // (b) Always: clear WorkflowInbox rows for this process. The
            //     rule provider's CheckRole rule gates on inbox membership,
            //     so removing these rows effectively removes the
            //     transaction from every actor's queue. Mirror legacy
            //     WorkflowService.DropWorkflowInbox semantics.
            var inboxRows = await _db.WorkflowInboxes
                .Where(i => i.ProcessId == processId)
                .ToListAsync(ct);

            if (inboxRows.Count > 0)
            {
                _db.WorkflowInboxes.RemoveRange(inboxRows);
                _log.LogInformation(
                    "Transaction {TransactionId} inbox cleared — {Count} WorkflowInbox row(s) removed for process {ProcessId}.",
                    tx.TransactionId, inboxRows.Count, processId);
            }
        }
        else
        {
            _log.LogInformation(
                "Transaction {TransactionId} cancelled without engine touch — IsWorkflowAttached={Attached}, ProcessInstanceId={Pid}.",
                tx.TransactionId, tx.IsWorkflowAttached, tx.ProcessInstanceId);
        }

        // 3. Resolve the cancelled status lookup ID.
        var cancelledStatusLkp = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType   == CancelledStatusLookupType
                     && l.VisibleValue == CancelledStatusVisibleValue
                     && l.IsActive)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

        if (cancelledStatusLkp is null)
        {
            _log.LogWarning(
                "No active TmX_Lookup row for Lookup_Type='{Type}' Visible_Value='{Visible}'. " +
                "Transaction {TransactionId}'s Transaction_Status_Lkp left unchanged. " +
                "Run migration 2026_05_009_SeedApplicationCancelledLookup.sql to seed the missing row.",
                CancelledStatusLookupType, CancelledStatusVisibleValue, tx.TransactionId);
        }
        else if (tx.TransactionStatusLkp == cancelledStatusLkp.Value)
        {
            _log.LogInformation("Transaction {TransactionId} already at cancelled status; lookup write skipped.", tx.TransactionId);
        }
        else
        {
            tx.TransactionStatusLkp = cancelledStatusLkp.Value;
            tx.LastUpdatedBy        = userId;
            tx.LastUpdatedDate      = DateTime.UtcNow;
            _log.LogInformation(
                "Transaction {TransactionId} cancelled — Transaction_Status_Lkp set to {Lkp}. Reason: {Reason}.",
                tx.TransactionId, cancelledStatusLkp.Value, req.reason ?? "<none>");
        }

        // 4. Single SaveChanges commits inbox deletions + status update atomically.
        await _db.SaveChangesAsync(ct);

        // 5. Return the canonical detail shape via the existing read handler.
        return await _mediator.Send(new GetTransactionByIdQuery(tx.TransactionId), ct);
    }
}

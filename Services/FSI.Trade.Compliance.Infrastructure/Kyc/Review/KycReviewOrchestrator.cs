using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Kyc;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using FSI.Trade.Compliance.Application.Features.Transactions.Create;
using FSI.Trade.Compliance.Application.Features.Workflow.Execute;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

public class KycReviewOrchestrator : IKycReviewOrchestrator
{
    // Forward-only KYC commands, in the order they become available. The driver
    // executes the highest-priority AVAILABLE command each step (never a reverse
    // command like SendBackToAnalyst).
    private static readonly string[] ForwardCommands = { "SendToAuthorizer", "AssignToMe", "AuthorizeOrProceedToFccm" };
    private const string ActorName = "PERIODIC REVIEWER";

    private readonly IMediator                _mediator;
    private readonly IWorkflowEngine          _engine;
    private readonly IKycTransactionCloner    _cloner;
    private readonly IAmbientUserContext      _ambient;
    private readonly IApplicationDbContext    _db;
    private readonly IOptionsMonitor<PeriodicReviewOptions> _opt;
    private readonly ILogger<KycReviewOrchestrator> _log;

    public KycReviewOrchestrator(
        IMediator mediator, IWorkflowEngine engine, IKycTransactionCloner cloner,
        IAmbientUserContext ambient, IApplicationDbContext db,
        IOptionsMonitor<PeriodicReviewOptions> opt, ILogger<KycReviewOrchestrator> log)
    {
        _mediator = mediator; _engine = engine; _cloner = cloner; _ambient = ambient;
        _db = db; _opt = opt; _log = log;
    }

    public async Task<ReviewRunOutcome> RunAsync(string customerCode, int sourceTransactionId, CancellationToken ct = default)
    {
        var configured = string.IsNullOrWhiteSpace(_opt.CurrentValue.ReviewerUserId)
            ? "PERIODIC_REVIEWER" : _opt.CurrentValue.ReviewerUserId.Trim();
        // Resolve to the reviewer's GUID identity (User_ID). The OptimaJet inbox +
        // CheckRole rule are GUID-based, so impersonating with a non-GUID id leaves the
        // review KYC stuck at the authorizer step. Accept a configured GUID or username.
        var reviewerId = await _db.Users.AsNoTracking()
            .Where(u => u.Id == configured || u.UserName == configured)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct) ?? configured;

        using (_ambient.Impersonate(reviewerId, ActorName, 1))
        {
            // 1. Build + create the clone (header + customer snapshot).
            CreateTransactionCommand createCmd;
            try
            {
                createCmd = await _cloner.BuildCreateCommandAsync(sourceTransactionId, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Periodic review: clone build failed for source txn {Src}.", sourceTransactionId);
                return new ReviewRunOutcome(ReviewOutcome.Failed, null, null, "Clone build failed: " + ex.Message);
            }

            int newTxnId; Guid? processId;
            try
            {
                var created = await _mediator.Send(createCmd, ct);
                newTxnId  = created.transactionId;
                processId = created.processInstanceId;
            }
            catch (ConflictException ex) when (ex.Code == "kyc_active_exists")
            {
                _log.LogInformation("Periodic review blocked for customer {Cust}: {Msg}", customerCode, ex.Message);
                return new ReviewRunOutcome(ReviewOutcome.Blocked, null, null, ex.Message);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Periodic review: create failed for customer {Cust}.", customerCode);
                return new ReviewRunOutcome(ReviewOutcome.Failed, null, null, "Create failed: " + ex.Message);
            }

            if (processId is null || processId == Guid.Empty)
                return new ReviewRunOutcome(ReviewOutcome.Failed, newTxnId, null,
                    "New transaction has no workflow process attached.");

            // 2. Copy the product payload (detail UDF + grids + snapshot udf) from source.
            try
            {
                await _cloner.CopyProductDataAsync(sourceTransactionId, newTxnId, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Periodic review: copy product data failed src {Src} -> new {New}.", sourceTransactionId, newTxnId);
                return new ReviewRunOutcome(ReviewOutcome.Failed, newTxnId, null, "Copy failed: " + ex.Message);
            }

            // 3. Drive the workflow to finalisation as PERIODIC REVIEWER. Dispatching
            //    ExecuteWorkflowCommand via MediatR reuses the existing handler's
            //    status-sync + remark + onboarding finalise hook (TryFireCaseInsertion).
            var finalised = await DriveWorkflowAsync(processId.Value, reviewerId, ct);

            // 4. The finalise hook creates the KycCaseRequest — surface its id.
            long? kycReqId = await _db.KycCaseRequests
                .Where(k => k.TransactionId == newTxnId)
                .OrderByDescending(k => k.Id)
                .Select(k => (long?)k.Id)
                .FirstOrDefaultAsync(ct);

            if (!finalised)
                return new ReviewRunOutcome(ReviewOutcome.Failed, newTxnId, kycReqId,
                    "Workflow did not reach finalisation.");

            _log.LogInformation("Periodic review initiated for customer {Cust}: new txn {New}, KycReq {Req}.",
                customerCode, newTxnId, kycReqId);
            return new ReviewRunOutcome(ReviewOutcome.Completed, newTxnId, kycReqId, "Review initiated");
        }
    }

    /// <summary>Steps the KYC workflow forward as the reviewer until it finalises.</summary>
    private async Task<bool> DriveWorkflowAsync(Guid processId, string reviewerId, CancellationToken ct)
    {
        for (var step = 0; step < 8; step++)
        {
            IReadOnlyList<WorkflowCommand> available;
            try { available = await _engine.GetAvailableCommandsAsync(processId, reviewerId, ct); }
            catch (Exception ex) { _log.LogError(ex, "Periodic review: GetAvailableCommands failed for {Pid}.", processId); return false; }

            var names = available.Select(c => c.Name).ToList();
            var next  = ForwardCommands.FirstOrDefault(f => names.Any(n => string.Equals(n, f, StringComparison.OrdinalIgnoreCase)));
            if (next is null)
            {
                _log.LogWarning("Periodic review {Pid}: no forward command available (have: {Cmds}).",
                    processId, string.Join(", ", names));
                return false;
            }

            WorkflowExecutionResult res;
            try
            {
                res = await _mediator.Send(new ExecuteWorkflowCommand
                {
                    processId = processId,
                    command   = next,
                    comments  = "System-initiated periodic review",
                }, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Periodic review: command '{Cmd}' failed for {Pid}.", next, processId);
                return false;
            }

            if (res.IsFinalised) return true;
            if (!res.WasExecuted)
            {
                _log.LogWarning("Periodic review: command '{Cmd}' not executed for {Pid} (state {State}).",
                    next, processId, res.CurrentState);
                return false;
            }
        }
        return false;
    }
}

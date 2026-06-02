using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Application.Features.Workflow.Execute;

/// <summary>
/// Executes a workflow command and — if the resulting state is final —
/// fires the trade-to-FCCM submission as a post-execute hook.
///
/// This is the new-backend equivalent of the legacy
/// <c>IcbcImplementation.PostTransactionWFExecution</c> hook that
/// fired after <c>WorkflowRuntime.ExecuteCommand</c> in the main TMX
/// backend (see <c>LEGACY_TRANSACTION_FLOW.md</c> Phase 4).
///
/// Decoupling from FCCM failures: the FCCM submit is invoked best-
/// effort. If it throws, the workflow command is still considered
/// successful (the state transition happened) — the failure is logged
/// for retry via the resubmit admin endpoint. This matches the legacy
/// behaviour where a network blip on the post-execute hook didn't
/// undo the workflow transition that already committed in OptimaJet.
/// </summary>
public class ExecuteWorkflowCommandHandler : IRequestHandler<ExecuteWorkflowCommand, WorkflowExecutionResult>
{
    private readonly IWorkflowEngine        _engine;
    private readonly ICurrentUserService    _current;
    private readonly IApplicationDbContext  _db;
    private readonly IFccmTradeCaseService  _fccmTrade;
    private readonly ILogger<ExecuteWorkflowCommandHandler> _logger;

    public ExecuteWorkflowCommandHandler(
        IWorkflowEngine        engine,
        ICurrentUserService    current,
        IApplicationDbContext  db,
        IFccmTradeCaseService  fccmTrade,
        ILogger<ExecuteWorkflowCommandHandler> logger)
    {
        _engine    = engine;
        _current   = current;
        _db        = db;
        _fccmTrade = fccmTrade;
        _logger    = logger;
    }

    public async Task<WorkflowExecutionResult> Handle(ExecuteWorkflowCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Workflow execution requires an authenticated caller.");

        var result = await _engine.ExecuteCommandAsync(
            processId:  req.processId,
            identityId: userId,
            command:    req.command,
            comments:   req.comments,
            parameters: req.parameters,
            ct:         ct);

        // Post-execute hook — Slice 9. Fires the FCCM trade submission
        // when the command finalised the workflow. Best-effort: failures
        // are logged but don't fail the workflow command.
        if (result.IsFinalised && result.WasExecuted)
        {
            await TryFireFccmSubmitAsync(req.processId, ct);
        }

        return result;
    }

    /// <summary>
    /// Resolves the transaction matching this process instance and
    /// fires <c>IFccmTradeCaseService.SubmitAsync</c>. Idempotency is
    /// the service's job (it short-circuits when Fccm_Case_Id is
    /// already populated). All exceptions caught + logged — the
    /// workflow command already committed and shouldn't fail because
    /// the downstream is offline.
    /// </summary>
    private async Task TryFireFccmSubmitAsync(Guid processId, CancellationToken ct)
    {
        try
        {
            var transactionId = await _db.Transactions
                .AsNoTracking()
                .Where(t => t.ProcessInstanceId == processId)
                .Select(t => (int?)t.TransactionId)
                .FirstOrDefaultAsync(ct);

            if (transactionId is null)
            {
                _logger.LogWarning(
                    "Workflow finalised for ProcessId={ProcessId} but no transaction is linked — skipping FCCM submit.",
                    processId);
                return;
            }

            var submit = await _fccmTrade.SubmitAsync(transactionId.Value, ct);
            _logger.LogInformation(
                "FCCM submit post-workflow-finalise: TxId={TxId} CaseId={CaseId} Skipped={Skipped} FlagEvents={FlagCount}",
                submit.TransactionId, submit.FccmCaseId, submit.WasSkipped, submit.FlagEventCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FCCM submit FAILED for ProcessId={ProcessId} after workflow finalise. " +
                "Workflow state already committed; use POST /Transaction/{{id}}/ResubmitToFccm to retry.",
                processId);
        }
    }
}

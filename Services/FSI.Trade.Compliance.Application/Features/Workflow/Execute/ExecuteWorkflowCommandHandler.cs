using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Logging;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using FSI.Trade.Compliance.Domain.Entities.Application;
using System.Text.RegularExpressions;
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
    // Legacy parity (caseInsertionController): the KYC product routes to the
    // KYC onboarding case-insertion path; every other (trade) product routes
    // to the trade FCCM case build. Legacy hard-coded ProductId == 5; per FSI
    // direction we resolve it by Product_Name instead so it survives an id
    // change. The finalisation submit is also suppressed when the workflow
    // ends in the DISCARDED state (legacy: ToStateName != "DISCARDED").
    private const string KycProductName    = "KYC";
    private const string DiscardedStateName = "DISCARDED";

    private readonly IWorkflowEngine        _engine;
    private readonly ICurrentUserService    _current;
    private readonly IApplicationDbContext  _db;
    private readonly IFccmTradeCaseService  _fccmTrade;
    private readonly IKycCaseService        _kyc;
    private readonly ILogger<ExecuteWorkflowCommandHandler> _logger;

    public ExecuteWorkflowCommandHandler(
        IWorkflowEngine        engine,
        ICurrentUserService    current,
        IApplicationDbContext  db,
        IFccmTradeCaseService  fccmTrade,
        IKycCaseService        kyc,
        ILogger<ExecuteWorkflowCommandHandler> logger)
    {
        _engine    = engine;
        _current   = current;
        _db        = db;
        _fccmTrade = fccmTrade;
        _kyc       = kyc;
        _logger    = logger;
    }

    public async Task<WorkflowExecutionResult> Handle(ExecuteWorkflowCommand req, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Workflow execution requires an authenticated caller.");

        using var wf = WorkflowLog.Begin(_logger, "Workflow Command",
            ("ProcessId", req.processId), ("Command", req.command), ("UserId", userId));
        wf.Step($"Executing workflow command '{req.command}' on the engine.");

        var result = await wf.ExternalAsync("OptimaJet ExecuteCommand", () => _engine.ExecuteCommandAsync(
            processId:     req.processId,
            identityId:    userId,
            command:       req.command,
            comments:      req.comments,
            parameters:    req.parameters,
            identityLabel: _current.UserName ?? userId,
            ct:            ct));

        wf.Step($"Engine result: executed={result.WasExecuted}, finalised={result.IsFinalised}, state='{result.CurrentState}'.");

        // Post-execute hook — Slice 9. Fires the FCCM case insertion when
        // the command finalised the workflow, EXCEPT when it finalised into
        // the DISCARDED state (legacy: IsFinalised && ToStateName !=
        // "DISCARDED"). Best-effort: failures are logged but don't fail the
        // workflow command.
        var finalisedToDiscarded = string.Equals(
            result.CurrentState?.Trim(), DiscardedStateName, StringComparison.OrdinalIgnoreCase);

        if (result.IsFinalised && result.WasExecuted && !finalisedToDiscarded)
        {
            wf.Step("Workflow finalised — firing FCCM case insertion (post-execute hook).");
            await TryFireCaseInsertionAsync(req.processId, ct);
        }
        else if (result.IsFinalised && result.WasExecuted && finalisedToDiscarded)
        {
            wf.Step("Workflow finalised to DISCARDED — skipping FCCM case insertion (legacy parity).");
            _logger.LogInformation(
                "Workflow finalised to DISCARDED for ProcessId={ProcessId} — skipping FCCM case insertion (legacy parity).",
                req.processId);
        }

        // Keep the transaction's own status column in step with the workflow
        // trail. The OptimaJet engine advances its state, but TmX_Transaction's
        // Transaction_Status_Lkp is a separate column that was never updated —
        // so the detail stepper / status pill / list screens stayed on "Draft"
        // forever. Resolved by the executed COMMAND (legacy parity), id-agnostic.
        await TrySyncTransactionStatusAsync(req.command, req.processId, result, finalisedToDiscarded, userId, DateTime.UtcNow, ct);

        // Persist the analyst/authorizer's note (if any) as a transaction remark
        // so it's visible in the audit trail — e.g. the "notes to analyst" sent
        // with SendBackToAnalyst. Legacy stored these as TmX_Application_Remark
        // rows; we mirror that from the execute's `comments`.
        if (result.WasExecuted)
            await TryPersistWorkflowRemarkAsync(req, userId, DateTime.UtcNow, ct);

        wf.EndSuccess($"Command='{req.command}' executed={result.WasExecuted} finalised={result.IsFinalised} state='{result.CurrentState}'.");
        return result;
    }

    // ---- Workflow remark (execute comment → TmX_Application_Remark) ----

    /// <summary>
    /// Stores the execute's <c>comments</c> as a transaction remark (Module_Code
    /// "5") so the note surfaces in the audit trail. ActionType carries the
    /// command name so the reader sees which action the note accompanied (e.g.
    /// "SendBackToAnalyst"). Best-effort — never fails the workflow command.
    /// </summary>
    private async Task TryPersistWorkflowRemarkAsync(
        ExecuteWorkflowCommand req, string userId, DateTime now, CancellationToken ct)
    {
        var comment = req.comments?.Trim();
        if (string.IsNullOrWhiteSpace(comment)) return;

        try
        {
            var txId = await _db.Transactions.AsNoTracking()
                .Where(t => t.ProcessInstanceId == req.processId)
                .Select(t => (int?)t.TransactionId)
                .FirstOrDefaultAsync(ct);
            if (txId is null) return;

            var userName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(ct);

            _db.ApplicationRemarks.Add(new ApplicationRemark
            {
                TransactionId = txId.Value,
                ModuleCode    = "5",                                   // transaction scope (matches the audit read)
                TenantId      = _current.TenantId ?? 1,
                ActionType    = string.IsNullOrWhiteSpace(req.command) ? "Workflow" : req.command.Trim(),
                RemarksLkp    = 0,
                Comments      = comment,
                UserId        = userId,
                CreatedBy     = userName ?? userId,
                CreatedDate   = now
            });
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Workflow remark persisted for ProcessId={ProcessId} (command '{Command}').",
                req.processId, req.command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist workflow remark for ProcessId={ProcessId}.", req.processId);
        }
    }

    // ---- Transaction status sync (workflow trail → Transaction_Status_Lkp) ----

    // ICBC's APPLICATION_STATUS roster IS the workflow stage labels (Draft,
    // Analyst, Sent to Authorizer, Authorizer, CMS), and the engine's state
    // names mirror them ("AuthorizerActivity" → "Authorizer"). So we map by
    // NAME (id-agnostic across envs): try the raw state, then a humanized form
    // (strip "Activity", space camelCase) against the roster's Visible_Values.
    // The outcome-based names below are a LAST-resort for deployments whose
    // roster uses generic statuses instead; harmless if absent (resolve → null).
    private static readonly string[] CancelledStatusNames = { "Application Cancelled", "Cancelled", "Discarded", "Rejected" };
    private static readonly string[] CompletedStatusNames = { "Completed", "Approved", "Authorized", "Active" };
    private static readonly string[] InProgressStatusNames = { "Submitted", "In Progress", "Pending Authorization", "Pending", "With Authorizer" };

    private async Task TrySyncTransactionStatusAsync(
        string command, Guid processId, WorkflowExecutionResult result, bool discarded,
        string userId, DateTime now, CancellationToken ct)
    {
        try
        {
            if (!result.WasExecuted) return;

            var tx = await _db.Transactions
                .FirstOrDefaultAsync(t => t.ProcessInstanceId == processId, ct);
            if (tx is null) return;

            // PRIMARY (legacy parity — TransactionService.UpdateTransactionStatus):
            // the executed COMMAND determines the new application status via the
            // Command_Application_Status_Mapping lookup (command code → status
            // label → APPLICATION_STATUS id). We must map by command, NOT by the
            // OptimaJet state name: the engine parks every in-flight stage on the
            // single generic state "CommonInboxActivity", so the state alone can't
            // tell Draft→"Sent To Authorizer" apart from →"Sent Back To Analyst".
            var targetId = await ResolveStatusIdByCommandAsync(tx.TenantId, command, ct);

            // FALLBACK for commands without an explicit mapping row and for
            // finalisation: try the raw/humanized state name, then outcome buckets.
            if (targetId is null)
            {
                var stateName = (result.CurrentState ?? "").Trim();
                var candidates = new List<string>();
                if (stateName.Length > 0)
                {
                    candidates.Add(stateName);
                    candidates.Add(HumanizeStateName(stateName));
                }
                candidates.AddRange(
                    result.IsFinalised
                        ? (discarded ? CancelledStatusNames : CompletedStatusNames)
                        : InProgressStatusNames);

                targetId = await ResolveStatusIdAsync(tx.TenantId, candidates, ct);
            }

            if (targetId is null)
            {
                _logger.LogWarning(
                    "Status sync: no APPLICATION_STATUS lookup matched command '{Command}' (state '{State}', finalised={Final}, discarded={Discarded}) " +
                    "for tenant {Tenant}; leaving Transaction_Status_Lkp unchanged on ProcessId={ProcessId}.",
                    command, result.CurrentState, result.IsFinalised, discarded, tx.TenantId, processId);
                return;
            }

            if (tx.TransactionStatusLkp == targetId.Value) return;

            var previous = tx.TransactionStatusLkp;
            tx.TransactionStatusLkp = targetId.Value;
            tx.LastUpdatedBy        = userId;
            tx.LastUpdatedDate      = now;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Status sync: Transaction_Status_Lkp {Prev} → {Next} for ProcessId={ProcessId} (command '{Command}', state '{State}').",
                previous, targetId.Value, processId, command, result.CurrentState);
        }
        catch (Exception ex)
        {
            // Never fail the workflow command because the status mirror lagged.
            _logger.LogError(ex,
                "Status sync failed for ProcessId={ProcessId}; workflow state already committed.", processId);
        }
    }

    /// <summary>
    /// Legacy parity (TransactionService.UpdateTransactionStatus): maps the
    /// executed <paramref name="command"/> to an APPLICATION_STATUS id via the
    /// <c>Command_Application_Status_Mapping</c> lookup. The mapping row's
    /// Hidden_Value is the command code (e.g. "SendToAuthorizer") and its
    /// Visible_Value is the resulting status label (e.g. "Sent To Authorizer");
    /// that label is then resolved against APPLICATION_STATUS to get the id.
    /// Returns null when the command has no mapping row (caller falls back to
    /// state-name resolution). Projects only the columns needed — the lookup
    /// table's Created_Date is nvarchar here, so a full-entity load would throw.
    /// </summary>
    private async Task<int?> ResolveStatusIdByCommandAsync(int callerTenantId, string? command, CancellationToken ct)
    {
        var cmd = (command ?? "").Trim();
        if (cmd.Length == 0) return null;
        var lower = cmd.ToLowerInvariant();

        // command code → status label (Command_Application_Status_Mapping).
        // These reference lookups follow a cross-tenant CASCADE (caller tenant >
        // platform tenant 1 > lowest tenant present) — see
        // GetLookupsGroupedByCultureQueryHandler. They're commonly seeded under a
        // single tenant that is NOT the caller's, so a hard `TenantId == caller`
        // filter finds nothing and the status silently never advances. Legacy
        // didn't tenant-filter these at all; we fetch the candidates and pick the
        // cascade winner instead.
        var rows = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "Command_Application_Status_Mapping"
                     && l.IsActive
                     && l.HiddenValue != null
                     && l.HiddenValue.ToLower() == lower
                     && l.VisibleValue != null)
            .Select(l => new { l.VisibleValue, l.TenantId })
            .ToListAsync(ct);

        var label = PickByTenantCascade(
            rows.Select(r => (r.VisibleValue, r.TenantId)), callerTenantId);

        if (string.IsNullOrWhiteSpace(label)) return null;

        // status label → APPLICATION_STATUS id
        return await ResolveStatusIdAsync(callerTenantId, new[] { label }, ct);
    }

    /// <summary>
    /// Returns the first APPLICATION_STATUS lookup id (for the tenant) whose
    /// Visible_Value case-insensitively matches one of the candidate names, in
    /// order. Projects only the id (the lookup table's Created_Date is nvarchar
    /// in this DB, so a full-entity load would throw on the DateTime mapping).
    /// </summary>
    private async Task<int?> ResolveStatusIdAsync(int callerTenantId, IEnumerable<string> names, CancellationToken ct)
    {
        foreach (var raw in names)
        {
            var name = (raw ?? "").Trim();
            if (name.Length == 0) continue;
            var lower = name.ToLowerInvariant();

            // Cross-tenant cascade (see ResolveStatusIdByCommandAsync): match by
            // Visible_Value across tenants, then pick the cascade winner.
            var rows = await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == "APPLICATION_STATUS"
                         && l.IsActive
                         && l.VisibleValue != null
                         && l.VisibleValue.ToLower() == lower)
                .Select(l => new { l.Id, l.TenantId })
                .ToListAsync(ct);

            var id = PickByTenantCascade(
                rows.Select(r => ((int?)r.Id, r.TenantId)), callerTenantId);

            if (id.HasValue) return id;
        }
        return null;
    }

    /// <summary>
    /// Mirrors the lookup catalogue's cross-tenant cascade
    /// (<c>GetLookupsGroupedByCultureQueryHandler.ResolveOwningTenant</c>):
    /// prefer the caller's tenant, then platform tenant 1, then the lowest
    /// tenant present. Returns the value belonging to the winning tenant, or
    /// <c>default</c> when there are no rows.
    /// </summary>
    private static T? PickByTenantCascade<T>(IEnumerable<(T Value, int TenantId)> rows, int callerTenantId)
    {
        var list = rows.ToList();
        if (list.Count == 0) return default;

        var winners = list.Where(r => r.TenantId == callerTenantId).ToList();
        if (winners.Count == 0) winners = list.Where(r => r.TenantId == 1).ToList();
        if (winners.Count == 0)
        {
            var min = list.Min(r => r.TenantId);
            winners = list.Where(r => r.TenantId == min).ToList();
        }
        return winners[0].Value;
    }

    /// <summary>
    /// Turns an OptimaJet state/activity name into the business label the
    /// APPLICATION_STATUS roster uses: strips a trailing "Activity" and inserts
    /// spaces at camelCase boundaries. "AuthorizerActivity" → "Authorizer",
    /// "SentToAuthorizerActivity" → "Sent To Authorizer" (matched
    /// case-insensitively against the roster).
    /// </summary>
    private static string HumanizeStateName(string state)
    {
        var noActivity = Regex.Replace(state ?? "", "Activity$", "", RegexOptions.IgnoreCase);
        var spaced     = Regex.Replace(noActivity, "([a-z0-9])([A-Z])", "$1 $2");
        return spaced.Trim();
    }

    /// <summary>
    /// Resolves the transaction matching this process instance and routes
    /// the FCCM case insertion BY PRODUCT — mirroring the legacy
    /// <c>caseInsertionController.Post</c> branch:
    /// <list type="bullet">
    ///   <item>KYC product (resolved by <c>Product_Name = "KYC"</c>) → the
    ///   KYC onboarding case path (<c>IKycCaseService.SubmitAsync</c>),
    ///   guarded so a transaction never submits a second KYC case.</item>
    ///   <item>every other (trade) product → <c>IFccmTradeCaseService.SubmitAsync</c>
    ///   (idempotent on <c>Fccm_Case_Id</c>).</item>
    /// </list>
    /// All exceptions caught + logged — the workflow command already
    /// committed and shouldn't fail because the downstream is offline.
    /// </summary>
    private async Task TryFireCaseInsertionAsync(Guid processId, CancellationToken ct)
    {
        try
        {
            var tx = await _db.Transactions
                .AsNoTracking()
                .Where(t => t.ProcessInstanceId == processId)
                .Select(t => new { t.TransactionId, t.ProductId })
                .FirstOrDefaultAsync(ct);

            if (tx is null)
            {
                _logger.LogWarning(
                    "Workflow finalised for ProcessId={ProcessId} but no transaction is linked — skipping FCCM case insertion.",
                    processId);
                return;
            }

            var productName = await _db.Products
                .AsNoTracking()
                .Where(p => p.ProductId == tx.ProductId)
                .Select(p => p.ProductName)
                .FirstOrDefaultAsync(ct);

            var isKyc = string.Equals(productName?.Trim(), KycProductName, StringComparison.OrdinalIgnoreCase);

            if (isKyc)
            {
                // No double submit: legacy fired KYC onboarding once at
                // finalisation, and nothing else in the new flow submits a
                // KYC case. Skip if this transaction already has one.
                var alreadySubmitted = await _db.KycCaseRequests
                    .AsNoTracking()
                    .AnyAsync(k => k.TransactionId == tx.TransactionId, ct);

                if (alreadySubmitted)
                {
                    _logger.LogInformation(
                        "KYC case already exists for TxId={TxId} — skipping duplicate KYC submission.",
                        tx.TransactionId);
                    return;
                }

                var customerCode = await _db.TransactionCustomerSnapshots
                    .AsNoTracking()
                    .Where(c => c.TransactionId == tx.TransactionId)
                    .Select(c => c.CustomerCode)
                    .FirstOrDefaultAsync(ct);

                var kyc = await _kyc.SubmitAsync(new KycCaseSubmissionRequest
                {
                    CustomerId    = customerCode ?? "",
                    TransactionId = tx.TransactionId,
                }, ct);

                _logger.LogInformation(
                    "KYC case insertion post-workflow-finalise: TxId={TxId} RequestId={RequestId} Status={Status}",
                    tx.TransactionId, kyc.RequestId, kyc.Status);
                return;
            }

            // Trade product — existing FCCM trade case build.
            var submit = await _fccmTrade.SubmitAsync(tx.TransactionId, ct);
            _logger.LogInformation(
                "FCCM trade submit post-workflow-finalise: TxId={TxId} CaseId={CaseId} Skipped={Skipped} FlagEvents={FlagCount}",
                submit.TransactionId, submit.FccmCaseId, submit.WasSkipped, submit.FlagEventCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FCCM case insertion FAILED for ProcessId={ProcessId} after workflow finalise. " +
                "Workflow state already committed; use POST /Transaction/{{id}}/ResubmitToFccm to retry (trade).",
                processId);
        }
    }
}

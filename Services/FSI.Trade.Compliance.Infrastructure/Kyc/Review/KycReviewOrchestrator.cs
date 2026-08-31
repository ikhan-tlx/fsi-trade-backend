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

        var reviewerRoles = await GetRoleNamesAsync(reviewerId, ct);
        _log.LogInformation(
            "[PERIODIC-REVIEW] START customer={Cust} sourceTxn={Src} reviewerConfigured='{Cfg}' reviewerId={Rid} reviewerRoles=[{Roles}] reviewerResolved={Resolved}.",
            customerCode, sourceTransactionId, configured, reviewerId,
            string.Join(", ", reviewerRoles),
            !string.Equals(reviewerId, configured, StringComparison.OrdinalIgnoreCase) || reviewerRoles.Count > 0);

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

            _log.LogInformation(
                "[PERIODIC-REVIEW] created review txn {New} (processId={Pid}) for customer {Cust}.",
                newTxnId, processId, customerCode);

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

            _log.LogInformation(
                "[PERIODIC-REVIEW] product data copied src {Src} -> new {New}; driving workflow to finalisation.",
                sourceTransactionId, newTxnId);

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

    /// <summary>
    /// Steps the KYC workflow forward as the reviewer until it finalises.
    /// Emits a step-by-step trail under the <c>[PERIODIC-REVIEW DRIVE]</c> tag so
    /// UAT can follow every transition (available commands → chosen command →
    /// executed result / new state) and, on a halt, a full diagnostic of the
    /// authorizer pool vs. the reviewer's roles — the usual root cause being that
    /// the reviewer's role name does not match the scheme's authorizer actor value,
    /// so it is never queued into the authorizer inbox and CheckRole denies the
    /// authorize command. Logs land in <c>BackgroundService-*.log</c>.
    /// </summary>
    private async Task<bool> DriveWorkflowAsync(Guid processId, string reviewerId, CancellationToken ct)
    {
        var reviewerRoles = await GetRoleNamesAsync(reviewerId, ct);
        _log.LogInformation(
            "[PERIODIC-REVIEW DRIVE] START ProcessId={Pid} ReviewerId={Rid} ReviewerRoles=[{Roles}] Forward=[{Fwd}].",
            processId, reviewerId, string.Join(", ", reviewerRoles), string.Join(" > ", ForwardCommands));

        for (var step = 0; step < 8; step++)
        {
            IReadOnlyList<WorkflowCommand> available;
            try { available = await _engine.GetAvailableCommandsAsync(processId, reviewerId, ct); }
            catch (Exception ex)
            {
                _log.LogError(ex, "[PERIODIC-REVIEW DRIVE] step {Step} ProcessId={Pid}: GetAvailableCommands failed.", step, processId);
                return false;
            }

            var names = available.Select(c => c.Name).ToList();
            var next  = ForwardCommands.FirstOrDefault(f => names.Any(n => string.Equals(n, f, StringComparison.OrdinalIgnoreCase)));

            _log.LogInformation(
                "[PERIODIC-REVIEW DRIVE] step {Step} ProcessId={Pid}: available=[{Available}] -> chosen={Chosen}.",
                step, processId, string.Join(", ", names), next ?? "(none)");

            if (next is null)
            {
                // Halt: no forward command. Dump who owns the step so the cause is
                // unambiguous on UAT (reviewer roles vs. the resolved pool + roles).
                var diag = await BuildHaltDiagnosticsAsync(processId, reviewerId, reviewerRoles, ct);
                _log.LogWarning(
                    "[PERIODIC-REVIEW DRIVE] HALT ProcessId={Pid} step {Step}: no forward command available (have: [{Cmds}]). {Diag}",
                    processId, step, string.Join(", ", names), diag);
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
                _log.LogError(ex, "[PERIODIC-REVIEW DRIVE] step {Step} ProcessId={Pid}: command '{Cmd}' threw.", step, processId, next);
                return false;
            }

            _log.LogInformation(
                "[PERIODIC-REVIEW DRIVE] step {Step} ProcessId={Pid}: executed '{Cmd}' -> WasExecuted={Ok} IsFinalised={Fin} State='{State}'.",
                step, processId, next, res.WasExecuted, res.IsFinalised, res.CurrentState);

            if (res.IsFinalised)
            {
                _log.LogInformation(
                    "[PERIODIC-REVIEW DRIVE] COMPLETE ProcessId={Pid}: workflow finalised after {Steps} step(s).",
                    processId, step + 1);
                return true;
            }
            if (!res.WasExecuted)
            {
                var diag = await BuildHaltDiagnosticsAsync(processId, reviewerId, reviewerRoles, ct);
                _log.LogWarning(
                    "[PERIODIC-REVIEW DRIVE] HALT ProcessId={Pid} step {Step}: command '{Cmd}' reported not executed (state {State}). {Diag}",
                    processId, step, next, res.CurrentState, diag);
                return false;
            }
        }

        _log.LogWarning("[PERIODIC-REVIEW DRIVE] HALT ProcessId={Pid}: exceeded 8 steps without finalising.", processId);
        return false;
    }

    /// <summary>Role names held by a user id (empty if the id maps to no roles).</summary>
    private async Task<List<string>> GetRoleNamesAsync(string userId, CancellationToken ct) =>
        await _db.UserRoleMappings.AsNoTracking()
            .Join(_db.Roles.AsNoTracking(), urm => urm.RoleId, r => r.Id, (urm, r) => new { urm.UserId, r.Name })
            .Where(x => x.UserId == userId)
            .Select(x => x.Name)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>
    /// Builds a one-line diagnostic explaining WHY the drive stalled: the current
    /// workflow state, the reviewer's roles, and the authorizer pool (the
    /// WorkflowInbox rows the synchronizer wrote for this process) with the roles
    /// those pool members hold. When the reviewer is not in the pool, the scheme's
    /// authorizer actor role name does not match any role the reviewer holds — the
    /// fix is to align the reviewer's role mapping to the scheme's authorizer value.
    /// </summary>
    private async Task<string> BuildHaltDiagnosticsAsync(
        Guid processId, string reviewerId, IReadOnlyCollection<string> reviewerRoles, CancellationToken ct)
    {
        try
        {
            var pos = await _db.WorkflowProcessInstances.AsNoTracking()
                .Where(p => p.Id == processId)
                .Select(p => new { p.StateName, p.ActivityName })
                .FirstOrDefaultAsync(ct);

            var poolIds = await _db.WorkflowInboxes.AsNoTracking()
                .Where(i => i.ProcessId == processId)
                .Select(i => i.IdentityId.ToString())
                .ToListAsync(ct);

            var reviewerInPool = poolIds.Any(id => string.Equals(id, reviewerId, StringComparison.OrdinalIgnoreCase));

            var poolRoles = await _db.UserRoleMappings.AsNoTracking()
                .Join(_db.Roles.AsNoTracking(), urm => urm.RoleId, r => r.Id, (urm, r) => new { urm.UserId, r.Name })
                .Where(x => poolIds.Contains(x.UserId))
                .Select(x => x.Name)
                .Distinct()
                .ToListAsync(ct);

            var poolNames = await _db.Users.AsNoTracking()
                .Where(u => poolIds.Contains(u.Id))
                .Select(u => u.UserName)
                .Take(10)
                .ToListAsync(ct);

            var verdict = reviewerInPool
                ? "Reviewer IS in the authorizer pool but the authorize command was still denied — verify the reviewer holds the scheme's authorizer actor role name (CheckRole matches on role NAME)."
                : "Reviewer is NOT in the authorizer pool: the scheme's authorizer actor role name does not match any role the reviewer holds, so the inbox synchronizer never queued the reviewer. Align the reviewer's TmX_User_Role_Mapping to the scheme's authorizer actor value (this env's scheme uses 'Authorizer', while the reviewer is seeded with 'Authorizer KYC').";

            return $"State='{pos?.StateName}' Activity='{pos?.ActivityName}'. "
                 + $"Reviewer roles=[{string.Join(", ", reviewerRoles)}] ReviewerInPool={reviewerInPool}. "
                 + $"AuthorizerPool: {poolIds.Count} member(s) holdingRoles=[{string.Join(", ", poolRoles)}] sample=[{string.Join(", ", poolNames)}]. "
                 + verdict;
        }
        catch (Exception ex)
        {
            return "halt diagnostics unavailable: " + ex.Message;
        }
    }
}

using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Locations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OptimaJet.Workflow.Core.Model;
using OptimaJet.Workflow.Core.Persistence;          // ProcessHistoryItem (returned by Runtime.GetProcessHistory)
using OptimaJet.Workflow.Core.Runtime;

namespace FSI.Trade.Compliance.Infrastructure.Workflow.Actions;

/// <summary>
/// Port of legacy <c>tmx-finance-backend/TMX.Workflows/Runtime/WorkflowActions.cs</c>.
/// Registers every action and condition referenced by scheme XML on the
/// trade-finance flows. Each handler lives in its own bound delegate so
/// per-handler dependencies stay isolated.
///
/// SLICE 5 PHASE 2 — comprehensive port. Three buckets:
///
/// <list type="bullet">
///   <item><b>Bucket A — pure runtime</b>: only touch the engine APIs or
///         scheme parameters. No DB, no external services. Ported fully.</item>
///   <item><b>Bucket B — DB-only via existing entities</b>: read/write
///         <c>WorkflowInbox</c>, <c>TmX_Transaction</c>, role/user lookups,
///         location hierarchy. Ported fully against
///         <see cref="IApplicationDbContext"/> + <see cref="ILocationHierarchyService"/>.</item>
///   <item><b>Bucket C — external services or out-of-scope domain</b>:
///         8 notification/email/SMS actions (DLP integration HTTP),
///         loan-specific actions (AreaWiseVerification, recommendation,
///         attach/detach, etc.), and conditions reading loan-app fields
///         (PendingApprovals, RoleAmountSlabs, IsAmountInActorSlab,
///         CheckWFDeviation, IsAppAttached, checkSecondApprovalRequired*).
///         <b>No-op-with-log</b>: handlers log a structured warning and return a safe
///         default (void for actions, false for conditions). Workflow advances cleanly;
///         no NotImplementedException ever thrown for these. Real ports
///         land in Slice 8 — see BACKLOG "Notifications &amp; Approval Services".</item>
/// </list>
///
/// SLICE 5.6 — bound to v3.5's <see cref="IWorkflowActionProvider"/>:
///   • <c>GetActions()</c> / <c>GetConditions()</c> take no scheme args
///   • <c>IsActionAsync(name)</c> / <c>IsConditionAsync(name)</c> take only the action name
///   • No <c>IsGlobalAction</c> / <c>IsGlobalCondition</c> (those landed in v8+)
/// </summary>
public class FsiWorkflowActionProvider : IWorkflowActionProvider
{
    /// <summary>Process parameter name the scheme reads/writes for approval state.</summary>
    private const string ApproversParameterName = "Approvers";

    /// <summary>Process parameter name <c>ResetToStateByParameter</c> writes the target state into.</summary>
    private const string SetStateVariable = "SetStateVariable";

    private readonly Dictionary<string, ActionFn>           _actions;
    private readonly Dictionary<string, ConditionFn>        _conditions;
    private readonly IServiceScopeFactory                   _scopes;
    private readonly ILogger<FsiWorkflowActionProvider>     _log;

    private delegate void ActionFn(ProcessInstance pi, WorkflowRuntime r, string actionParameter);
    private delegate bool ConditionFn(ProcessInstance pi, WorkflowRuntime r, string actionParameter);

    public FsiWorkflowActionProvider(IServiceScopeFactory scopes, ILogger<FsiWorkflowActionProvider> log)
    {
        _scopes = scopes;
        _log    = log;

        _conditions = new Dictionary<string, ConditionFn>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- Bucket A — pure runtime ----
            ["IsApproveComplete"]       = IsApproveComplete,
            ["CurrentActivityExecuted"] = CurrentActivityExecuted,
            ["AppDelayWithWF"]          = AppDelayWithWF,

            // ---- Bucket B — DB-only ----
            ["RoleExists"]              = RoleExistsInCreatorLocationHierarchy,
            ["RoleExistsInBranch"]      = RoleExistsInBranch,
            ["AppDelay"]                = AppDelay,
            ["CheckCreatorRole"]        = CheckCreatorRole,

            // ---- Bucket C — out-of-scope / external (no-op-false-with-log) ----
            ["PendingApprovals"]                          = NoOpCondition("PendingApprovals",                          "loan-specific — pending-approval count from LoanApplicationService; not in Trade scope"),
            ["RoleAmountSlabs"]                           = NoOpCondition("RoleAmountSlabs",                           "loan-specific — role + amount-slab lookup; not in Trade scope"),
            ["IsAmountInActorSlab"]                       = NoOpCondition("IsAmountInActorSlab",                       "loan-specific — actor amount-slab check; not in Trade scope"),
            ["CheckWFDeviation"]                          = NoOpCondition("CheckWFDeviation",                          "needs ProductRuleService port (rule-evaluation engine)"),
            ["IsAppAttached"]                             = NoOpCondition("IsAppAttached",                             "loan-app IsAttached flag — not on Transaction; defaults to false"),
            ["checkSecondApprovalRequiredForNoGuarantor"] = NoOpCondition("checkSecondApprovalRequiredForNoGuarantor", "loan-specific — guarantor existence + amount-slab; not in Trade scope")
        };

        _actions = new Dictionary<string, ActionFn>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- Bucket A — pure runtime ----
            ["SaveCurrentStateInParameter"] = SaveCurrentStateInParameter,
            ["ResetToStateByParameter"]     = ResetToStateByParameter,
            ["KillSubProcesses"]            = KillSubProcesses,

            // ---- Bucket B — DB-only ----
            ["FillAllUsersBucket"]                = FillAllUsersBucket,
            ["FillApprovers"]                     = FillApprovers,
            ["Approve"]                           = Approve,
            ["ClearInboxByRole"]                  = ClearInboxByRole,
            ["UpdateApplicationCreatorByCommand"] = UpdateApplicationCreatorByCommand,

            // ---- Bucket C — DLP HTTP integration (no-op-with-log; Slice 8) ----
            ["SendClientNotification"]    = NoOpAction("SendClientNotification",    "DLP HTTP integration — Slice 8"),
            ["SendSMS"]                   = NoOpAction("SendSMS",                   "DLP HTTP integration — Slice 8"),
            ["SendEmail"]                 = NoOpAction("SendEmail",                 "DLP HTTP integration — Slice 8"),
            ["SendEmailToReceiver"]       = NoOpAction("SendEmailToReceiver",       "DLP HTTP integration — Slice 8"),
            ["SendEmailToAllReceivers"]   = NoOpAction("SendEmailToAllReceivers",   "DLP HTTP integration — Slice 8"),
            ["SendEscalationEmailByRole"] = NoOpAction("SendEscalationEmailByRole", "DLP HTTP integration — Slice 8"),
            ["SendEmailToCustomer"]       = NoOpAction("SendEmailToCustomer",       "DLP HTTP integration — Slice 8"),
            ["SendEmailToAppReceiver"]    = NoOpAction("SendEmailToAppReceiver",    "DLP HTTP integration + PDF report — Slice 8"),

            // ---- Bucket C — loan-specific / out-of-scope (no-op-with-log) ----
            ["AreaWiseVerification"]            = NoOpAction("AreaWiseVerification",            "loan-app verification matrix — not in Trade scope"),
            ["FillAppReceiversRecommendation"]  = NoOpAction("FillAppReceiversRecommendation",  "loan-app recommendation service — not in Trade scope"),
            ["ClearRecommendations"]            = NoOpAction("ClearRecommendations",            "loan-app recommendation service — not in Trade scope"),
            ["FillVerificationsFromChecklist"]  = NoOpAction("FillVerificationsFromChecklist",  "loan-app checklist-to-verification — not in Trade scope"),
            ["AttachApplication"]               = NoOpAction("AttachApplication",               "loan-app IsAttached flag — not on Transaction; treat as no-op"),
            ["DeattachApplication"]             = NoOpAction("DeattachApplication",             "loan-app IsAttached flag — not on Transaction; treat as no-op")
        };
    }

    /// <summary>
    /// Constructs an action that logs a structured warning and returns
    /// without throwing. The workflow continues normally. Replace with a
    /// real implementation when the supporting service is wired (Slice 8
    /// for notifications, future loan/transaction slices for the rest).
    /// </summary>
    private ActionFn NoOpAction(string actionName, string portNote) =>
        (pi, r, ap) =>
        {
            _log.LogWarning(
                "Workflow action '{Action}' invoked but not yet implemented — treating as no-op. " +
                "Process={ProcessId}, ActionParam={ActionParam}. Port note: {PortNote}.",
                actionName, pi.ProcessId, ap, portNote);
        };

    /// <summary>
    /// Constructs a condition that logs a structured warning and returns
    /// <c>false</c> without throwing. False is the safe default — the scheme
    /// transition that depends on this condition simply doesn't fire.
    /// </summary>
    private ConditionFn NoOpCondition(string conditionName, string portNote) =>
        (pi, r, ap) =>
        {
            _log.LogWarning(
                "Workflow condition '{Condition}' invoked but not yet implemented — returning false. " +
                "Process={ProcessId}, ActionParam={ActionParam}. Port note: {PortNote}.",
                conditionName, pi.ProcessId, ap, portNote);
            return false;
        };

    // ============================================================
    // IWorkflowActionProvider (v3.5)
    // ============================================================

    public void ExecuteAction(string name, ProcessInstance processInstance, WorkflowRuntime runtime, string actionParameter)
    {
        if (!_actions.TryGetValue(name, out var fn))
            throw new NotImplementedException($"Unknown workflow action '{name}'.");

        try
        {
            fn(processInstance, runtime, actionParameter);
        }
        catch (Exception ex)
        {
            // Match legacy "SaveWorkflowError" semantics — log the failure
            // with context but don't rethrow. Otherwise a single bad action
            // (e.g. malformed scheme param) aborts the whole transition and
            // leaves the workflow in an inconsistent state.
            _log.LogError(ex,
                "Workflow action '{Action}' threw. Process={ProcessId}, ActionParam={ActionParam}.",
                name, processInstance.ProcessId, actionParameter);
        }
    }

    public Task ExecuteActionAsync(string name, ProcessInstance processInstance, WorkflowRuntime runtime, string actionParameter, CancellationToken token)
    {
        ExecuteAction(name, processInstance, runtime, actionParameter);
        return Task.CompletedTask;
    }

    public bool ExecuteCondition(string name, ProcessInstance processInstance, WorkflowRuntime runtime, string actionParameter)
    {
        if (!_conditions.TryGetValue(name, out var fn))
            throw new NotImplementedException($"Workflow condition '{name}' is not implemented.");

        try
        {
            return fn(processInstance, runtime, actionParameter);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Workflow condition '{Condition}' threw — treating as false. Process={ProcessId}, ActionParam={ActionParam}.",
                name, processInstance.ProcessId, actionParameter);
            return false;
        }
    }

    public Task<bool> ExecuteConditionAsync(string name, ProcessInstance processInstance, WorkflowRuntime runtime, string actionParameter, CancellationToken token)
        => Task.FromResult(ExecuteCondition(name, processInstance, runtime, actionParameter));

    public bool IsActionAsync(string name)    => false;
    public bool IsConditionAsync(string name) => false;

    public List<string> GetActions()    => _actions.Keys.ToList();
    public List<string> GetConditions() => _conditions.Keys.ToList();

    // ============================================================
    // Bucket A — Condition implementations (pure runtime)
    // ============================================================

    /// <summary>
    /// Legacy parity: returns true when every entry in the Approvers
    /// dictionary has been approved (or no Approvers parameter is set).
    /// </summary>
    private bool IsApproveComplete(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        var approvers = ReadApproversParameter(pi);
        return approvers is null || approvers.IsApproved;
    }

    /// <summary>
    /// Legacy parity: returns true if the workflow history shows this state
    /// was already exited via the same (non-<c>SetState</c>) trigger before.
    /// On true, clears <c>WorkflowInbox</c> rows for this process — matches
    /// legacy <c>WorkflowService.DropWorkflowInbox</c> side effect.
    /// </summary>
    private bool CurrentActivityExecuted(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        IEnumerable<ProcessHistoryItem> history;
        try
        {
            history = r.GetProcessHistory(pi.ProcessId) ?? Enumerable.Empty<ProcessHistoryItem>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "CurrentActivityExecuted: failed to read process history for {ProcessId}; treating as not executed.",
                pi.ProcessId);
            return false;
        }

        var currentState  = pi.CurrentState;
        var triggerFilter = (actionParameter ?? "").Trim();

        var alreadyExecuted = string.IsNullOrEmpty(triggerFilter)
            ? history.Any(h => h.FromStateName == currentState && !string.Equals(h.TriggerName, "SetState", StringComparison.OrdinalIgnoreCase))
            : history.Any(h => h.FromStateName == currentState
                            && !string.Equals(h.TriggerName, "SetState",    StringComparison.OrdinalIgnoreCase)
                            &&  string.Equals(h.TriggerName, triggerFilter, StringComparison.OrdinalIgnoreCase));

        if (alreadyExecuted)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var processId = pi.ProcessId;
                var rows = db.WorkflowInboxes.Where(i => i.ProcessId == processId).ToList();
                if (rows.Count > 0)
                {
                    db.WorkflowInboxes.RemoveRange(rows);
                    db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

                    _log.LogInformation(
                        "CurrentActivityExecuted: cleared {Count} inbox row(s) for process {ProcessId} (state '{State}', trigger filter '{Filter}').",
                        rows.Count, processId, currentState, triggerFilter);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "CurrentActivityExecuted: side-effect inbox-clear failed for process {ProcessId}; condition still returns true.",
                    pi.ProcessId);
            }
        }

        return alreadyExecuted;
    }

    /// <summary>
    /// Returns true when the process has been idle in its current state for
    /// longer than <paramref name="actionParameter"/> minutes. "Idle" is
    /// measured against the most recent transition's timestamp in the
    /// runtime's process history.
    /// </summary>
    private bool AppDelayWithWF(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (!int.TryParse(actionParameter, out var thresholdMinutes))
            return false;

        try
        {
            var history = r.GetProcessHistory(pi.ProcessId);
            if (history is null || history.Count == 0)
                return false;

            var lastTransition = history.OrderByDescending(h => h.TransitionTime).First().TransitionTime;
            return DateTime.UtcNow.Subtract(lastTransition).TotalMinutes > thresholdMinutes;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AppDelayWithWF: failed to read history for {ProcessId}; returning false.", pi.ProcessId);
            return false;
        }
    }

    // ============================================================
    // Bucket B — Condition implementations (DB-only)
    // ============================================================

    /// <summary>
    /// Legacy <c>RoleExists</c> (a.k.a. RoleExistsInCreatorLocHierarchy):
    /// returns true if at least one user holds the named role AND that user
    /// is in the creator's location hierarchy (creator's home location or
    /// any descendant). Used to gate transitions on "does anyone in this
    /// role reach this transaction's geographic scope?".
    /// </summary>
    private bool RoleExistsInCreatorLocationHierarchy(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (string.IsNullOrWhiteSpace(actionParameter)) return false;

        var roleName = actionParameter.Trim();

        using var scope = _scopes.CreateScope();
        var db        = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationHierarchyService>();

        var creatorUserId = db.Transactions.AsNoTracking()
            .Where(t => t.ProcessInstanceId == pi.RootProcessId)
            .Select(t => t.UserId)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(creatorUserId)) return false;

        var creatorLocationId = db.Users.AsNoTracking()
            .Where(u => u.Id == creatorUserId)
            .Select(u => u.LocationId)
            .FirstOrDefault();
        if (!creatorLocationId.HasValue) return false;

        var locationIds = locations
            .GetSelfAndDescendantIdsAsync(creatorLocationId.Value, CancellationToken.None)
            .GetAwaiter().GetResult();

        return db.UserRoleMappings.AsNoTracking()
            .Join(db.Roles.AsNoTracking(), urm => urm.RoleId, role => role.Id, (urm, role) => new { urm.UserId, role.Name })
            .Join(db.Users.AsNoTracking(), x => x.UserId, u => u.Id, (x, u) => new { x.Name, u.LocationId })
            .Any(x => x.Name == roleName && x.LocationId.HasValue && locationIds.Contains(x.LocationId.Value));
    }

    /// <summary>
    /// Legacy <c>RoleExistsInBranch</c>: returns true if at least one user
    /// holds the named role and is mapped to the transaction's company
    /// branch in <c>TmX_Company_Branch_Users_Mapping</c> (effective today).
    /// </summary>
    private bool RoleExistsInBranch(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (string.IsNullOrWhiteSpace(actionParameter)) return false;
        var roleName = actionParameter.Trim();

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var branchId = db.Transactions.AsNoTracking()
            .Where(t => t.ProcessInstanceId == pi.RootProcessId)
            .Select(t => t.CompanyBranchId)
            .FirstOrDefault();
        if (!branchId.HasValue) return false;

        var now = DateTime.UtcNow;

        return db.CompanyBranchUserMappings.AsNoTracking()
            .Where(m => m.CompanyBranchId == branchId.Value
                     && m.ActiveFlag
                     && m.EffectiveStartDate <= now
                     && m.EffectiveEndDate   >= now)
            .Join(db.UserRoleMappings.AsNoTracking(), m => m.UserId, urm => urm.UserId, (m, urm) => urm.RoleId)
            .Join(db.Roles.AsNoTracking(), roleId => roleId, role => role.Id, (roleId, role) => role.Name)
            .Any(name => name == roleName);
    }

    /// <summary>
    /// Returns true when the transaction has been untouched (no
    /// <c>Last_Updated_Date</c> change) for longer than
    /// <paramref name="actionParameter"/> minutes. Falls back to
    /// <c>Created_Date</c> when LastUpdatedDate is null (matches legacy
    /// behaviour where brand-new applications use their creation timestamp).
    /// </summary>
    private bool AppDelay(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (!int.TryParse(actionParameter, out var thresholdMinutes)) return false;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var tx = db.Transactions.AsNoTracking()
            .Where(t => t.ProcessInstanceId == pi.RootProcessId)
            .Select(t => new { t.LastUpdatedDate, t.CreatedDate })
            .FirstOrDefault();
        if (tx is null) return false;

        var reference = tx.LastUpdatedDate ?? tx.CreatedDate;
        return DateTime.UtcNow.Subtract(reference).TotalMinutes > thresholdMinutes;
    }

    /// <summary>
    /// Legacy <c>CheckCreatorRole</c>: returns true when the transaction's
    /// creator holds the role named in <paramref name="actionParameter"/>.
    /// </summary>
    private bool CheckCreatorRole(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (string.IsNullOrWhiteSpace(actionParameter)) return false;
        var roleName = actionParameter.Trim();

        using var scope = _scopes.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleQueryService>();

        var creatorUserId = db.Transactions.AsNoTracking()
            .Where(t => t.ProcessInstanceId == pi.RootProcessId)
            .Select(t => t.UserId)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(creatorUserId)) return false;

        var creatorRoles = roles.GetRoleNamesAsync(creatorUserId, CancellationToken.None).GetAwaiter().GetResult();
        return creatorRoles.Any(rn => string.Equals(rn, roleName, StringComparison.OrdinalIgnoreCase));
    }

    // ============================================================
    // Bucket A — Action implementations (pure runtime)
    // ============================================================

    /// <summary>
    /// Stores the executed activity's name under the scheme-provided
    /// parameter name (lowercased to match legacy convention). The scheme
    /// reads it back via <c>ResetToStateByParameter</c> to revert.
    /// </summary>
    private void SaveCurrentStateInParameter(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (string.IsNullOrWhiteSpace(actionParameter))
        {
            _log.LogWarning("SaveCurrentStateInParameter called on process {ProcessId} with no parameter name — nothing saved.", pi.ProcessId);
            return;
        }

        try
        {
            pi.SetParameter(actionParameter.Trim().ToLowerInvariant(),
                            pi.ExecutedActivity?.Name ?? pi.CurrentActivityName,
                            ParameterPurpose.Persistence);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "SaveCurrentStateInParameter failed for process {ProcessId}, parameter '{Parameter}'.",
                pi.ProcessId, actionParameter);
        }
    }

    /// <summary>
    /// Reads the previously-saved state name from the named parameter and
    /// writes it into the engine's <c>SetStateVariable</c> slot. The
    /// surrounding controller code checks this slot after the transition
    /// completes and calls <c>SetState</c> to actually move the engine.
    /// </summary>
    private void ResetToStateByParameter(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (string.IsNullOrWhiteSpace(actionParameter)) return;

        try
        {
            var resetStateName = pi.GetParameter<string>(actionParameter.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(resetStateName))
            {
                pi.SetParameter(SetStateVariable, resetStateName, ParameterPurpose.Persistence);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "ResetToStateByParameter failed for process {ProcessId}, parameter '{Parameter}'.",
                pi.ProcessId, actionParameter);
        }
    }

    /// <summary>
    /// Walks the children of the current process via
    /// <c>GetProcessInstancesTree</c> and deletes every still-existing
    /// sub-process via <c>DeleteInstance</c>.
    /// </summary>
    private void KillSubProcesses(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        try
        {
            var tree = r.GetProcessInstancesTree(pi.ProcessId);
            if (tree?.Children is null) return;

            foreach (var child in tree.Children)
            {
                if (r.IsProcessExists(child.Id))
                    r.DeleteInstance(child.Id);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KillSubProcesses failed for process {ProcessId}.", pi.ProcessId);
        }
    }

    // ============================================================
    // Bucket B — Action implementations (DB-only)
    // ============================================================

    /// <summary>
    /// Resolves the role parameter to candidate users, INSERTs one
    /// <c>WorkflowInbox</c> row per new user for the current process,
    /// then sets the <c>Approvers</c> process parameter to the current
    /// inbox membership.
    ///
    /// Legacy <c>FillAllUsersBucket</c> behaviour minus location filtering
    /// (deferred to Slice 8 when <c>UserService.GetWorkflowInboxUsers</c>
    /// is ported) and sub-process fan-out (only inserts for current
    /// <c>ProcessId</c>; no <c>RootProcessId</c> duplicate write).
    /// </summary>
    private void FillAllUsersBucket(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        var inserted = FillInboxByRole(pi.ProcessId, actionParameter, out var allInboxUsers);
        if (inserted is null) return;
        TrySetApproversParameter(pi, allInboxUsers, "FillAllUsersBucket");
    }

    /// <summary>
    /// Legacy <c>FillApprovers</c>: same shape as FillAllUsersBucket but
    /// distinct from it semantically — the scheme uses the resulting
    /// <c>Approvers</c> parameter as an approval tracker, where each entry's
    /// bool flips to true as users invoke the <c>Approve</c> action. Once
    /// all entries are true, <c>IsApproveComplete</c> returns true and the
    /// transition fires.
    /// </summary>
    private void FillApprovers(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        var inserted = FillInboxByRole(pi.ProcessId, actionParameter, out var allInboxUsers);
        if (inserted is null) return;
        TrySetApproversParameter(pi, allInboxUsers, "FillApprovers");
    }

    /// <summary>
    /// Legacy <c>Approve</c>: marks the caller as approved in the
    /// <c>Approvers</c> parameter and removes the caller's
    /// <c>WorkflowInbox</c> row for this process.
    /// </summary>
    private void Approve(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        var approvers = ReadApproversParameter(pi);
        if (approvers is null)
        {
            _log.LogWarning("Approve invoked on process {ProcessId} but no 'Approvers' parameter present. Nothing to mark.", pi.ProcessId);
            return;
        }

        var identityId = pi.IdentityId;
        if (string.IsNullOrWhiteSpace(identityId))
        {
            _log.LogWarning("Approve invoked on process {ProcessId} with empty IdentityId. Skipping.", pi.ProcessId);
            return;
        }

        approvers.Approve(identityId);

        try
        {
            pi.SetParameter(ApproversParameterName, approvers, ParameterPurpose.Persistence);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Approve: failed to persist Approvers parameter on process {ProcessId}.", pi.ProcessId);
        }

        if (Guid.TryParse(identityId, out var identityGuid))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var rootProcessId = pi.RootProcessId;
                var rows = db.WorkflowInboxes
                    .Where(i => i.ProcessId == rootProcessId && i.IdentityId == identityGuid)
                    .ToList();
                if (rows.Count > 0)
                {
                    db.WorkflowInboxes.RemoveRange(rows);
                    db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Approve: failed to clear caller inbox on process {ProcessId}.", pi.ProcessId);
            }
        }
    }

    /// <summary>
    /// Deletes <c>WorkflowInbox</c> rows for this process where the row's
    /// <c>IdentityId</c> matches a user holding the named role. Mirrors
    /// legacy <c>WorkflowService.ClearUsersInbox(processId, userIds, rootProcessId)</c>.
    /// </summary>
    private void ClearInboxByRole(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (string.IsNullOrWhiteSpace(actionParameter)) return;
        var roleName = actionParameter.Trim();

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var userIds = db.UserRoleMappings.AsNoTracking()
            .Join(db.Roles.AsNoTracking(), urm => urm.RoleId, role => role.Id, (urm, role) => new { urm.UserId, role.Name })
            .Where(x => x.Name == roleName)
            .Select(x => x.UserId)
            .Distinct()
            .ToList();
        if (userIds.Count == 0) return;

        var identityGuids = userIds
            .Select(u => Guid.TryParse(u, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToHashSet();
        if (identityGuids.Count == 0) return;

        var processId     = pi.ProcessId;
        var rootProcessId = pi.RootProcessId;

        var rows = db.WorkflowInboxes
            .Where(i => (i.ProcessId == processId || i.ProcessId == rootProcessId)
                     && identityGuids.Contains(i.IdentityId))
            .ToList();

        if (rows.Count == 0) return;

        db.WorkflowInboxes.RemoveRange(rows);
        var deleted = db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

        _log.LogInformation(
            "ClearInboxByRole: removed {Deleted} inbox row(s) for role '{Role}' on process {ProcessId}.",
            deleted, roleName, processId);
    }

    /// <summary>
    /// Legacy <c>UpdateApplicationCreatorByCommand</c>: when the current
    /// transition's command matches <paramref name="actionParameter"/>,
    /// rewrites the transaction's <c>User_Id</c> to whichever user is the
    /// new inbox owner. Used by re-assignment commands that hand ownership
    /// of the transaction over to a new responsible party.
    /// </summary>
    private void UpdateApplicationCreatorByCommand(ProcessInstance pi, WorkflowRuntime r, string actionParameter)
    {
        if (string.IsNullOrWhiteSpace(actionParameter)) return;
        if (!string.Equals(pi.CurrentCommand, actionParameter.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var rootProcessId = pi.RootProcessId;

        var newOwnerGuid = db.WorkflowInboxes.AsNoTracking()
            .Where(i => i.ProcessId == rootProcessId)
            .Select(i => i.IdentityId)
            .FirstOrDefault();
        if (newOwnerGuid == Guid.Empty) return;

        var newOwnerUserId = newOwnerGuid.ToString();

        var tx = db.Transactions.FirstOrDefault(t => t.ProcessInstanceId == rootProcessId);
        if (tx is null) return;

        if (string.Equals(tx.UserId, newOwnerUserId, StringComparison.OrdinalIgnoreCase)) return;

        var previousOwner = tx.UserId;
        tx.UserId          = newOwnerUserId;
        tx.LastUpdatedBy   = pi.IdentityId;
        tx.LastUpdatedDate = DateTime.UtcNow;

        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

        _log.LogInformation(
            "UpdateApplicationCreatorByCommand: TmX_Transaction.User_Id moved {From} → {To} for process {ProcessId} (command '{Command}').",
            previousOwner, newOwnerUserId, rootProcessId, pi.CurrentCommand);
    }

    // ============================================================
    // Shared helpers
    // ============================================================

    /// <summary>
    /// Shared inbox-INSERT routine for FillAllUsersBucket / FillApprovers.
    /// Resolves the role parameter, INSERTs new rows, returns the count
    /// inserted and the full list of current inbox identity ids for the
    /// process (used by callers to set the Approvers parameter).
    /// Returns null when the role parameter resolves to no users — caller
    /// short-circuits and skips the Approvers update.
    /// </summary>
    private int? FillInboxByRole(Guid processId, string actionParameter, out List<string> currentInboxUserIds)
    {
        currentInboxUserIds = new List<string>();

        if (string.IsNullOrWhiteSpace(actionParameter))
        {
            _log.LogWarning("FillInboxByRole: empty actionParameter on process {ProcessId}; nothing to do.", processId);
            return null;
        }

        var roleNames = ExtractRoleNames(actionParameter);
        if (roleNames.Count == 0) return null;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var candidateUserIds = db.UserRoleMappings.AsNoTracking()
            .Join(db.Roles.AsNoTracking(), urm => urm.RoleId, role => role.Id, (urm, role) => new { urm.UserId, role.Name })
            .Where(x => roleNames.Contains(x.Name))
            .Select(x => x.UserId)
            .Distinct()
            .ToList();

        if (candidateUserIds.Count == 0)
        {
            _log.LogWarning("FillInboxByRole: no users for roles [{Roles}] on process {ProcessId}.",
                string.Join(", ", roleNames), processId);
            return null;
        }

        var existingIdentityIds = db.WorkflowInboxes.AsNoTracking()
            .Where(i => i.ProcessId == processId)
            .Select(i => i.IdentityId)
            .ToHashSet();

        var newGuids = new List<Guid>();
        foreach (var rawUserId in candidateUserIds)
        {
            if (!Guid.TryParse(rawUserId, out var userGuid)) continue;
            if (userGuid == Guid.Empty) continue;
            if (existingIdentityIds.Contains(userGuid)) continue;
            newGuids.Add(userGuid);
        }

        foreach (var g in newGuids)
        {
            db.WorkflowInboxes.Add(new WorkflowInbox
            {
                Id         = Guid.NewGuid(),
                ProcessId  = processId,
                IdentityId = g
            });
        }

        var inserted = newGuids.Count > 0
            ? db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult()
            : 0;

        currentInboxUserIds = db.WorkflowInboxes.AsNoTracking()
            .Where(i => i.ProcessId == processId)
            .Select(i => i.IdentityId.ToString())
            .Distinct()
            .ToList();

        _log.LogInformation(
            "FillInboxByRole: inserted {Inserted} new row(s) for process {ProcessId} (roles: [{Roles}], total now: {Total}).",
            inserted, processId, string.Join(", ", roleNames), currentInboxUserIds.Count);

        return inserted;
    }

    /// <summary>
    /// Reads the <c>Approvers</c> process parameter and adapts whichever
    /// JSON shape OptimaJet handed back (legacy dictionary wrapper, flat
    /// list, or null) into a typed <see cref="Approvers"/>. Returns null
    /// when the parameter isn't present.
    /// </summary>
    private Approvers? ReadApproversParameter(ProcessInstance pi)
    {
        try
        {
            var typed = pi.GetParameter<Approvers>(ApproversParameterName);
            if (typed is { ApproversDictionary: not null }) return typed;
        }
        catch { /* fall through */ }

        try
        {
            var raw = pi.GetParameter<object>(ApproversParameterName);
            if (raw is null) return null;

            var json = raw is string s ? s : JsonSerializer.Serialize(raw);
            if (string.IsNullOrWhiteSpace(json)) return null;

            if (json.Contains("ApproversDictionary", StringComparison.OrdinalIgnoreCase))
            {
                return JsonSerializer.Deserialize<Approvers>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            var ids = JsonSerializer.Deserialize<List<string>>(json);
            return ids is not null ? new Approvers(ids) : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ReadApproversParameter: failed to read 'Approvers' on process {ProcessId}.", pi.ProcessId);
            return null;
        }
    }

    private void TrySetApproversParameter(ProcessInstance pi, IEnumerable<string> userIds, string actionName)
    {
        try
        {
            var approvers = new Approvers(userIds.ToList());
            pi.SetParameter(ApproversParameterName, approvers, ParameterPurpose.Persistence);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "{Action}: could not set 'Approvers' parameter on process {ProcessId}. The scheme may not declare it as persistent.",
                actionName, pi.ProcessId);
        }
    }

    /// <summary>
    /// Parses the role parameter in legacy formats: JSON array of
    /// <see cref="WorkflowActorRef"/> objects → extract every non-empty
    /// <c>ActorRole</c>; otherwise treat as a single role name string.
    /// </summary>
    private static List<string> ExtractRoleNames(string parameter)
    {
        var trimmed = parameter.Trim();

        if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
        {
            try
            {
                var refs = JsonSerializer.Deserialize<List<WorkflowActorRef>>(trimmed,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (refs is { Count: > 0 })
                {
                    return refs
                        .Where(r => !string.IsNullOrWhiteSpace(r.ActorRole))
                        .Select(r => r.ActorRole!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            catch (JsonException)
            {
                // Fall through to literal-string handling.
            }
        }

        return new List<string> { trimmed };
    }

    private sealed class WorkflowActorRef
    {
        public string? ActorRole { get; set; }
        public string? ActorType { get; set; }
        public string? ActorId   { get; set; }
    }

    /// <summary>
    /// Mirrors legacy <c>TMX.Workflows.Business.Approvers</c>. Same property
    /// shape so JSON round-trips with legacy persisted parameters: serializes
    /// to / deserializes from <c>{ "ApproversDictionary": { "userId": bool } }</c>.
    /// </summary>
    private sealed class Approvers
    {
        public Dictionary<string, bool> ApproversDictionary { get; set; }

        public Approvers() => ApproversDictionary = new Dictionary<string, bool>();

        public Approvers(List<string>? ids) =>
            ApproversDictionary = ids is null
                ? new Dictionary<string, bool>()
                : ids.Distinct().ToDictionary(id => id, id => false);

        public bool IsApproved => ApproversDictionary.Values.All(v => v);

        public void Approve(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            ApproversDictionary[id] = true;
        }
    }
}

using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OptimaJet.Workflow.Core.Model;
using OptimaJet.Workflow.Core.Runtime;

namespace FSI.Trade.Compliance.Infrastructure.Workflow.Rules;

/// <summary>
/// Port of the legacy ICBC <c>WorkflowRule</c> provider. Mirrors the legacy
/// semantics so existing scheme XMLs evaluate identically:
///
/// <list type="bullet">
///   <item><b>IsCreator</b> — caller is the user who created the transaction.
///         Resolved by JOINing <c>TmX_Transaction.User_Id</c> against the
///         <c>Process_Instance_Id</c>.</item>
///   <item><b>CheckRole</b> — caller (a) has the process in their
///         <c>WorkflowInbox</c> AND (b) has the role(s) named in the rule
///         parameter. The parameter may be either a plain role name string
///         or a JSON array of <c>WorkflowActorRef</c> objects (legacy supports
///         both via try-parse).</item>
///   <item><b>Boss</b> — caller has the process in their inbox. Legacy used
///         a supervisor-chain check via reporting-boss data; until that
///         service is ported, the inbox check is the legacy fallback the
///         scheme XML actually relies on for "next-up actor".</item>
/// </list>
///
/// Scoped Application contracts are resolved per call via
/// <see cref="IServiceScopeFactory"/> because the workflow runtime is a
/// singleton and rules fire inside transition evaluation. Each scope is
/// disposed before the rule returns.
/// </summary>
public class FsiWorkflowRuleProvider : IWorkflowRuleProvider
{
    private readonly IServiceScopeFactory               _scopes;
    private readonly ILogger<FsiWorkflowRuleProvider>   _log;

    public FsiWorkflowRuleProvider(IServiceScopeFactory scopes, ILogger<FsiWorkflowRuleProvider> log)
    {
        _scopes = scopes;
        _log    = log;
    }

    // ---------- IWorkflowRuleProvider (v3.5) ----------

    public bool Check(ProcessInstance processInstance, WorkflowRuntime runtime, string identityId, string ruleName, string parameter)
    {
        try
        {
            return ruleName switch
            {
                "IsCreator" => IsCreatorAsync(processInstance.ProcessId, identityId, CancellationToken.None).GetAwaiter().GetResult(),
                "CheckRole" => CheckRoleAsync(processInstance.ProcessId, identityId, parameter, CancellationToken.None).GetAwaiter().GetResult(),
                "Boss"      => VerifyInboxAsync(processInstance.ProcessId, identityId, CancellationToken.None).GetAwaiter().GetResult(),
                _           => throw new NotImplementedException($"Workflow rule '{ruleName}' not implemented.")
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Workflow rule check failed. Process={ProcessId}, Rule={Rule}, Identity={Identity}, Parameter={Parameter}.",
                processInstance.ProcessId, ruleName, identityId, parameter);
            return false;
        }
    }

    public IEnumerable<string> GetIdentities(ProcessInstance processInstance, WorkflowRuntime runtime, string ruleName, string parameter)
    {
        try
        {
            return ruleName switch
            {
                "IsCreator" => GetCreatorAsync(processInstance.ProcessId, CancellationToken.None).GetAwaiter().GetResult(),
                "CheckRole" => GetUsersInRoleAsync(parameter, CancellationToken.None).GetAwaiter().GetResult(),
                "Boss"      => Array.Empty<string>(),
                _           => Array.Empty<string>()
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Workflow rule get-identities failed. Process={ProcessId}, Rule={Rule}, Parameter={Parameter}.",
                processInstance.ProcessId, ruleName, parameter);
            return Array.Empty<string>();
        }
    }

    public List<string> GetRules() => new() { "IsCreator", "CheckRole", "Boss" };

    // ---------- Rule implementations ----------

    /// <summary>
    /// Mirrors legacy <c>WorkflowService.VerifyInbox</c>:
    /// is there a <c>WorkflowInbox</c> row for this (ProcessId, IdentityId)?
    /// </summary>
    private async Task<bool> VerifyInboxAsync(Guid processId, string identityId, CancellationToken ct)
    {
        if (!Guid.TryParse(identityId, out var identityGuid))
            return false;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        return await db.WorkflowInboxes
            .AsNoTracking()
            .AnyAsync(i => i.ProcessId == processId && i.IdentityId == identityGuid, ct);
    }

    /// <summary>
    /// Legacy CheckRole: caller must have the process in their inbox AND
    /// must hold at least one of the role names declared in the parameter.
    /// Parameter is either a plain role name (legacy old-style) or a JSON
    /// array of <see cref="WorkflowActorRef"/> objects (legacy current-style).
    /// </summary>
    private async Task<bool> CheckRoleAsync(Guid processId, string identityId, string parameter, CancellationToken ct)
    {
        // Inbox gate first — matches legacy semantics. The user must have
        // this specific process assigned before they can fire any command.
        if (!await VerifyInboxAsync(processId, identityId, ct))
            return false;

        var roleNames = ExtractRoleNames(parameter);
        if (roleNames.Count == 0)
            return false;

        using var scope = _scopes.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleQueryService>();
        var have  = await roles.GetRoleNamesAsync(identityId, ct);

        return have.Any(r => roleNames.Any(req => string.Equals(r, req, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Returns the set of user IDs that satisfy the CheckRole rule for the
    /// given parameter (i.e. every user with at least one of the named roles).
    /// </summary>
    private async Task<IEnumerable<string>> GetUsersInRoleAsync(string parameter, CancellationToken ct)
    {
        var roleNames = ExtractRoleNames(parameter);
        if (roleNames.Count == 0) return Array.Empty<string>();

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        return await db.UserRoleMappings
            .AsNoTracking()
            .Join(db.Roles.AsNoTracking(),
                  urm => urm.RoleId,
                  r   => r.Id,
                  (urm, r) => new { urm.UserId, r.Name })
            .Where(x => roleNames.Contains(x.Name))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Legacy IsCreator: caller is the user_id stored on the
    /// <c>TmX_Transaction</c> row whose <c>Process_Instance_Id</c> matches
    /// this process. (Legacy used <c>TmX_Workflow_Application_VW.User_Id</c>;
    /// the underlying source is the same column.)
    /// </summary>
    private async Task<bool> IsCreatorAsync(Guid processId, string identityId, CancellationToken ct)
    {
        var creator = await GetTransactionCreatorAsync(processId, ct);
        return !string.IsNullOrWhiteSpace(creator)
            && string.Equals(creator, identityId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IEnumerable<string>> GetCreatorAsync(Guid processId, CancellationToken ct)
    {
        var creator = await GetTransactionCreatorAsync(processId, ct);
        return string.IsNullOrWhiteSpace(creator) ? Array.Empty<string>() : new[] { creator! };
    }

    private async Task<string?> GetTransactionCreatorAsync(Guid processId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        return await db.Transactions
            .AsNoTracking()
            .Where(t => t.ProcessInstanceId == processId)
            .Select(t => t.UserId)
            .FirstOrDefaultAsync(ct);
    }

    // ---------- Parameter parsing ----------

    /// <summary>
    /// Parses the rule parameter in the legacy formats:
    /// <list type="bullet">
    ///   <item>JSON array of <see cref="WorkflowActorRef"/> objects — extract
    ///         every non-empty <c>ActorRole</c>.</item>
    ///   <item>Plain string — treat as a single role name (legacy old-style
    ///         fallback when the JSON parse fails).</item>
    /// </list>
    /// </summary>
    private static List<string> ExtractRoleNames(string? parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
            return new List<string>();

        var trimmed = parameter.Trim();

        // Try JSON first.
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
                // Fall through to the literal-string path.
            }
        }

        // Legacy old-style: parameter is a plain role name.
        return new List<string> { trimmed };
    }

    /// <summary>
    /// Mirrors legacy <c>TMX.Services.ServiceModels.WorkflowActorModel</c>.
    /// Only the fields we use in <see cref="CheckRoleAsync"/> /
    /// <see cref="GetUsersInRoleAsync"/> are kept; the legacy
    /// <c>IsFillVerificationUserInChecklist</c> and <c>ActorId</c> fields
    /// aren't read by our rule logic (yet).
    /// </summary>
    private sealed class WorkflowActorRef
    {
        public string? ActorRole { get; set; }
        public string? ActorType { get; set; }
        public string? ActorId   { get; set; }
    }
}

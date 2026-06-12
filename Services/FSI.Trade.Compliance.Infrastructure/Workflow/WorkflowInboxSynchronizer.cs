using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OptimaJet.Workflow.Core.Model;        // TransitionClassifier
using OptimaJet.Workflow.Core.Persistence;  // ProcessStatus
using OptimaJet.Workflow.Core.Runtime;      // WorkflowRuntime, ProcessStatusChangedEventArgs

namespace FSI.Trade.Compliance.Infrastructure.Workflow;

/// <summary>
/// Port of the legacy <c>WorkflowInit._runtime_ProcessStatusChanged</c> inbox
/// auto-fill (tmx-finance-backend). The custom <c>dbo.WorkflowInbox</c> table —
/// which <see cref="Rules.FsiWorkflowRuleProvider"/>'s <c>CheckRole</c> /
/// <c>Boss</c> rules gate command availability on — is NOT maintained by the
/// engine itself. Legacy kept it in step by subscribing to the runtime's
/// <c>ProcessStatusChanged</c> event: whenever a process pauses awaiting the
/// next command (<see cref="ProcessStatus.Idled"/>) it resolves the actors who
/// can fire the new activity's commands and rewrites the inbox; on
/// <see cref="ProcessStatus.Finalized"/> it drops the inbox.
///
/// We had NOT ported this — so states whose inbox is populated only by this
/// auto-fill (e.g. AuthorizerActivity reached via the AssignToMe claim) ended
/// up with an empty inbox, and <c>GET /Workflow/Process/{id}/Commands</c>
/// returned nothing for the user who should own the step. Registering this
/// handler restores legacy parity.
///
/// Keyed by ProcessId (== RootProcessId for the single-process trade flows in
/// this deployment, which is also what the rule provider checks).
/// </summary>
public sealed class WorkflowInboxSynchronizer
{
    private readonly IServiceScopeFactory                 _scopes;
    private readonly ILogger<WorkflowInboxSynchronizer>   _log;

    public WorkflowInboxSynchronizer(IServiceScopeFactory scopes, ILogger<WorkflowInboxSynchronizer> log)
    {
        _scopes = scopes;
        _log    = log;
    }

    /// <summary>
    /// Runtime <c>ProcessStatusChanged</c> handler. <paramref name="runtime"/> is
    /// passed by the factory's subscription so we can resolve actors and check
    /// process existence.
    /// </summary>
    public void OnProcessStatusChanged(WorkflowRuntime runtime, ProcessStatusChangedEventArgs e)
    {
        try
        {
            // Legacy guard: only act when the process is paused awaiting a
            // command (Idled) or has finalised. Ignore Running / Initialized /
            // Subprocess-noise, missing scheme, and already-deleted processes.
            if ((e.NewStatus != ProcessStatus.Idled && e.NewStatus != ProcessStatus.Finalized)
                || string.IsNullOrEmpty(e.SchemeCode)
                || !runtime.IsProcessExists(e.ProcessId))
            {
                return;
            }

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            if (e.NewStatus == ProcessStatus.Finalized)
            {
                DropInbox(db, e.ProcessId);
                return;
            }

            // Resolve the actors who can act on the new activity's outgoing
            // (Direct) command transitions — same two paths legacy used.
            IEnumerable<string> newActors;
            if (e.ProcessInstance?.ExecutedTransition?.Trigger?.Command?.InputParameters?.Count > 0)
            {
                newActors = runtime.GetAllActorsForCommandTransitions(
                    e.ProcessInstance, new List<TransitionClassifier> { TransitionClassifier.Direct });
            }
            else
            {
                newActors = runtime.GetAllActorsForDirectCommandTransitions(e.ProcessId);
            }

            var actorGuids = (newActors ?? Enumerable.Empty<string>())
                .Where(a => Guid.TryParse(a, out _))
                .Select(Guid.Parse)
                .Distinct()
                .ToHashSet();

            // CLAIM semantics (legacy "Assign to me" — FSI 2026-06-09): when the
            // just-executed command is a CLAIM, the item becomes owned by the
            // single authorizer who claimed it and must DROP OUT of every other
            // pool member's inbox. The generic actor resolution above returns the
            // whole role pool (e.g. all Authorizers), so on a claim we narrow the
            // inbox to just the claimer (the executor of the AssignToMe command).
            // Non-claim transitions (SendToAuthorizer → pool, SendBackToAnalyst →
            // analyst pool, etc.) keep the full resolved actor set.
            var executedCommand =
                e.ProcessInstance?.ExecutedTransition?.Trigger?.Command?.Name
                ?? e.ProcessInstance?.CurrentCommand;

            if (IsClaimCommand(executedCommand)
                && Guid.TryParse(e.ProcessInstance?.IdentityId, out var claimerGuid)
                && claimerGuid != Guid.Empty)
            {
                _log.LogInformation(
                    "Inbox sync: '{Command}' claim by {Claimer} on ProcessId={ProcessId} — locking inbox to claimer (was {PoolSize} pool member(s)).",
                    executedCommand, claimerGuid, e.ProcessId, actorGuids.Count);
                actorGuids = new HashSet<Guid> { claimerGuid };
            }

            SyncInbox(db, e.ProcessId, actorGuids);
        }
        catch (Exception ex)
        {
            // Never let an inbox-sync failure bubble into the engine's
            // transition pipeline — log and move on (legacy SaveWorkflowError).
            _log.LogError(ex, "Inbox sync failed for ProcessId={ProcessId}.", e.ProcessId);
        }
    }

    /// <summary>
    /// Commands that CLAIM a pooled item to a single owner. When one of these
    /// fires, the inbox is locked to the executing user so the item leaves every
    /// other pool member's queue (legacy "Assign to me" behaviour). Matched
    /// case-insensitively against the executed command name.
    /// </summary>
    private static readonly HashSet<string> ClaimCommands =
        new(StringComparer.OrdinalIgnoreCase) { "AssignToMe" };

    private static bool IsClaimCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command) && ClaimCommands.Contains(command.Trim());

    private void SyncInbox(IApplicationDbContext db, Guid processId, HashSet<Guid> actorGuids)
    {
        var existing = db.WorkflowInboxes.Where(i => i.ProcessId == processId).ToList();
        var existingIds = existing.Select(i => i.IdentityId).ToHashSet();

        // Remove rows whose user is no longer an actor for the current activity.
        var stale = existing.Where(i => !actorGuids.Contains(i.IdentityId)).ToList();
        if (stale.Count > 0) db.WorkflowInboxes.RemoveRange(stale);

        // Add rows for newly-eligible actors.
        foreach (var g in actorGuids.Where(g => g != Guid.Empty && !existingIds.Contains(g)))
        {
            db.WorkflowInboxes.Add(new WorkflowInbox
            {
                Id         = Guid.NewGuid(),
                ProcessId  = processId,
                IdentityId = g
            });
        }

        var changes = db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (changes > 0)
        {
            _log.LogInformation(
                "Inbox sync: ProcessId={ProcessId} now queued to {Count} actor(s) (+{Added}/-{Removed}).",
                processId, actorGuids.Count,
                actorGuids.Count(g => !existingIds.Contains(g)), stale.Count);
        }
    }

    private void DropInbox(IApplicationDbContext db, Guid processId)
    {
        var rows = db.WorkflowInboxes.Where(i => i.ProcessId == processId).ToList();
        if (rows.Count == 0) return;
        db.WorkflowInboxes.RemoveRange(rows);
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        _log.LogInformation("Inbox sync: dropped {Count} row(s) for finalised ProcessId={ProcessId}.", rows.Count, processId);
    }
}

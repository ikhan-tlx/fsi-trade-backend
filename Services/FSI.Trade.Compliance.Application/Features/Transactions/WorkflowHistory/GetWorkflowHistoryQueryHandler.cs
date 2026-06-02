using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Transactions.WorkflowHistory;

/// <summary>
/// Builds the workflow-history feed for a transaction by projecting
/// <c>WorkflowProcessTransitionHistory</c> rows tied to the transaction's
/// process instance.
///
/// Algorithm:
/// <list type="number">
///   <item>Load the transaction; resolve <c>ProcessInstanceId</c>.</item>
///   <item>Pull every transition row for that ProcessId, sorted by
///         <c>TransitionTime</c> ASC, excluding <c>SetState</c> triggers
///         (same convention as the legacy inbox view).</item>
///   <item>Resolve executor IDs to TmX_User display names in a single
///         IN-list query — avoids the N+1 the legacy view used.</item>
///   <item>Emit one DTO per transition. The current stage's
///         <c>exitedAt</c> is null; the latest transition's
///         <c>ToActivityName</c> is the current stage.</item>
/// </list>
///
/// Edge case: if the transaction has no <c>ProcessInstanceId</c> (workflow
/// never attached or has been detached), returns an empty list — the FE
/// renders "No history" in that case.
/// </summary>
public class GetWorkflowHistoryQueryHandler
    : IRequestHandler<GetWorkflowHistoryQuery, IReadOnlyList<WorkflowHistoryEntryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetWorkflowHistoryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkflowHistoryEntryDto>> Handle(
        GetWorkflowHistoryQuery req,
        CancellationToken      ct)
    {
        // 1. Resolve transaction → process instance id.
        var tx = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionId == req.TransactionId)
            .Select(t => new { t.TransactionId, t.ProcessInstanceId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(
                "transaction_not_found",
                $"Transaction {req.TransactionId} does not exist.");

        if (!tx.ProcessInstanceId.HasValue)
            return Array.Empty<WorkflowHistoryEntryDto>();

        var processId = tx.ProcessInstanceId.Value;

        // 2. Pull transition rows. Filter SetState exactly the way the
        //    legacy inbox view does (CheckRole / Boss rule providers ignore
        //    SetState rows when building the actor list — staying consistent
        //    here keeps the FE's history view aligned with what those rules
        //    consider "real" activity).
        var transitions = await _db.WorkflowProcessTransitionHistories.AsNoTracking()
            .Where(h => h.ProcessId == processId)
            .Where(h => h.TriggerName == null || h.TriggerName != "SetState")
            .OrderBy(h => h.TransitionTime)
            .Select(h => new
            {
                h.ExecutorIdentityId,
                h.ToActivityName,
                h.ToStateName,
                h.TransitionTime,
                h.TriggerName
            })
            .ToListAsync(ct);

        if (transitions.Count == 0) return Array.Empty<WorkflowHistoryEntryDto>();

        // 3. Resolve executor IDs to display names. Single IN-list query;
        //    null/empty executors stay null in the output.
        var executorIds = transitions
            .Where(t => !string.IsNullOrWhiteSpace(t.ExecutorIdentityId))
            .Select(t => t.ExecutorIdentityId!)
            .Distinct()
            .ToList();

        var userById = executorIds.Count == 0
            ? new Dictionary<string, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => executorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
                .ToDictionaryAsync(
                    u => u.Id,
                    u => ComposeDisplayName(u.FirstName, u.LastName, u.UserName),
                    ct);

        // 4. Build the result. exitedAt = next row's TransitionTime (or null
        //    for the latest entry).
        var result = new List<WorkflowHistoryEntryDto>(transitions.Count);
        for (int i = 0; i < transitions.Count; i++)
        {
            var current  = transitions[i];
            var nextTime = i + 1 < transitions.Count ? transitions[i + 1].TransitionTime : (DateTime?)null;

            string? actorName = null;
            if (!string.IsNullOrWhiteSpace(current.ExecutorIdentityId)
                && userById.TryGetValue(current.ExecutorIdentityId!, out var name))
            {
                actorName = name;
            }

            result.Add(new WorkflowHistoryEntryDto
            {
                step        = i + 1,
                stageName   = current.ToActivityName ?? "",
                stateName   = current.ToStateName,
                actorId     = current.ExecutorIdentityId,
                actorName   = actorName,
                triggerName = current.TriggerName,
                enteredAt   = current.TransitionTime,
                exitedAt    = nextTime,
                isCurrent   = !nextTime.HasValue
            });
        }

        return result;
    }

    /// <summary>
    /// Builds a display name for the actor cell. Prefers "First Last",
    /// falls back to the username (TmX_User.User_Name) when both parts
    /// are blank — same shape the inbox view uses.
    /// </summary>
    private static string ComposeDisplayName(string? firstName, string? lastName, string? userName)
    {
        var first = (firstName ?? "").Trim();
        var last  = (lastName  ?? "").Trim();

        if (first.Length > 0 || last.Length > 0)
            return (first + " " + last).Trim();

        return userName ?? "";
    }
}

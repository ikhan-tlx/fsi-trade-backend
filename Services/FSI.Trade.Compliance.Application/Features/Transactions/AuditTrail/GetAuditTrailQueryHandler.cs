using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Transactions.AuditTrail;

/// <summary>
/// Composes the audit trail by merging lifecycle stamps + workflow
/// transitions + remarks. No new audit table per Slice 10 design
/// (user direction: "workflow related table contains the audit level
/// that who assign who and who took an action").
///
/// Actor resolution is a single batched IN-list query against TmX_User
/// covering every actor id mentioned across the three sources, so the
/// handler is N+3 (transaction, transitions, remarks, users) rather than
/// N+rows.
/// </summary>
public class GetAuditTrailQueryHandler
    : IRequestHandler<GetAuditTrailQuery, IReadOnlyList<AuditTrailEntryDto>>
{
    /// <summary>Module_Code value identifying "Transaction" rows in shared application tables — matches GetTransactionByIdQueryHandler.</summary>
    public const string TransactionModuleCode = "5";

    private readonly IApplicationDbContext _db;

    public GetAuditTrailQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditTrailEntryDto>> Handle(
        GetAuditTrailQuery req,
        CancellationToken  ct)
    {
        // 1. Load transaction lifecycle stamps.
        var tx = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionId == req.TransactionId)
            .Select(t => new
            {
                t.TransactionId,
                t.ProcessInstanceId,
                t.CreatedDate,
                t.CreatedBy,
                t.LastUpdatedDate,
                t.LastUpdatedBy
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(
                "transaction_not_found",
                $"Transaction {req.TransactionId} does not exist.");

        // 2. Workflow transitions (may be empty if no scheme attached).
        var transitions = new List<TransitionRow>();
        if (tx.ProcessInstanceId.HasValue)
        {
            transitions = await _db.WorkflowProcessTransitionHistories.AsNoTracking()
                .Where(h => h.ProcessId == tx.ProcessInstanceId.Value)
                .Where(h => h.TriggerName == null || h.TriggerName != "SetState")
                .Select(h => new TransitionRow
                {
                    ExecutorIdentityId = h.ExecutorIdentityId,
                    FromActivityName   = h.FromActivityName,
                    ToActivityName     = h.ToActivityName,
                    TriggerName        = h.TriggerName,
                    TransitionTime     = h.TransitionTime
                })
                .ToListAsync(ct);
        }

        // 3. Remarks (Module_Code = "5").
        var remarks = await _db.ApplicationRemarks.AsNoTracking()
            .Where(r => r.TransactionId == req.TransactionId && r.ModuleCode == TransactionModuleCode)
            .Select(r => new
            {
                r.ApplicationRemarkId,
                r.UserId,
                r.CreatedBy,
                r.CreatedDate,
                r.ActionType,
                r.Comments
            })
            .ToListAsync(ct);

        // 4. Batch-resolve all distinct actor IDs in one query.
        //    Sources: tx.CreatedBy, tx.LastUpdatedBy, transition executors,
        //    remark UserIds. CreatedBy / LastUpdatedBy already store the
        //    USERNAME, not the user_id — the legacy convention is to use
        //    `CreatedBy/LastUpdatedBy = User_Name` for audit display.
        //    Transition executors and remark.UserId store the user_id (Guid).
        var actorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in transitions)
            if (!string.IsNullOrWhiteSpace(t.ExecutorIdentityId)) actorIds.Add(t.ExecutorIdentityId!);
        foreach (var r in remarks)
            if (!string.IsNullOrWhiteSpace(r.UserId)) actorIds.Add(r.UserId);

        var nameByUserId = actorIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (await _db.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
                .ToListAsync(ct))
                .ToDictionary(
                    u => u.Id,
                    u => ComposeDisplayName(u.FirstName, u.LastName, u.UserName),
                    StringComparer.OrdinalIgnoreCase);

        // 5. Build event list.
        var entries = new List<AuditTrailEntryDto>(2 + transitions.Count + remarks.Count);

        entries.Add(new AuditTrailEntryDto
        {
            eventType = "Created",
            actorName = tx.CreatedBy,   // CreatedBy stores the username verbatim
            timestamp = tx.CreatedDate
        });

        // Suppress "Updated" when LastUpdatedDate matches CreatedDate exactly
        // (common pattern: rows inserted with both stamps equal). Also skip
        // when LastUpdatedDate is null.
        if (tx.LastUpdatedDate.HasValue && tx.LastUpdatedDate.Value != tx.CreatedDate)
        {
            entries.Add(new AuditTrailEntryDto
            {
                eventType = "Updated",
                actorName = tx.LastUpdatedBy,
                timestamp = tx.LastUpdatedDate.Value
            });
        }

        foreach (var t in transitions)
        {
            string? actorName = null;
            if (!string.IsNullOrWhiteSpace(t.ExecutorIdentityId)
                && nameByUserId.TryGetValue(t.ExecutorIdentityId!, out var resolved))
            {
                actorName = resolved;
            }

            entries.Add(new AuditTrailEntryDto
            {
                eventType   = "Transition",
                action      = t.TriggerName,
                actorId     = t.ExecutorIdentityId,
                actorName   = actorName,
                timestamp   = t.TransitionTime,
                stageChange = $"{t.FromActivityName} → {t.ToActivityName}"
            });
        }

        foreach (var r in remarks)
        {
            string? actorName = null;
            if (!string.IsNullOrWhiteSpace(r.UserId)
                && nameByUserId.TryGetValue(r.UserId, out var resolved))
            {
                actorName = resolved;
            }
            // Fallback to the legacy CreatedBy username when User_ID didn't
            // resolve (e.g. system-generated note).
            actorName ??= r.CreatedBy;

            entries.Add(new AuditTrailEntryDto
            {
                eventType = "NoteAdded",
                action    = r.ActionType,
                actorId   = r.UserId,
                actorName = actorName,
                timestamp = r.CreatedDate,
                remarks   = r.Comments
            });
        }

        // 6. Sort chronologically. Stable secondary sort by event-source
        //    so coincident timestamps fall in a predictable order.
        return entries
            .OrderBy(e => e.timestamp)
            .ThenBy(e => SourceOrder(e.eventType))
            .ToList();
    }

    private static int SourceOrder(string eventType) => eventType switch
    {
        "Created"    => 0,
        "Updated"    => 1,
        "Transition" => 2,
        "NoteAdded"  => 3,
        _            => 9
    };

    private static string ComposeDisplayName(string? firstName, string? lastName, string? userName)
    {
        var first = (firstName ?? "").Trim();
        var last  = (lastName  ?? "").Trim();
        if (first.Length > 0 || last.Length > 0)
            return (first + " " + last).Trim();
        return userName ?? "";
    }

    private sealed class TransitionRow
    {
        public string?  ExecutorIdentityId { get; set; }
        public string   FromActivityName   { get; set; } = "";
        public string   ToActivityName     { get; set; } = "";
        public string?  TriggerName        { get; set; }
        public DateTime TransitionTime     { get; set; }
    }
}

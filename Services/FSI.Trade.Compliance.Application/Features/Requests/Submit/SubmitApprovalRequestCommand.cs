using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Requests.Common;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Requests.Submit;

/// <summary>
/// Maker side of maker/checker: parks a proposed change as a PENDING
/// <c>TmX_Request</c> row instead of committing it. The exact command payload the
/// write handler expects is stored verbatim in <c>Entity_Json</c>; on approval
/// <see cref="SubmitRequestCommand"/> replays it through the real handler. Nothing
/// is applied to the live entity here — the whole point of maker/checker.
///
/// POST /api/v1/Request/Submit. The controller enforces that the maker holds the
/// underlying entity's create/update privilege before this runs.
/// </summary>
public class SubmitApprovalRequestCommand : IRequest<long>
{
    public string?     RequestTypeCode  { get; set; }   // 0001 User, 0002 Lookup, 0003 Role, 0004 Branch, 0005 Auto Flag
    public string?     EntityStatusCode { get; set; }   // 0001 Create / 0002 Update / 0003 Delete / 0004 Deactivate
    public string?     EntityId         { get; set; }   // target row id (updates / deletes / deactivates)
    public JsonElement Payload          { get; set; }   // the write-command body the maker would have POSTed
}

public class SubmitApprovalRequestCommandHandler : IRequestHandler<SubmitApprovalRequestCommand, long>
{
    private const string PendingStatus = "0011";

    private readonly IApplicationDbContext   _db;
    private readonly ICurrentUserService     _current;
    private readonly IApprovalReplayRegistry _registry;

    public SubmitApprovalRequestCommandHandler(
        IApplicationDbContext db, ICurrentUserService current, IApprovalReplayRegistry registry)
    { _db = db; _current = current; _registry = registry; }

    public async Task<long> Handle(SubmitApprovalRequestCommand req, CancellationToken ct)
    {
        var type   = req.RequestTypeCode?.Trim();
        var action = req.EntityStatusCode?.Trim();

        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(action))
            throw new ConflictException("request_type_required", "A request type and action are required.");
        if (!_registry.IsSupported(type, action))
            throw new ConflictException("request_type_unsupported",
                $"Maker/checker is not configured for type '{type}' action '{action}'.");

        var json = req.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}"
            : req.Payload.GetRawText();

        // Fail fast: prove the payload deserialises into the target command NOW so
        // the maker gets immediate feedback rather than a surprise at approval time.
        _ = _registry.BuildReplayCommand(type, action, req.EntityId, json);

        // One pending request per entity (updates / deletes / deactivates) — a maker
        // can't stack two competing changes on the same record.
        if (!string.IsNullOrWhiteSpace(req.EntityId))
        {
            var alreadyPending = await _db.Requests.AsNoTracking().AnyAsync(r =>
                r.RequestTypeCode   == type &&
                r.EntityId          == req.EntityId &&
                r.RequestStatusCode == PendingStatus, ct);
            if (alreadyPending)
                throw new ConflictException("request_already_pending",
                    "A change request for this record is already awaiting approval.");
        }

        var now = DateTime.UtcNow;
        var who = _current.UserName ?? _current.UserId ?? "unknown";

        var row = new Request
        {
            TenantId          = _current.TenantId ?? 1,
            RequestTypeCode   = type,
            EntityStatusCode  = action,
            RequestStatusCode = PendingStatus,
            EntityId          = req.EntityId,
            EntityJson        = json,
            CommandKey        = $"{type}:{action}",   // marks this row as command-shaped → replayable on approve
            CreatedBy         = who,
            CreatedDate       = now,
        };
        _db.Requests.Add(row);
        await _db.SaveChangesAsync(ct);   // audit interceptor records this as SUBMIT (by the maker)
        return row.RequestId;
    }
}

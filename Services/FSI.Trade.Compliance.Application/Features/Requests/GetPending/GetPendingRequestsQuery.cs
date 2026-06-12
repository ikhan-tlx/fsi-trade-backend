using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Requests.GetPending;

/// <summary>
/// Pending maker/checker requests for an entity (e.g. a user awaiting approval),
/// keyed by request-type code + entity id. Slice 14 — backs the FE's
/// "is there a pending request?" guard. Returns rows whose status is NOT a
/// final (approved/rejected) state.
/// </summary>
public record GetPendingRequestsQuery(string RequestType, string EntityId)
    : IRequest<IReadOnlyList<PendingRequestDto>>;

public class PendingRequestDto
{
    public long     requestId         { get; set; }
    public string?  requestTypeCode   { get; set; }
    public string?  requestStatusCode { get; set; }
    public string?  entityStatusCode  { get; set; }
    public string?  entityId          { get; set; }
    public string?  createdBy         { get; set; }
    public DateTime createdDate       { get; set; }
}

public class GetPendingRequestsQueryHandler
    : IRequestHandler<GetPendingRequestsQuery, IReadOnlyList<PendingRequestDto>>
{
    // Final states are excluded from "pending". Codes vary by deployment, so we
    // match common approved/rejected markers case-insensitively.
    private static readonly string[] FinalStatuses =
        { "A", "R", "APPROVED", "REJECTED", "CLOSED", "COMPLETE", "COMPLETED" };

    private readonly IApplicationDbContext _db;
    public GetPendingRequestsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PendingRequestDto>> Handle(GetPendingRequestsQuery req, CancellationToken ct)
    {
        var entityId = (req.EntityId ?? "").Trim();
        if (entityId.Length == 0) return new List<PendingRequestDto>();

        var rows = await _db.Requests
            .AsNoTracking()
            .Where(r => r.EntityId == entityId
                     && (string.IsNullOrEmpty(req.RequestType) || r.RequestTypeCode == req.RequestType))
            .Select(r => new PendingRequestDto
            {
                requestId         = r.RequestId,
                requestTypeCode   = r.RequestTypeCode,
                requestStatusCode = r.RequestStatusCode,
                entityStatusCode  = r.EntityStatusCode,
                entityId          = r.EntityId,
                createdBy         = r.CreatedBy,
                createdDate       = r.CreatedDate
            })
            .ToListAsync(ct);

        return rows
            .Where(r => string.IsNullOrWhiteSpace(r.requestStatusCode)
                     || !FinalStatuses.Contains(r.requestStatusCode!.Trim().ToUpperInvariant()))
            .ToList();
    }
}

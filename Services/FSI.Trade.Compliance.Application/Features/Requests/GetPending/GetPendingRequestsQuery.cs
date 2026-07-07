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
    // Bug 2026-06-XX — the original filter excluded a hardcoded "final states"
    // set ({"A","R","APPROVED","REJECTED","CLOSED","COMPLETE","COMPLETED"}),
    // but the actual codes stored in TmX_Request.Request_Status_Code in this
    // deployment are numeric-string tokens:
    //   "0011" = New (pending)
    //   "0012" = Approved (final)
    //   "0013" = Rejected (final)
    // "0012" / "0013" didn't match the exclusion list, so historical
    // approved/rejected rows leaked through as "pending", blocking edits on
    // users that had ANY prior request activity. Inverted the check to be
    // inclusion-based against the pending code — the only code that means
    // "still awaiting a checker" — so anything else (approved, rejected,
    // future terminal codes, garbage) is treated as final.
    //
    // Codes verified against tmx-finance-frontend-revamp/src/features/requests/
    // constants/requests-constants.ts (REQUEST_STATUSES).
    private const string PendingStatusCode = "0011";

    /// <summary>
    /// Additional alias tokens that mean "pending" — kept generous so an
    /// operator populating TmX_Request with a friendlier value (e.g.
    /// "NEW" / "PENDING") doesn't silently break the guard. The canonical
    /// value stored in this deployment is <see cref="PendingStatusCode"/>.
    /// </summary>
    private static readonly HashSet<string> PendingAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "0011",
            "NEW",
            "PENDING",
            "OPEN",
            "N",
            "P"
        };

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

        // Only true pending rows count. An unrecognised status is treated as
        // final so we err on the side of "let the edit through" rather than
        // "block spuriously" (which was the original bug).
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.requestStatusCode)
                     && PendingAliases.Contains(r.requestStatusCode!.Trim()))
            .ToList();
    }
}

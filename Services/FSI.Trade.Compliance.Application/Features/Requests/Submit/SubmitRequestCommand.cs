using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Requests.Submit;

/// <summary>
/// Checker decision on a maker/checker request (the Requests detail page's
/// Submit button). Slice 14. Records the chosen RequestStatusCode (approve /
/// reject / etc.) and stamps the executing checker on the row
/// (Last_Updated_By / Date).
///
/// Scope note: this transitions the REQUEST's status only. Applying the
/// approved Entity_Json to the underlying entity (auto-creating/updating the
/// user/role/branch on approval) is a separate workflow step not yet wired —
/// the FE renders the entity read-only and only sends the status decision.
/// The command accepts the full FE body but acts on RequestId + the new status.
/// </summary>
public class SubmitRequestCommand : IRequest<long>
{
    public long     RequestId         { get; set; }
    public string?  RequestTypeCode   { get; set; }
    public string?  EntityStatusCode  { get; set; }
    public string?  RequestStatusCode { get; set; }   // the checker's decision
    public string?  EntityId          { get; set; }
}

public class SubmitRequestCommandHandler : IRequestHandler<SubmitRequestCommand, long>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public SubmitRequestCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<long> Handle(SubmitRequestCommand req, CancellationToken ct)
    {
        if (req.RequestId <= 0)
            throw new ConflictException("request_id_required", "A valid RequestId is required.");
        if (string.IsNullOrWhiteSpace(req.RequestStatusCode))
            throw new ConflictException("request_status_required", "A RequestStatusCode (decision) is required.");

        var entity = await _db.Requests.FirstOrDefaultAsync(x => x.RequestId == req.RequestId, ct)
                     ?? throw new NotFoundException("request_not_found", $"Request '{req.RequestId}' not found.");

        entity.RequestStatusCode = req.RequestStatusCode!.Trim();
        entity.LastUpdatedBy     = _current.UserName ?? _current.UserId ?? "unknown";
        entity.LastUpdatedDate   = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return entity.RequestId;
    }
}

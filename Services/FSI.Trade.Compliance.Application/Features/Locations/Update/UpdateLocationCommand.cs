using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Locations.Update;

/// <summary>Updates the mutable fields of a location. Slice 14.</summary>
public class UpdateLocationCommand : IRequest<Unit>
{
    public int       LocationId         { get; set; }   // from route
    public string?   LocationName       { get; set; }
    public int?      ParentLocationId   { get; set; }
    public int?      LocationTypeLkpId  { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
}

public class UpdateLocationCommandHandler : IRequestHandler<UpdateLocationCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public UpdateLocationCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<Unit> Handle(UpdateLocationCommand req, CancellationToken ct)
    {
        var location = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == req.LocationId, ct)
            ?? throw new NotFoundException("location_not_found", $"Location {req.LocationId} not found.");

        if (req.LocationName != null)       location.LocationName      = req.LocationName.Trim();
        if (req.ParentLocationId != null)   location.ParentLocationId  = req.ParentLocationId;
        if (req.LocationTypeLkpId != null)  location.LocationTypeLkpId  = req.LocationTypeLkpId.Value;
        if (req.EffectiveStartDate != null) location.EffectiveStartDate = req.EffectiveStartDate.Value;
        if (req.EffectiveEndDate != null)   location.EffectiveEndDate   = req.EffectiveEndDate.Value;

        location.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        location.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

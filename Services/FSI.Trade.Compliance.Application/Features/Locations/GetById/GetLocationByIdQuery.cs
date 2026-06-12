using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Locations.List;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Locations.GetById;

/// <summary>Single location by id, for the edit form. Slice 14.</summary>
public record GetLocationByIdQuery(int Id) : IRequest<LocationListItemDto>;

public class GetLocationByIdQueryHandler : IRequestHandler<GetLocationByIdQuery, LocationListItemDto>
{
    private readonly IApplicationDbContext _db;
    public GetLocationByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<LocationListItemDto> Handle(GetLocationByIdQuery req, CancellationToken ct)
    {
        return await _db.Locations.AsNoTracking()
            .Where(l => l.Id == req.Id)
            .Select(l => new LocationListItemDto
            {
                locationId         = l.Id,
                locationCode       = l.LocationCode,
                locationName       = l.LocationName,
                parentLocationId   = l.ParentLocationId,
                locationTypeLkpId  = l.LocationTypeLkpId,
                status             = l.ActiveFlag ? "Active" : "Inactive",
                activeFlag         = l.ActiveFlag,
                effectiveStartDate = l.EffectiveStartDate,
                effectiveEndDate   = l.EffectiveEndDate
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("location_not_found", $"Location {req.Id} not found.");
    }
}

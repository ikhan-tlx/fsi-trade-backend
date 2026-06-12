using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Locations.Lov;

/// <summary>
/// Flat list of active locations for dropdowns (parent-location selector on
/// the Location form, location filters elsewhere). LOV shape mirrors the other
/// /lov endpoints.
/// </summary>
public record ListLocationsLovQuery : IRequest<IReadOnlyList<LocationLovItemDto>>;

public class LocationLovItemDto
{
    public int     hiddenValue      { get; set; }   // Location_ID
    public string? visibleValue     { get; set; }   // Location_Name
    public string? locationCode     { get; set; }
    public int?    parentLocationId { get; set; }
}

public class ListLocationsLovQueryHandler
    : IRequestHandler<ListLocationsLovQuery, IReadOnlyList<LocationLovItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListLocationsLovQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<LocationLovItemDto>> Handle(ListLocationsLovQuery req, CancellationToken ct)
    {
        return await _db.Locations
            .AsNoTracking()
            .Where(l => l.ActiveFlag)
            .OrderBy(l => l.LocationName)
            .Select(l => new LocationLovItemDto
            {
                hiddenValue      = l.Id,
                visibleValue     = l.LocationName,
                locationCode     = l.LocationCode,
                parentLocationId = l.ParentLocationId
            })
            .ToListAsync(ct);
    }
}

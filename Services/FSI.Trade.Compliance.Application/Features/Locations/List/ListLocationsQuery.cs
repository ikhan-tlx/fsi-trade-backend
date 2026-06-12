using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Locations.List;

/// <summary>
/// Paged admin list of locations for the FE "Locations" management grid
/// (<c>GET /api/v1/Location?page=…&amp;pageSize=…&amp;sort=…&amp;filter=…</c>).
///
/// Slice 14 — the new BE had no LocationController at all; this is the first
/// read surface over <c>TmX_Location</c> for the setup-data screen. Mirrors
/// the User / Role paged pattern (inherits <see cref="PagedQuery"/>).
/// </summary>
public class ListLocationsQuery : PagedQuery, IRequest<PagedResult<LocationListItemDto>>
{
}

public class LocationListItemDto
{
    public int       locationId         { get; set; }
    public string    locationCode       { get; set; } = "";
    public string?   locationName       { get; set; }
    public int?      parentLocationId   { get; set; }
    public string?   parentLocationName { get; set; }
    public int       locationTypeLkpId  { get; set; }
    public string?   status             { get; set; }
    public bool      activeFlag         { get; set; }
    public DateTime  effectiveStartDate { get; set; }
    public DateTime  effectiveEndDate   { get; set; }
}

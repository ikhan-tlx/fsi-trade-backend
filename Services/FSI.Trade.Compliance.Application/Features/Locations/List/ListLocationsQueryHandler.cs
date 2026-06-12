using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Locations.List;

/// <summary>
/// Server-side paged / sorted / filtered list over <c>TmX_Location</c>.
/// The location tree is small (&lt;500 rows in this deployment) so the parent
/// name is resolved via a one-shot id→name map rather than a join.
/// </summary>
public class ListLocationsQueryHandler
    : IRequestHandler<ListLocationsQuery, PagedResult<LocationListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListLocationsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<LocationListItemDto>> Handle(
        ListLocationsQuery req,
        CancellationToken ct)
    {
        IQueryable<Location> q = _db.Locations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            q = q.Where(l =>
                   EF.Functions.Like(l.LocationCode, $"%{f}%")
                || (l.LocationName != null && EF.Functions.Like(l.LocationName, $"%{f}%")));
        }

        q = ApplySort(q, req.Sort);

        var total = await q.CountAsync(ct);

        var page = await q
            .Skip(req.Skip)
            .Take(req.Take)
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
            .ToListAsync(ct);

        // Resolve parent names for the rows that have a parent (small table).
        var parentIds = page.Where(r => r.parentLocationId != null)
                            .Select(r => r.parentLocationId!.Value)
                            .Distinct()
                            .ToList();
        if (parentIds.Count > 0)
        {
            var names = await _db.Locations.AsNoTracking()
                .Where(l => parentIds.Contains(l.Id))
                .Select(l => new { l.Id, l.LocationName })
                .ToDictionaryAsync(x => x.Id, x => x.LocationName, ct);

            foreach (var r in page)
                if (r.parentLocationId != null && names.TryGetValue(r.parentLocationId.Value, out var nm))
                    r.parentLocationName = nm;
        }

        return new PagedResult<LocationListItemDto>
        {
            items    = page,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private static IQueryable<Location> ApplySort(IQueryable<Location> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return q.OrderBy(l => l.LocationName);

        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
        if (first is null) return q.OrderBy(l => l.LocationName);

        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var desc  = string.Equals(parts.ElementAtOrDefault(1), "desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "locationname"      => desc ? q.OrderByDescending(l => l.LocationName) : q.OrderBy(l => l.LocationName),
            "locationcode"      => desc ? q.OrderByDescending(l => l.LocationCode) : q.OrderBy(l => l.LocationCode),
            "locationtypelkpid" => desc ? q.OrderByDescending(l => l.LocationTypeLkpId) : q.OrderBy(l => l.LocationTypeLkpId),
            "locationid"        => desc ? q.OrderByDescending(l => l.Id) : q.OrderBy(l => l.Id),
            _                   => q.OrderBy(l => l.LocationName)
        };
    }
}

using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Products.Tabs;

/// <summary>
/// Reads active entity-tab-product mappings and caches the entire list for 5
/// minutes. The dataset is small (hundreds of rows) and changes rarely; this
/// avoids hammering the DB on every transaction page open.
/// </summary>
public class ListEntityTabMappingsQueryHandler
    : IRequestHandler<ListEntityTabMappingsQuery, IReadOnlyList<EntityTabProductMappingDto>>
{
    private const string CacheKey = "Products::EntityTabMappings::v1";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache          _cache;

    public ListEntityTabMappingsQueryHandler(IApplicationDbContext db, IMemoryCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<EntityTabProductMappingDto>> Handle(ListEntityTabMappingsQuery req, CancellationToken ct)
    {
        if (_cache.TryGetValue<IReadOnlyList<EntityTabProductMappingDto>>(CacheKey, out var hit) && hit is not null)
            return hit;

        var rows = await _db.EntityTabProductMappings
            .AsNoTracking()
            .Where(m => m.IsActive == 1)
            .OrderBy(m => m.ProductId)
            .ThenBy(m => m.SortOrder)
            .Select(m => new EntityTabProductMappingDto
            {
                id          = m.Id,
                tenantId    = m.TenantId,
                entityId    = m.EntityId,
                productId   = m.ProductId,
                tabId       = m.TabId,
                parentTabId = m.ParentTabId,
                sortOrder   = m.SortOrder
            })
            .ToListAsync(ct);

        _cache.Set(CacheKey, (IReadOnlyList<EntityTabProductMappingDto>)rows, Ttl);
        return rows;
    }
}

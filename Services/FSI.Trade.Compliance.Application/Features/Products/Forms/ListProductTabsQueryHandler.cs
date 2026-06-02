using FSI.Trade.Compliance.Application.Common.Caching;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Products.Forms;

/// <summary>
/// Pulls every active <c>EntityTabProductMapping</c> row for the product
/// + the matching <c>Tab</c> rows (names + locale labels), composes the
/// nested tab tree, and returns it WITHOUT fields. Per-tab fields are
/// served separately by <see cref="GetTabFieldsQueryHandler"/>.
///
/// Shape parity with <c>FormDefinition</c> — same <c>FormTabDto</c> reuse,
/// so the FE can render tab metadata identically across both endpoints.
/// The <c>fields</c> list on each tab stays empty here.
/// </summary>
public class ListProductTabsQueryHandler
    : IRequestHandler<ListProductTabsQuery, ProductTabsDto>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache          _cache;

    public ListProductTabsQueryHandler(IApplicationDbContext db, IMemoryCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<ProductTabsDto> Handle(ListProductTabsQuery req, CancellationToken ct)
    {
        var cultureKey = (req.Culture ?? "").Trim().ToLowerInvariant();
        // v2 — selectorTabId added to the response shape; bumping the key
        // forces cached entries to invalidate on deploy.
        // version segment — invalidated by FieldSetup admin edits via
        // FormSchemaCacheVersion.Bump (Slice 12 §A).
        var version    = FormSchemaCacheVersion.Get(req.ProductId);
        var cacheKey   = $"Products::Tabs::v2::{req.ProductId}::ver={version}::{cultureKey}";

        if (_cache.TryGetValue<ProductTabsDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        // 1. Active mappings for this product.
        var mappings = await _db.EntityTabProductMappings
            .AsNoTracking()
            .Where(m => m.ProductId == req.ProductId && m.IsActive == 1)
            .ToListAsync(ct);

        if (mappings.Count == 0)
        {
            var empty = new ProductTabsDto { productId = req.ProductId, culture = req.Culture };
            _cache.Set(cacheKey, empty, Ttl);
            return empty;
        }

        var tabIds = mappings.Select(m => m.TabId).Distinct().ToList();

        // 2. Tab master rows (names + locale labels).
        var tabs = await _db.Tabs
            .AsNoTracking()
            .Where(t => tabIds.Contains(t.TabId) && t.ActiveFlag)
            .ToListAsync(ct);

        // 3. Build flat tab list; nest under parent. fields[] stays empty.
        var flat = mappings
            .Select(m =>
            {
                var t = tabs.FirstOrDefault(t => t.TabId == m.TabId);
                return new FormTabDto
                {
                    tabId       = m.TabId,
                    parentTabId = m.ParentTabId,
                    tabName     = t?.TabName,
                    localeLabel = t?.LocaleLabel,
                    localeId    = t?.LocaleId ?? 0,
                    hiddenValue = t?.HiddenValue,
                    sortOrder   = m.SortOrder
                    // fields left empty by design — call /Fields for content.
                };
            })
            .OrderBy(t => t.sortOrder)
            .ThenBy(t => t.tabId)
            .ToList();

        var topLevel = flat.Where(t => t.parentTabId is null).ToList();
        foreach (var top in topLevel)
            top.tabs = ResolveChildren(flat, top.tabId);

        // Derive selectorTabId — the id of the tab that holds the
        // sub-product selector checkboxes for this product, or null
        // if there's no selector (KYC and similar single-flow products).
        var selectorTabId = await ResolveSelectorTabIdAsync(req.ProductId, ct);

        var result = new ProductTabsDto
        {
            productId     = req.ProductId,
            culture       = req.Culture,
            selectorTabId = selectorTabId,
            tabs          = topLevel
        };

        _cache.Set(cacheKey, result, Ttl);
        return result;
    }

    /// <summary>
    /// A product has at most one selector tab — the tab containing fields
    /// with <c>FieldLookupType = "ProductTab"</c> (sub-product checkboxes;
    /// see migration 20260522_001 Change 3). If multiple tabs somehow
    /// ended up with ProductTab-typed fields (data quirk), the lowest
    /// TabId wins for deterministic behaviour. Null means the product
    /// has no selector — FE should one-shot load all fields.
    /// </summary>
    private async Task<int?> ResolveSelectorTabIdAsync(int productId, CancellationToken ct)
    {
        return await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId       == productId
                    &&  f.FieldLookupType == "ProductTab"
                    &&  f.IsActive
                    &&  f.TabId.HasValue)
            .OrderBy(f => f.TabId)
            .Select(f => (int?)f.TabId!.Value)
            .FirstOrDefaultAsync(ct);
    }

    private static List<FormTabDto> ResolveChildren(List<FormTabDto> flat, int parentId)
    {
        var children = flat.Where(t => t.parentTabId == parentId).ToList();
        foreach (var c in children)
            c.tabs = ResolveChildren(flat, c.tabId);
        return children;
    }
}

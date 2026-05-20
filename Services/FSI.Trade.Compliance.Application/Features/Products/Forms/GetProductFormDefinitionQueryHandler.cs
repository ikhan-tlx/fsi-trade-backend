using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Forms;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Products.Forms;

/// <summary>
/// Pulls:
/// <list type="bullet">
///   <item>Every active <c>EntityTabProductMapping</c> row for the product
///         (tab ids + parent nesting + sort order).</item>
///   <item>Every <c>TenantFieldSetup</c> row for the product (the actual
///         dynamic-field definitions — labels, validation, formula, etc.).</item>
///   <item>Every <c>Tab</c> referenced above (tab names + localised labels).</item>
/// </list>
///
/// Then assembles the nested tab tree in memory: top-level tabs (<c>ParentTabId IS NULL</c>)
/// with fields ordered by <c>Field_Sequence</c>, and child tabs nested
/// recursively per <c>Parent_Tab_Id</c>.
///
/// LOCALISATION (B): the response includes BOTH the default <c>Field_Label</c>
/// and the localised <c>Locale_Label</c> per field, plus the row's
/// <c>Locale_ID</c>. The FE selects the right one based on its current
/// culture — if no localised row exists for that culture, the FE falls back
/// to <c>Field_Label</c> automatically (default-locale fallback).
///
/// CACHING: per-(productId, culture) for 15 minutes. The dataset rarely
/// changes; admins update field setups via separate (future) endpoints,
/// and any update should evict the matching cache entry.
/// </summary>
public class GetProductFormDefinitionQueryHandler
    : IRequestHandler<GetProductFormDefinitionQuery, ProductFormDefinitionDto>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache          _cache;

    public GetProductFormDefinitionQueryHandler(IApplicationDbContext db, IMemoryCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<ProductFormDefinitionDto> Handle(GetProductFormDefinitionQuery req, CancellationToken ct)
    {
        var cultureKey = (req.Culture ?? "").Trim().ToLowerInvariant();
        var cacheKey   = $"Products::FormDefinition::v1::{req.ProductId}::{cultureKey}";

        if (_cache.TryGetValue<ProductFormDefinitionDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        // 1. Tab mappings for this product (defines which tabs apply + parent nesting + sort).
        var mappings = await _db.EntityTabProductMappings
            .AsNoTracking()
            .Where(m => m.ProductId == req.ProductId && m.IsActive == 1)
            .ToListAsync(ct);

        if (mappings.Count == 0)
        {
            var empty = new ProductFormDefinitionDto { productId = req.ProductId, culture = req.Culture };
            _cache.Set(cacheKey, empty, Ttl);
            return empty;
        }

        var tabIds = mappings.Select(m => m.TabId).Distinct().ToList();

        // 2. Tabs referenced (names + locale labels).
        var tabs = await _db.Tabs
            .AsNoTracking()
            .Where(t => tabIds.Contains(t.TabId) && t.ActiveFlag)
            .ToListAsync(ct);

        // 3. Fields for this product.
        var fields = await _db.TenantFieldSetups
            .AsNoTracking()
            .Where(f => f.ProductId == req.ProductId)
            .OrderBy(f => f.FieldSequence)
            .ToListAsync(ct);

        // 4. Compose. Build a flat tab dictionary first, then walk parent→child.
        var fieldsByTab = fields
            .Where(f => f.TabId.HasValue)
            .GroupBy(f => f.TabId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(ToFieldDto).ToList());

        var flatTabs = mappings
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
                    sortOrder   = m.SortOrder,
                    fields      = fieldsByTab.TryGetValue(m.TabId, out var fs) ? fs : new List<FormFieldDto>()
                };
            })
            .OrderBy(t => t.sortOrder)
            .ThenBy(t => t.tabId)
            .ToList();

        // Top-level tabs are those without ParentTabId; child tabs nest under their parent.
        var topLevel = flatTabs.Where(t => t.parentTabId is null).ToList();
        foreach (var top in topLevel)
            top.tabs = ResolveChildren(flatTabs, top.tabId);

        var result = new ProductFormDefinitionDto
        {
            productId = req.ProductId,
            culture   = req.Culture,
            tabs      = topLevel
        };

        _cache.Set(cacheKey, result, Ttl);
        return result;
    }

    private static List<FormTabDto> ResolveChildren(List<FormTabDto> flat, int parentId)
    {
        var children = flat.Where(t => t.parentTabId == parentId).ToList();
        foreach (var c in children)
            c.tabs = ResolveChildren(flat, c.tabId);
        return children;
    }

    private static FormFieldDto ToFieldDto(TenantFieldSetup f) => new()
    {
        tenantFieldSetupId       = f.TenantFieldSetupId,
        parentTenantFieldSetupId = f.ParentTenantFieldSetupId,
        tabId                    = f.TabId,
        fieldName                = f.FieldName,
        fieldLabel               = f.FieldLabel,
        localeLabel              = f.LocaleLabel,
        localeFieldLabel         = f.LocaleFieldLabel,
        localeId                 = f.LocaleId,
        fieldTypeLkp             = f.FieldTypeLkp,
        fieldSequence            = f.FieldSequence,
        fieldTableName           = f.FieldTableName,
        fieldLookupType          = f.FieldLookupType,
        fieldLength              = f.FieldLength,
        minLength                = f.MinLength,
        isMandatory              = f.IsMandatory,
        isDisabled               = f.IsDisabled,
        visibility               = f.Visibility,
        formula                  = f.Formula,
        allowedState             = f.AllowedState,
        defaultValue             = f.DefaultValue
    };
}

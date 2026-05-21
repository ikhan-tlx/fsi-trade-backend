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
///         dynamic-field definitions — labels, validation, formula, etc.),
///         EXCLUDING the Manual Red flag rows that have been migrated to
///         the new flag catalogue (see filter below).</item>
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
/// SLICE 8 — MRL FIELDS EXCLUDED: rows matching
/// <c>(Field_Type_Lkp = 28 AND Field_Name LIKE '%MRL%' AND
/// Field_Table_Name = 'TmxTransactionDetail[]')</c> are filtered out.
/// These are the "Manual Red flags" now owned by
/// <c>TmX_Flag_Catalogue</c> / <c>TmX_Flag_Scope</c> and served via
/// <c>GET /Flag/Product/{id}</c>. Returning them here would cause the FE
/// to render two competing UIs for the same flag (legacy checkbox_file
/// alongside the new flag panel). Other <c>checkbox_file</c> fields —
/// e.g. real document-upload checkboxes — keep their existing rendering.
///
/// CACHING: per-(productId, culture) for 15 minutes. The dataset rarely
/// changes; admins update field setups via separate (future) endpoints,
/// and any update should evict the matching cache entry. Cache key was
/// bumped from v1 → v2 with the Slice 8 MRL filter so previously-cached
/// responses (which would still contain the MRL rows) invalidate on deploy.
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
        // v2 — Slice 8 filter dropped MRL rows. Old v1 cache entries would
        // still contain them; bumping the key ensures clean invalidation.
        var cacheKey   = $"Products::FormDefinition::v2::{req.ProductId}::{cultureKey}";

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

        // 3. Fields for this product. Slice 8: skip Manual Red flag rows
        //    (FieldTypeLkp=28 + name contains "MRL" + bound to
        //    TmxTransactionDetail[]) — they're owned by the new flag
        //    catalogue and served via GET /Flag/Product/{id}. Other
        //    checkbox_file fields stay (e.g. document uploads).
        var fields = await _db.TenantFieldSetups
            .AsNoTracking()
            .Where(f => f.ProductId == req.ProductId)
            .Where(f => !(f.FieldTypeLkp == 28
                       && f.FieldName     != null
                       && EF.Functions.Like(f.FieldName, "%MRL%")
                       && f.FieldTableName == "TmxTransactionDetail[]"))
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

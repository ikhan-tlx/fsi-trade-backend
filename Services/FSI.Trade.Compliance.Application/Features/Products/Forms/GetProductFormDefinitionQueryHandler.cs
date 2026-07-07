using FSI.Trade.Compliance.Application.Common.Caching;
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
/// SLICE 8 + MIGRATION 20260522_001 — DEACTIVATED FIELDS EXCLUDED:
/// the <c>Is_Active</c> column added to <c>TmX_Tenant_Field_Setup</c>
/// by migration 20260522_001 covers most exclusions (the migration
/// flipped every <c>%MRL%</c> row to <c>Is_Active = 0</c>). We apply
/// <c>WHERE IsActive = true</c> as the primary filter. The original
/// Slice 8 type-specific filter (<c>FieldTypeLkp = 28 AND Field_Name
/// LIKE '%MRL%' AND Field_Table_Name = 'TmxTransactionDetail[]'</c>)
/// remains as belt-and-braces — if some environment hasn't run the
/// migration yet, the type filter still catches the MRL rows.
///
/// MIGRATION 20260522_001 ADDITIONS: the response now surfaces
/// <c>UDF_Data</c> on each field (per-field metadata; today carries
/// <c>{"TabId":N}</c> on selector checkboxes so the FE can resolve
/// the destination sub-product tab on selection). <c>Is_Visible</c>
/// on the ETPM row is intentionally NOT surfaced — FSI direction
/// (May 2026): "is visible might not required will be handled from
/// FE side". The column stays mapped on the entity for future use.
///
/// CACHING: per-(productId, culture) for 15 minutes. The dataset rarely
/// changes; admins update field setups via separate (future) endpoints,
/// and any update should evict the matching cache entry. Cache key
/// bumped v2 → v3 with the migration-20260522_001 changes (Is_Active
/// filter + UDF_Data surface) so previously-cached responses invalidate
/// on deploy.
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
        // v4 — selectorTabId added to the response shape so KYC and
        // other one-shot callers also receive the signal. Cached
        // alongside the rest of the form definition.
        //
        // Cache-key includes FormSchemaCacheVersion.Get(productId): an
        // admin field edit calls Bump() and the next read computes a
        // new key, naturally evicting the previous entry (Slice 12 §A).
        var version    = FormSchemaCacheVersion.Get(req.ProductId);
        // v6 — added dynamic fieldTypeName (resolved from the 'field' lookup).
        var cacheKey   = $"Products::FormDefinition::v6::{req.ProductId}::ver={version}::{cultureKey}";

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
        //    Primary filter: IsActive == true (migration 20260522_001 —
        //    every legacy MRL row was flipped to Is_Active = 0).
        //    Belt-and-braces: keep the original Slice 8 type-specific
        //    exclusion in case some environment hasn't run the migration
        //    yet (MRL rows still get filtered even if their IsActive
        //    column hasn't been populated). Other checkbox_file fields
        //    (e.g. document uploads) keep flowing through.
        var fields = await _db.TenantFieldSetups
            .AsNoTracking()
            .Where(f => f.ProductId == req.ProductId)
            .Where(f => f.IsActive)
            .Where(f => !(f.FieldTypeLkp == 28
                       && f.FieldName     != null
                       && EF.Functions.Like(f.FieldName, "%MRL%")
                       && f.FieldTableName == "TmxTransactionDetail[]"))
            .OrderBy(f => f.FieldSequence)
            .ToListAsync(ct);

        // 3b. Field-type display names — resolved dynamically from
        //     TmX_Lookup (Lookup_Type = 'field'): Hidden_Value (as int) is
        //     the Field_Type_Lkp code, Visible_Value is the display name
        //     ("Text", "Dropdown", "Date", …). Loaded once per request
        //     (cache-miss only). Non-numeric Hidden_Values are skipped
        //     (mirrors the TRY_CAST in the reference query).
        var typeNameByCode = await LoadFieldTypeNamesAsync(ct);

        // 4. Compose. Build a flat tab dictionary first, then walk parent→child.
        var fieldsByTab = fields
            .Where(f => f.TabId.HasValue)
            .GroupBy(f => f.TabId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => ToFieldDto(f, typeNameByCode)).ToList());

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
                    // NOTE: m.IsVisible deliberately NOT projected — FE owns
                    // tab-rail visibility per FSI direction (May 2026).
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

        // Server-derived load-strategy signal — see ProductTabsDto.selectorTabId
        // for full semantics. Null = no selector (KYC etc.); non-null = the
        // tab id containing sub-product checkboxes.
        var selectorTabId = await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId       == req.ProductId
                    &&  f.FieldLookupType == "ProductTab"
                    &&  f.IsActive
                    &&  f.TabId.HasValue)
            .OrderBy(f => f.TabId)
            .Select(f => (int?)f.TabId!.Value)
            .FirstOrDefaultAsync(ct);

        var result = new ProductFormDefinitionDto
        {
            productId     = req.ProductId,
            culture       = req.Culture,
            selectorTabId = selectorTabId,
            tabs          = topLevel
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

    /// <summary>
    /// Loads the dynamic Field_Type_Lkp → display-name map from
    /// TmX_Lookup (Lookup_Type = 'field'). Keyed by the integer parsed from
    /// Hidden_Value; first row wins on duplicate codes.
    /// </summary>
    private async Task<Dictionary<int, string>> LoadFieldTypeNamesAsync(CancellationToken ct)
    {
        var rows = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "field" && l.HiddenValue != null)
            .Select(l => new { l.HiddenValue, l.VisibleValue })
            .ToListAsync(ct);

        var map = new Dictionary<int, string>();
        foreach (var r in rows)
            if (int.TryParse(r.HiddenValue, out var code)
                && !string.IsNullOrWhiteSpace(r.VisibleValue)
                && !map.ContainsKey(code))
                map[code] = r.VisibleValue!;
        return map;
    }

    private static FormFieldDto ToFieldDto(TenantFieldSetup f, IReadOnlyDictionary<int, string> typeNames) => new()
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
        fieldType                = FieldTypeMapper.ToSemanticType(f.FieldTypeLkp, f.FieldLookupType),
        fieldTypeName            = FieldTypeMapper.ResolveTypeName(f.FieldTypeLkp, typeNames),
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
        defaultValue             = f.DefaultValue,
        udfData                  = f.UdfData,
        placeholder              = f.Placeholder,
        isAmountField            = f.IsAmountField
    };
}

using FSI.Trade.Compliance.Application.Common.Caching;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Forms;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Products.Forms;

/// <summary>
/// Returns fields for one tab inside a product. Applies the same filters
/// as <see cref="GetProductFormDefinitionQueryHandler"/>:
/// <list type="bullet">
///   <item><c>Is_Active = 1</c> (migration 20260522_001 — drops deactivated
///         fields including ~415 legacy MRL rows).</item>
///   <item>Belt-and-braces MRL exclusion in case <c>Is_Active</c> wasn't
///         populated yet (FieldTypeLkp = 28 AND name LIKE '%MRL%' AND
///         table = TmxTransactionDetail[]).</item>
/// </list>
///
/// Validates that the (productId, tabId) pair exists in the
/// EntityTabProductMapping table before reading fields. Throws 404 on
/// mismatch — keeps the endpoint a tight security boundary.
/// </summary>
public class GetTabFieldsQueryHandler
    : IRequestHandler<GetTabFieldsQuery, TabFieldsDto>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache          _cache;

    public GetTabFieldsQueryHandler(IApplicationDbContext db, IMemoryCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<TabFieldsDto> Handle(GetTabFieldsQuery req, CancellationToken ct)
    {
        var cultureKey = (req.Culture ?? "").Trim().ToLowerInvariant();
        // version segment — invalidated by FieldSetup admin edits via
        // FormSchemaCacheVersion.Bump (Slice 12 §A).
        var version    = FormSchemaCacheVersion.Get(req.ProductId);
        // v3 — added dynamic fieldTypeName (resolved from the 'field' lookup).
        var cacheKey   = $"Products::TabFields::v3::{req.ProductId}::{req.TabId}::ver={version}::{cultureKey}";

        if (_cache.TryGetValue<TabFieldsDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        // 1. Verify (product, tab) is a valid mapping. Stops callers from
        //    enumerating fields across products by guessing tab ids.
        var mappingExists = await _db.EntityTabProductMappings.AsNoTracking()
            .AnyAsync(m => m.ProductId == req.ProductId
                       &&  m.TabId     == req.TabId
                       &&  m.IsActive  == 1, ct);

        if (!mappingExists)
            throw new NotFoundException(
                "tab_not_mapped_to_product",
                $"Tab {req.TabId} is not mapped to product {req.ProductId}.");

        // 2. Tab metadata (name + locale label) so the FE has a header to render.
        var tab = await _db.Tabs.AsNoTracking()
            .Where(t => t.TabId == req.TabId && t.ActiveFlag)
            .Select(t => new { t.TabName, t.LocaleLabel })
            .FirstOrDefaultAsync(ct);

        // 3. Field-type display names — resolved dynamically from TmX_Lookup
        //    (Lookup_Type = 'field'); see FormDefinition handler for detail.
        var typeNameByCode = await LoadFieldTypeNamesAsync(ct);

        // 4. Active fields for this (product, tab). Filters mirror
        //    FormDefinition exactly.
        var fields = (await _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.ProductId == req.ProductId && f.TabId == req.TabId)
            .Where(f => f.IsActive)
            .Where(f => !(f.FieldTypeLkp == 28
                       && f.FieldName     != null
                       && EF.Functions.Like(f.FieldName, "%MRL%")
                       && f.FieldTableName == "TmxTransactionDetail[]"))
            .OrderBy(f => f.FieldSequence)
            .ToListAsync(ct))
            .Select(f => ToFieldDto(f, typeNameByCode))
            .ToList();

        var result = new TabFieldsDto
        {
            productId   = req.ProductId,
            tabId       = req.TabId,
            culture     = req.Culture,
            tabName     = tab?.TabName,
            localeLabel = tab?.LocaleLabel,
            fields      = fields
        };

        _cache.Set(cacheKey, result, Ttl);
        return result;
    }

    /// <summary>
    /// Shape parity with <see cref="GetProductFormDefinitionQueryHandler"/>'s
    /// projection — same FormFieldDto fields populated identically so the
    /// FE renderer code path is shared between the lazy and one-shot
    /// endpoints.
    /// </summary>
    /// <summary>
    /// Loads the dynamic Field_Type_Lkp → display-name map from TmX_Lookup
    /// (Lookup_Type = 'field'); keyed by the int parsed from Hidden_Value.
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

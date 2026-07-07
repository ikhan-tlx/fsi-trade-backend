using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.Forms;

/// <summary>
/// Replaces legacy GET /api/v1/TenantFieldSetup/GetFieldsByProduct/{id}/{culture}.
/// Returns the nested tab-tree with fields per tab, scoped to one product.
/// Cached server-side (15-min TTL) per (productId, culture).
///
/// The legacy stored proc <c>sp_GetTanantFieldsByCulture</c> is replaced by
/// EF Core LINQ — listed in DB cleanup candidates.
/// </summary>
public record GetProductFormDefinitionQuery(int ProductId, string? Culture)
    : IRequest<ProductFormDefinitionDto>;

/// <summary>
/// Top-level response: every top-level (no parent) tab for the product,
/// each with its fields list and nested child tabs.
/// </summary>
public class ProductFormDefinitionDto
{
    public int                       productId     { get; set; }
    public string?                   culture       { get; set; }

    /// <summary>
    /// Server-derived signal — see <see cref="ProductTabsDto.selectorTabId"/>
    /// for the full semantics. Included here so callers that go through
    /// <c>FormDefinition</c> in one shot (e.g. KYC) still receive the
    /// signal and can render the selector tab differently if needed.
    /// </summary>
    public int?                      selectorTabId { get; set; }

    public List<FormTabDto>          tabs          { get; set; } = new();
}

public class FormTabDto
{
    public int                  tabId        { get; set; }
    public int?                 parentTabId  { get; set; }
    public string?              tabName      { get; set; }   // default (Field_Label-equivalent on TmX_Tab)
    public string?              localeLabel  { get; set; }   // localised label (in TmX_Tab.Locale_Label)
    public int                  localeId     { get; set; }
    public string?              hiddenValue  { get; set; }   // tab "code" used by the FE renderer
    public int?                 sortOrder    { get; set; }

    // NOTE: Is_Visible (added by migration 20260522_001) is intentionally
    // NOT surfaced here per FSI direction (May 2026): "is visible might
    // not required will be handled from FE side". The column is still
    // mapped on the entity (EntityTabProductMapping.IsVisible) so future
    // consumers can read it; we just don't put it in the public API.

    public List<FormFieldDto>   fields       { get; set; } = new();
    public List<FormTabDto>     tabs         { get; set; } = new();  // child tabs
}

public class FormFieldDto
{
    public int       tenantFieldSetupId       { get; set; }
    public int?      parentTenantFieldSetupId { get; set; }
    public int?      tabId                    { get; set; }
    public string?   fieldName                { get; set; }
    public string?   fieldLabel               { get; set; }   // default (English) label — always present
    public string?   localeLabel              { get; set; }   // localised label, if any
    public string?   localeFieldLabel         { get; set; }
    public int       localeId                 { get; set; }

    /// <summary>Raw legacy <c>Field_Type_Lkp</c> integer (e.g. 3, 25, 55). Kept for back-compat.</summary>
    public int?      fieldTypeLkp             { get; set; }

    /// <summary>
    /// Stable lowercase semantic token derived from <c>fieldTypeLkp</c> per
    /// DynamicForm_Integration_Guide §5 — one of: text / textarea / number /
    /// date / select / multi / boolean / repeat / spacer / section /
    /// tableRoot / endMarker / productTab. Good for FE widget logic (matches
    /// the §10.2 field-config <c>type</c> union). Always present.
    /// </summary>
    public string?   fieldType                { get; set; }

    /// <summary>
    /// Human-readable type name resolved DYNAMICALLY from
    /// <c>TmX_Lookup</c> where <c>Lookup_Type = 'field'</c> and
    /// <c>TRY_CAST(Hidden_Value AS INT) = Field_Type_Lkp</c> — i.e. the
    /// <c>Visible_Value</c> ("Text", "Dropdown", "Date", …). This is the
    /// value the Field Configuration admin page shows in its Type column.
    /// Null when the 'field' lookup has no row for the code — FE falls back
    /// to <see cref="fieldType"/>.
    /// </summary>
    public string?   fieldTypeName            { get; set; }

    public int?      fieldSequence            { get; set; }
    public string?   fieldTableName           { get; set; }
    public string?   fieldLookupType          { get; set; }
    public string?   fieldLength              { get; set; }
    public string?   minLength                { get; set; }
    public string?   isMandatory              { get; set; }
    public string?   isDisabled               { get; set; }
    public string?   visibility               { get; set; }
    public string?   formula                  { get; set; }
    public string?   allowedState             { get; set; }
    public string?   defaultValue             { get; set; }

    /// <summary>
    /// From <c>TmX_Tenant_Field_Setup.UDF_Data</c> (migration 20260522_001).
    /// Per-field JSON metadata. Today carries <c>{"TabId":N}</c> on
    /// selector-checkbox fields (Field_Lookup_Type = "ProductTab") so the
    /// FE can resolve the destination sub-product tab on selection.
    /// Verbatim string passthrough — FE parses as needed.
    /// </summary>
    public string?   udfData                  { get; set; }

    /// <summary>
    /// From <c>TmX_Tenant_Field_Setup.Placeholder</c> (migration
    /// 2026_05_029). Hint text rendered under the field label by the FE
    /// form renderer (wizard + transaction edit page). Editable via the
    /// Slice 12 Field Configuration admin page (<c>PUT /FieldSetup/{id}</c>).
    /// </summary>
    public string?   placeholder              { get; set; }

    /// <summary>From <c>TmX_Tenant_Field_Setup.Is_Amount_Field</c> (migration 2026_07_004).
    /// TRUE on the one field per sub-product designated as the transaction amount (for
    /// cross-transaction aggregates). Toggled on the Field Configuration page.</summary>
    public bool      isAmountField            { get; set; }
}

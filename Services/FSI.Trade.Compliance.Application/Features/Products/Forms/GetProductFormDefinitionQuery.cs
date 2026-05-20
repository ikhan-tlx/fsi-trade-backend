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
    public int                       productId { get; set; }
    public string?                   culture   { get; set; }
    public List<FormTabDto>          tabs      { get; set; } = new();
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
    public int?      fieldTypeLkp             { get; set; }
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
}

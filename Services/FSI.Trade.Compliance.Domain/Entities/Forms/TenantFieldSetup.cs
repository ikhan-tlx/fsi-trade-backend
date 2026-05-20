namespace FSI.Trade.Compliance.Domain.Entities.Forms;

/// <summary>
/// Field definition per <c>(Tenant, Product, Tab)</c> tuple
/// (<c>TmX_Tenant_Field_Setup</c>). Holds everything the dynamic-form
/// renderer needs:
///
/// <list type="bullet">
///   <item><b>Field shape</b>: name, type lookup, sequence, label.</item>
///   <item><b>Validation</b>: max length, min length, mandatory expression,
///         disabled expression, visibility expression. These are stored as
///         <em>string expressions</em> evaluated client-side by the FE
///         renderer. The backend serves them verbatim; it does NOT
///         interpret them.</item>
///   <item><b>Computed value</b>: <c>Formula</c> — a string expression the
///         FE evaluates to derive a value from other fields.</item>
///   <item><b>Allowed states</b>: which workflow states allow editing.</item>
///   <item><b>Localisation</b>: <c>Locale_ID</c> + <c>Locale_Label</c>. One
///         row per (field × locale).</item>
/// </list>
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 8105.
///
/// IMPORTANT: this replaces the legacy stored proc
/// <c>sp_GetTanantFieldsByCulture</c> — we query the table directly via
/// LINQ. The SP is on the cleanup-candidates list once Slice 6 ships.
/// </summary>
public class TenantFieldSetup
{
    public int       TenantFieldSetupId         { get; set; }
    public int?      TenantId                   { get; set; }
    public int?      ProductId                  { get; set; }
    public int?      TabId                      { get; set; }
    public int?      ParentTenantFieldSetupId   { get; set; }

    public string?   FieldName                  { get; set; }
    public string?   FieldLabel                 { get; set; }
    public int?      FieldTypeLkp               { get; set; }
    public int?      FieldSequence              { get; set; }
    public string?   FieldTableName             { get; set; }
    public string?   FieldLookupType            { get; set; }
    public string?   FieldLength                { get; set; }
    public string?   MinLength                  { get; set; }

    /// <summary>String expression evaluated client-side; e.g. "field('amount') > 0".</summary>
    public string?   IsMandatory                { get; set; }

    /// <summary>String expression evaluated client-side.</summary>
    public string?   IsDisabled                 { get; set; }

    /// <summary>String expression evaluated client-side. Drives show/hide.</summary>
    public string?   Visibility                 { get; set; }

    /// <summary>String expression evaluated client-side to derive a value.</summary>
    public string?   Formula                    { get; set; }

    /// <summary>Comma-separated list of workflow states in which this field is editable.</summary>
    public string?   AllowedState               { get; set; }

    public string?   DefaultValue               { get; set; }

    // Localisation — one row per (field × locale).
    public string?   LocaleFieldLabel           { get; set; }
    public string?   LocaleLabel                { get; set; }
    public int       LocaleId                   { get; set; }

    public string    CreatedBy                  { get; set; } = "";
    public DateTime  CreatedDate                { get; set; }
    public string?   LastUpdatedBy              { get; set; }
    public DateTime? LastUpdatedDate            { get; set; }
}

namespace FSI.Trade.Compliance.Domain.Entities.Forms;

/// <summary>
/// Tab master (<c>TmX_Tab</c>). Tabs are reused across products via
/// <see cref="EntityTabProductMapping"/>. The localised name lives in
/// <c>Locale_Label</c> with <c>Locale_ID</c> picking the language.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 8030.
/// </summary>
public class Tab
{
    public int       TabId               { get; set; }
    public string?   TabName             { get; set; }
    public string?   Description         { get; set; }
    public string?   HiddenValue         { get; set; }
    public int       TenantId            { get; set; }
    public bool      ActiveFlag          { get; set; }
    public string?   LocaleLabel         { get; set; }
    public int       LocaleId            { get; set; }

    public DateTime  EffectiveStartDate  { get; set; }
    public DateTime  EffectiveEndDate    { get; set; }
    public string    CreatedBy           { get; set; } = "";
    public DateTime  CreatedDate         { get; set; }
    public string?   LastUpdatedBy       { get; set; }
    public DateTime? LastUpdatedDate     { get; set; }
}

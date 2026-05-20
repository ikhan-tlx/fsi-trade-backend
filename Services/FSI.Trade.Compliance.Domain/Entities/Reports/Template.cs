namespace FSI.Trade.Compliance.Domain.Entities.Reports;

/// <summary>
/// READ-ONLY projection over <c>TmX_Template</c>. A template row holds a
/// DotLiquid template body (HTML with <c>{{ field }}</c> placeholders) and
/// is looked up by <c>Template_Name</c> for both report rendering and
/// application-PDF generation (legacy used the same table for both).
///
/// Slice 7 reads the report templates (<c>Template_Type_Lkp_ID</c> would
/// indicate "report" vs "application document" but the new backend looks
/// up by name only — same as legacy).
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 8056.
/// </summary>
public class Template
{
    public int       TemplateId           { get; set; }
    public string    TemplateName         { get; set; } = "";
    public string?   TemplateDescription  { get; set; }
    public string?   TemplateText         { get; set; }
    public int?      TemplateTypeLkpId    { get; set; }
    public int       TenantId             { get; set; }
    public int?      ProductId            { get; set; }
    public bool      IsProtected          { get; set; }
    public string?   PasswordBinding      { get; set; }

    public string    CreatedBy            { get; set; } = "";
    public DateTime  CreatedDate          { get; set; }
    public string?   LastUpdatedBy        { get; set; }
    public DateTime? LastUpdatedDate      { get; set; }
}

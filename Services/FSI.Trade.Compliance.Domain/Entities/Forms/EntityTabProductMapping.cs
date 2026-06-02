namespace FSI.Trade.Compliance.Domain.Entities.Forms;

/// <summary>
/// "Which tabs apply to product X under entity Y, in what order, with what
/// parent nesting." One row per (Entity_Id, Product_Id, Tab_Id). The
/// transaction edit page reads every active row for the product to lay out
/// the tab structure.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 6281.
///
/// NB: <c>Is_Active</c> in this table is <c>int</c>, not <c>bit</c>.
/// 1 = active, 0/NULL = inactive.
/// </summary>
public class EntityTabProductMapping
{
    public int       Id                  { get; set; }   // Entity_Tab_Product_Mapping_Id
    public int       TenantId            { get; set; }
    public int       EntityId            { get; set; }
    public int       ProductId           { get; set; }
    public int       TabId               { get; set; }
    public int?      ParentTabId         { get; set; }
    public int?      IsActive            { get; set; }   // int — see class note
    public int?      SortOrder           { get; set; }

    /// <summary>
    /// Added by migration <c>20260522_001_DynamicFormEnhancements</c>.
    /// Distinguishes tabs that are <em>structurally present</em> (BE must
    /// return them) from tabs that <em>appear in the FE tab rail</em>.
    /// <c>Is_Visible = false</c> means the row is data the FE needs (e.g.
    /// selector-tab checkbox fields) but should NOT show in the tab rail.
    ///
    /// CRITICAL: the BE must NOT filter on this — the FE decides rendering.
    /// We surface the flag verbatim in the FormDefinition response.
    /// </summary>
    public bool      IsVisible           { get; set; } = true;

    public string?   CreatedBy           { get; set; }
    public DateTime? CreatedDate         { get; set; }
    public string?   LastUpdatedBy       { get; set; }
    public DateTime? LastUpdatedDate     { get; set; }
}

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

    public string?   CreatedBy           { get; set; }
    public DateTime? CreatedDate         { get; set; }
    public string?   LastUpdatedBy       { get; set; }
    public DateTime? LastUpdatedDate     { get; set; }
}

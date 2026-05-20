namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// READ-MOSTLY projection over the legacy <c>TmX_Product</c> table. Slice 5
/// only needs three things from it:
///
/// <list type="bullet">
///   <item>Listing product IDs currently mapped to a given workflow scheme
///         (<c>WorkflowSchemeCode</c> column).</item>
///   <item>Bulk-updating <c>WorkflowSchemeCode</c> when an admin re-assigns
///         products to a scheme via /api/v1/Workflow/ProductMapping.</item>
///   <item>Filtering on effective dates so retired products don't show up.</item>
/// </list>
///
/// Slice 6 will expand this entity (or introduce sibling entities) when
/// full Product CRUD lands. For now we map the minimum surface needed by
/// the workflow product-mapping endpoints.
/// </summary>
public class Product
{
    public int       ProductId            { get; set; }
    public string?   ProductCode          { get; set; }
    public string    ProductName          { get; set; } = "";
    public string?   ProductDescription   { get; set; }
    public int?      ProductTypeLkp       { get; set; }    // Slice 6.5 — used by /Product/list for FE-side filtering
    public string?   WorkflowSchemeCode   { get; set; }
    public int?      CurrencyId           { get; set; }
    public bool      ActiveFlag           { get; set; }
    public DateTime  EffectiveStartDate   { get; set; }
    public DateTime  EffectiveEndDate     { get; set; }

    // Audit slots — touched when SaveProductMapping mutates WorkflowSchemeCode.
    public string?   LastUpdatedBy        { get; set; }
    public DateTime? LastUpdatedDate      { get; set; }
}

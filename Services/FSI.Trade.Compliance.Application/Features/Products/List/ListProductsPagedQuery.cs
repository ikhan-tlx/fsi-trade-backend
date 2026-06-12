using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.List;

/// <summary>
/// Paged admin list of products for the FE "Products" management grid
/// (<c>GET /api/v1/Product?page=…&amp;pageSize=…&amp;sort=…&amp;filter=…</c>).
///
/// Distinct from the existing reads on this controller:
///   /list      — all ACTIVE products (flat, for dropdowns / wizard)
///   /ActiveLov — active products in legacy LOV shape
/// This one is the full paged grid (ALL products incl. inactive) with
/// server-side sort + filter, mirroring the User / Role list pattern.
/// </summary>
public class ListProductsPagedQuery : PagedQuery, IRequest<PagedResult<ProductPagedItemDto>>
{
}

public class ProductPagedItemDto
{
    public int       productId          { get; set; }
    public string?   productCode        { get; set; }
    public string    productName        { get; set; } = "";
    public string?   productDescription { get; set; }
    public int?      productTypeLkp     { get; set; }
    public string?   workflowSchemeCode { get; set; }
    public int?      currencyId         { get; set; }
    public string?   status             { get; set; }
    public bool      activeFlag         { get; set; }
    public DateTime  effectiveStartDate { get; set; }
    public DateTime  effectiveEndDate   { get; set; }
}

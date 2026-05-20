using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.List;

/// <summary>
/// Returns the active product catalog for FE dropdowns / wizard product step.
/// Slice 6.5. Replaces legacy <c>GET /api/v1/Product/list</c>.
///
/// "Active" means: <c>Active_Flag = 1</c> AND today falls inside
/// [EffectiveStartDate, EffectiveEndDate]. Legacy applied the same filters.
///
/// Note: legacy ALSO filtered by the caller's branch-product mapping
/// (<c>TmX_Branch_Products_Mapping</c>). We don't map that table yet — for
/// now every active product is visible to every authenticated caller. The
/// downstream <c>POST /Transaction</c> handler still validates that the
/// user is mapped to the chosen branch, so the security model isn't
/// weakened; the dropdown is just less precise. Add the branch-product
/// filter when <c>TmX_Branch_Products_Mapping</c> is mapped (future slice
/// when product CRUD or branch-product admin lands).
/// </summary>
public record ListProductsQuery : IRequest<IReadOnlyList<ProductListItemDto>>;

public class ProductListItemDto
{
    public int       productId           { get; set; }
    public string?   productCode         { get; set; }
    public string    productName         { get; set; } = "";
    public string?   productDescription  { get; set; }
    public int?      productTypeLkp      { get; set; }
    public string?   workflowSchemeCode  { get; set; }
    public int?      currencyId          { get; set; }
}

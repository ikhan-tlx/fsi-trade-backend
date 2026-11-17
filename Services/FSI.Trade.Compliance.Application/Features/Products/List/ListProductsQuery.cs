using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.List;

/// <summary>
/// Returns the active product catalog for FE dropdowns / wizard product step.
/// Slice 6.5. Replaces legacy <c>GET /api/v1/Product/list</c>.
///
/// "Active" means: <c>Active_Flag = 1</c> AND today falls inside
/// [EffectiveStartDate, EffectiveEndDate]. Legacy applied the same filters.
///
/// Scoping (2026-11-12): now filtered by the caller's branch-product mapping
/// (<c>TmX_Branch_Products_Mapping</c>) — the union of products across the
/// user's active branches — mirroring legacy
/// <c>LoanProductService.GetActiveLoanProductLovByUserBranch</c>. The wizard's
/// branch-first step uses the tighter per-branch query
/// (<c>ListProductsByBranchQuery</c>); this union list backs other product
/// dropdowns/filters.
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

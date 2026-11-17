using System.Text.Json.Serialization;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.AllActiveLov;

/// <summary>
/// ALL active products (no user/branch scoping) — the assignable-product picker on
/// the Branch Configuration form (`GET /api/v1/Product/GetAllActiveProducts`). This
/// is the admin "which products can this branch transact" list, so it is deliberately
/// unscoped, unlike the wizard's user/branch-scoped product LOVs.
/// </summary>
public record ListAllActiveProductsQuery : IRequest<IReadOnlyList<ActiveProductLovDto>>;

/// <summary>
/// JSON keys are pinned PascalCase (<c>ProductId</c> / <c>ProductName</c>) to match
/// the existing FE branch form, which reads <c>item.ProductId</c> / <c>item.ProductName</c>.
/// </summary>
public class ActiveProductLovDto
{
    [JsonPropertyName("ProductId")]
    public int ProductId { get; set; }

    [JsonPropertyName("ProductName")]
    public string ProductName { get; set; } = "";
}

using FSI.Trade.Compliance.Application.Features.Products.List;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.ByBranch;

/// <summary>
/// Active products a GIVEN branch is entitled to transact (branch → product via
/// <c>TmX_Branch_Products_Mapping</c>). Backs the new-transaction wizard's
/// branch-first product step: the user picks a branch, then only that branch's
/// products are offered. The branch must belong to the authenticated caller.
/// </summary>
public record ListProductsByBranchQuery(int BranchId) : IRequest<IReadOnlyList<ProductListItemDto>>;

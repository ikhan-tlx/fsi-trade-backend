using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.List;

public class ListProductsQueryHandler : IRequestHandler<ListProductsQuery, IReadOnlyList<ProductListItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public ListProductsQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<IReadOnlyList<ProductListItemDto>> Handle(ListProductsQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Product LOV requires an authenticated caller.");

        // User-scoped product selection (mirrors legacy LoanProductService
        // .GetActiveLoanProductLovByUserBranch): the products the caller may transact
        // are those mapped (TmX_Branch_Products_Mapping) to the branches the caller is
        // actively mapped to (TmX_Company_Branch_Users_Mapping). Only the scoped LOV is
        // visible — a user whose branches carry no product entitlement sees none.
        var allowedProductIds =
            from m in _db.CompanyBranchUserMappings.AsNoTracking()
            where m.UserId == userId
               && m.ActiveFlag
               && m.EffectiveStartDate <= now
               && m.EffectiveEndDate   >= now
            join bp in _db.BranchProductMappings.AsNoTracking()
                 on m.CompanyBranchId equals bp.CompanyBranchId
            where bp.ActiveFlag == true
            select bp.ProductId;

        return await _db.Products
            .AsNoTracking()
            .Where(p => p.ActiveFlag
                     && p.EffectiveStartDate <= now
                     && p.EffectiveEndDate   >= now
                     && allowedProductIds.Contains(p.ProductId))
            .OrderBy(p => p.ProductName)
            .Select(p => new ProductListItemDto
            {
                productId          = p.ProductId,
                productCode        = p.ProductCode,
                productName        = p.ProductName,
                productDescription = p.ProductDescription,
                productTypeLkp     = p.ProductTypeLkp,
                workflowSchemeCode = p.WorkflowSchemeCode,
                currencyId         = p.CurrencyId
            })
            .ToListAsync(ct);
    }
}

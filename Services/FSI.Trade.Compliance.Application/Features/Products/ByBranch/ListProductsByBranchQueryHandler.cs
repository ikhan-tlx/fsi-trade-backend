using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Products.List;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.ByBranch;

public class ListProductsByBranchQueryHandler
    : IRequestHandler<ListProductsByBranchQuery, IReadOnlyList<ProductListItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public ListProductsByBranchQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<IReadOnlyList<ProductListItemDto>> Handle(ListProductsByBranchQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var userId = _current.UserId
                     ?? throw new AuthenticationException("unauthenticated", "Product LOV requires an authenticated caller.");

        // The branch must be one the caller is actively mapped to — a user may only
        // pick products for their own branch. Unknown / foreign branch → empty.
        var ownsBranch = await _db.CompanyBranchUserMappings.AsNoTracking().AnyAsync(m =>
            m.UserId == userId
            && m.CompanyBranchId     == req.BranchId
            && m.ActiveFlag
            && m.EffectiveStartDate  <= now
            && m.EffectiveEndDate    >= now, ct);
        if (!ownsBranch) return Array.Empty<ProductListItemDto>();

        // Products this branch is entitled to (active mapping).
        var allowedProductIds = _db.BranchProductMappings.AsNoTracking()
            .Where(bp => bp.CompanyBranchId == req.BranchId && bp.ActiveFlag == true)
            .Select(bp => bp.ProductId);

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

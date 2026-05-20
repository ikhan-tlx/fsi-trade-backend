using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.List;

public class ListProductsQueryHandler : IRequestHandler<ListProductsQuery, IReadOnlyList<ProductListItemDto>>
{
    private readonly IApplicationDbContext _db;

    public ListProductsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProductListItemDto>> Handle(ListProductsQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        return await _db.Products
            .AsNoTracking()
            .Where(p => p.ActiveFlag
                     && p.EffectiveStartDate <= now
                     && p.EffectiveEndDate   >= now)
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

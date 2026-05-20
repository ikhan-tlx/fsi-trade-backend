using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.ActiveLov;

public class ListActiveProductsLovQueryHandler
    : IRequestHandler<ListActiveProductsLovQuery, IReadOnlyList<ProductLovItemDto>>
{
    private readonly IApplicationDbContext _db;

    public ListActiveProductsLovQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProductLovItemDto>> Handle(
        ListActiveProductsLovQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        return await _db.Products
            .AsNoTracking()
            .Where(p => p.ActiveFlag
                     && p.EffectiveStartDate <= now
                     && p.EffectiveEndDate   >= now)
            .OrderBy(p => p.ProductName)
            .Select(p => new ProductLovItemDto
            {
                lookupId     = p.ProductId,
                visibleValue = p.ProductName,
                hiddenValue  = p.ProductId.ToString(),
                description  = p.ProductDescription
            })
            .ToListAsync(ct);
    }
}

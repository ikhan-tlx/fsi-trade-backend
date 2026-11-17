using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.AllActiveLov;

public class ListAllActiveProductsQueryHandler
    : IRequestHandler<ListAllActiveProductsQuery, IReadOnlyList<ActiveProductLovDto>>
{
    private readonly IApplicationDbContext _db;
    public ListAllActiveProductsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ActiveProductLovDto>> Handle(ListAllActiveProductsQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.Products
            .AsNoTracking()
            .Where(p => p.ActiveFlag && p.EffectiveStartDate <= now && p.EffectiveEndDate >= now)
            .OrderBy(p => p.ProductName)
            .Select(p => new ActiveProductLovDto { ProductId = p.ProductId, ProductName = p.ProductName })
            .ToListAsync(ct);
    }
}

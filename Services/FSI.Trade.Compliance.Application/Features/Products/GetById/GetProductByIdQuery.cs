using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Products.List;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.GetById;

/// <summary>Single product by id, for the edit form. Slice 14.</summary>
public record GetProductByIdQuery(int Id) : IRequest<ProductPagedItemDto>;

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductPagedItemDto>
{
    private readonly IApplicationDbContext _db;
    public GetProductByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ProductPagedItemDto> Handle(GetProductByIdQuery req, CancellationToken ct)
    {
        return await _db.Products.AsNoTracking()
            .Where(p => p.ProductId == req.Id)
            .Select(p => new ProductPagedItemDto
            {
                productId          = p.ProductId,
                productCode        = p.ProductCode,
                productName        = p.ProductName,
                productDescription = p.ProductDescription,
                productTypeLkp     = p.ProductTypeLkp,
                workflowSchemeCode = p.WorkflowSchemeCode,
                currencyId         = p.CurrencyId,
                status             = p.ActiveFlag ? "Active" : "Inactive",
                activeFlag         = p.ActiveFlag,
                effectiveStartDate = p.EffectiveStartDate,
                effectiveEndDate   = p.EffectiveEndDate
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("product_not_found", $"Product {req.Id} not found.");
    }
}

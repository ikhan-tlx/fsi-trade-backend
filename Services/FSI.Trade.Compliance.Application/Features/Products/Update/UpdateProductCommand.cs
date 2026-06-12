using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.Update;

/// <summary>Updates the mutable fields of a product. Slice 14.</summary>
public class UpdateProductCommand : IRequest<Unit>
{
    public int       ProductId          { get; set; }   // from route
    public string?   ProductCode        { get; set; }
    public string?   ProductName        { get; set; }
    public int?      ProductTypeLkp     { get; set; }
    public int?      CurrencyId         { get; set; }
    public string?   Description        { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
}

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public UpdateProductCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<Unit> Handle(UpdateProductCommand req, CancellationToken ct)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.ProductId == req.ProductId, ct)
            ?? throw new NotFoundException("product_not_found", $"Product {req.ProductId} not found.");

        if (req.ProductCode != null)        product.ProductCode        = req.ProductCode.Trim();
        if (req.ProductName != null && req.ProductName.Trim().Length > 0)
                                            product.ProductName        = req.ProductName.Trim();
        if (req.Description != null)        product.ProductDescription = req.Description.Trim();
        if (req.ProductTypeLkp != null)     product.ProductTypeLkp     = req.ProductTypeLkp;
        if (req.CurrencyId != null)         product.CurrencyId         = req.CurrencyId;
        if (req.EffectiveStartDate != null) product.EffectiveStartDate = req.EffectiveStartDate.Value;
        if (req.EffectiveEndDate != null)   product.EffectiveEndDate   = req.EffectiveEndDate.Value;

        product.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        product.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

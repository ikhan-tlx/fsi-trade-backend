using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.SetActive;

/// <summary>Activate / deactivate a product. Slice 14.</summary>
public record SetProductActiveCommand(int ProductId, bool Active) : IRequest<Unit>;

public class SetProductActiveCommandHandler : IRequestHandler<SetProductActiveCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public SetProductActiveCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<Unit> Handle(SetProductActiveCommand req, CancellationToken ct)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.ProductId == req.ProductId, ct)
            ?? throw new NotFoundException("product_not_found", $"Product {req.ProductId} not found.");

        if (product.ActiveFlag == req.Active) return Unit.Value;

        product.ActiveFlag      = req.Active;
        product.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        product.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

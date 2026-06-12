using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Products.Create;

/// <summary>
/// Creates a product. Slice 14 (direct CRUD). Field names mirror the FE
/// ProductFormValues. TmX_Product requires Product_Type_Lkp and Currency_ID
/// (NOT NULL) — validated here. Group_Flag / Individual_Flag aren't on the FE
/// form; defaulted (individual product, not a group product).
/// </summary>
public class CreateProductCommand : IRequest<int>
{
    public string?   ProductCode        { get; set; }
    public string    ProductName        { get; set; } = "";
    public int?      ProductTypeLkp     { get; set; }
    public int?      CurrencyId         { get; set; }
    public string?   Description        { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
    public bool?     GroupFlag          { get; set; }
    public bool?     IndividualFlag     { get; set; }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _current;
    public CreateProductCommandHandler(IApplicationDbContext db, ICurrentUserService current)
        => (_db, _current) = (db, current);

    public async Task<int> Handle(CreateProductCommand req, CancellationToken ct)
    {
        var name = (req.ProductName ?? "").Trim();
        if (name.Length == 0)
            throw new ConflictException("product_name_required", "Product name is required.");
        if (req.ProductTypeLkp is null)
            throw new ConflictException("product_type_required", "Product type is required.");
        if (req.CurrencyId is null)
            throw new ConflictException("currency_required", "Currency is required.");

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var product = new Product
        {
            TenantId           = _current.TenantId ?? 1,
            ProductCode        = req.ProductCode?.Trim(),
            ProductName        = name,
            ProductDescription = req.Description?.Trim(),
            ProductTypeLkp     = req.ProductTypeLkp,
            CurrencyId         = req.CurrencyId,
            GroupFlag          = req.GroupFlag ?? false,
            IndividualFlag     = req.IndividualFlag ?? true,
            ActiveFlag         = true,
            EffectiveStartDate = req.EffectiveStartDate ?? DateTime.UtcNow,
            EffectiveEndDate   = req.EffectiveEndDate ?? new DateTime(9999, 12, 31),
            CreatedBy          = actor,
            CreatedDate        = DateTime.UtcNow,
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        return product.ProductId;
    }
}

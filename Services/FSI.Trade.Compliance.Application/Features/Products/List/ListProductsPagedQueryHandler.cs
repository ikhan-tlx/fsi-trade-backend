using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Products.List;

/// <summary>
/// Server-side paged / sorted / filtered list over <c>TmX_Product</c> for the
/// admin grid. Returns ALL products (active and inactive) so admins can manage
/// the full catalogue; the active-only flat read stays on <c>/list</c>.
/// </summary>
public class ListProductsPagedQueryHandler
    : IRequestHandler<ListProductsPagedQuery, PagedResult<ProductPagedItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListProductsPagedQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<ProductPagedItemDto>> Handle(
        ListProductsPagedQuery req,
        CancellationToken ct)
    {
        IQueryable<Product> q = _db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            q = q.Where(p =>
                   EF.Functions.Like(p.ProductName, $"%{f}%")
                || (p.ProductCode != null && EF.Functions.Like(p.ProductCode, $"%{f}%"))
                || (p.ProductDescription != null && EF.Functions.Like(p.ProductDescription, $"%{f}%")));
        }

        q = ApplySort(q, req.Sort);

        var total = await q.CountAsync(ct);

        var items = await q
            .Skip(req.Skip)
            .Take(req.Take)
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
            .ToListAsync(ct);

        return new PagedResult<ProductPagedItemDto>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private static IQueryable<Product> ApplySort(IQueryable<Product> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return q.OrderBy(p => p.ProductName);

        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
        if (first is null) return q.OrderBy(p => p.ProductName);

        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var desc  = string.Equals(parts.ElementAtOrDefault(1), "desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "productname" => desc ? q.OrderByDescending(p => p.ProductName) : q.OrderBy(p => p.ProductName),
            "productcode" => desc ? q.OrderByDescending(p => p.ProductCode) : q.OrderBy(p => p.ProductCode),
            "productid"   => desc ? q.OrderByDescending(p => p.ProductId)   : q.OrderBy(p => p.ProductId),
            _             => q.OrderBy(p => p.ProductName)
        };
    }
}

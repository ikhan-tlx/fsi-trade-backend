using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Workflow.ProductMapping;

public record GetProductMappingQuery(string SchemeCode) : IRequest<IReadOnlyList<int>>;

/// <summary>
/// Returns the IDs of every active product currently mapped to the given
/// workflow scheme. The mapping is stored on <c>TmX_Product.Workflow_Scheme_Code</c>
/// (i.e. the product row "owns" its scheme assignment — there is no
/// dedicated mapping table in this domain). Effective-date window mirrors
/// the legacy behaviour: today must fall inside [StartDate, EndDate].
///
/// Orchestration lives here in the Application layer; no vendor types.
/// </summary>
public class GetProductMappingQueryHandler : IRequestHandler<GetProductMappingQuery, IReadOnlyList<int>>
{
    private readonly IApplicationDbContext _db;

    public GetProductMappingQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<int>> Handle(GetProductMappingQuery req, CancellationToken ct)
    {
        var schemeCode = req.SchemeCode?.Trim() ?? "";
        if (schemeCode.Length == 0)
            return Array.Empty<int>();

        var now = DateTime.UtcNow;

        var ids = await _db.Products
            .AsNoTracking()
            .Where(p =>
                p.WorkflowSchemeCode  == schemeCode &&
                p.EffectiveStartDate  <= now &&
                p.EffectiveEndDate    >= now)
            .OrderBy(p => p.ProductId)
            .Select(p => p.ProductId)
            .ToListAsync(ct);

        return ids;
    }
}

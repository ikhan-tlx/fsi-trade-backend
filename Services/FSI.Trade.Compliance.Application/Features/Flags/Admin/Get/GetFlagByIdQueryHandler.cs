using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.Get;

public class GetFlagByIdQueryHandler
    : IRequestHandler<GetFlagByIdQuery, FlagCatalogueDetailDto>
{
    private readonly IApplicationDbContext _db;
    public GetFlagByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<FlagCatalogueDetailDto> Handle(
        GetFlagByIdQuery req, CancellationToken ct)
    {
        var c = await _db.FlagCatalogues.AsNoTracking()
            .FirstOrDefaultAsync(c => c.FlagId == req.FlagId, ct)
            ?? throw new NotFoundException("flag_not_found",
                $"Flag {req.FlagId} does not exist.");

        var scopes = await _db.FlagScopes.AsNoTracking()
            .Where(s => s.FlagId == req.FlagId)
            .OrderBy(s => s.ProductId)
            .ThenBy(s => s.TabId)
            .ThenBy(s => s.SortOrder)
            .Select(s => new FlagScopeDto
            {
                flagScopeId     = s.FlagScopeId,
                productId       = s.ProductId,
                tabId           = s.TabId,
                sortOrder       = s.SortOrder,
                activeFlag      = s.ActiveFlag,
                legacyFieldName = s.LegacyFieldName
            })
            .ToListAsync(ct);

        return new FlagCatalogueDetailDto
        {
            flagId             = c.FlagId,
            flagCode           = c.FlagCode,
            flagName           = c.FlagName,
            flagDescription    = c.FlagDescription,
            flagTypeLkpId      = c.FlagTypeLkpId,
            flagCategoryLkpId  = c.FlagCategoryLkpId,
            severityLkpId      = c.SeverityLkpId,
            defaultWeight      = c.DefaultWeight,
            requiresEvidence   = c.RequiresEvidence,
            sourceSystem       = c.SourceSystem,
            activeFlag         = c.ActiveFlag,
            createdBy          = c.CreatedBy,
            createdDate        = c.CreatedDate,
            lastUpdatedBy      = c.LastUpdatedBy,
            lastUpdatedDate    = c.LastUpdatedDate,
            scopes             = scopes
        };
    }
}

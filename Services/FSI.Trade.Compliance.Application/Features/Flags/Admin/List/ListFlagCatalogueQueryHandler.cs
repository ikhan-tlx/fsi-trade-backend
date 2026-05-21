using FSI.Trade.Compliance.Application.Common.Extensions;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Flags.Admin.List;

public class ListFlagCatalogueQueryHandler
    : IRequestHandler<ListFlagCatalogueQuery, PagedResult<FlagCatalogueListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListFlagCatalogueQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<FlagCatalogueListItemDto>> Handle(
        ListFlagCatalogueQuery req, CancellationToken ct)
    {
        IQueryable<FlagCatalogue> q = _db.FlagCatalogues.AsNoTracking();

        // Filter ----------------------------------------------------
        if (req.ActiveFlag.HasValue)
            q = q.Where(c => c.ActiveFlag == req.ActiveFlag.Value);

        if (req.CategoryLkpId.HasValue)
            q = q.Where(c => c.FlagCategoryLkpId == req.CategoryLkpId.Value);

        if (req.SeverityLkpId.HasValue)
            q = q.Where(c => c.SeverityLkpId == req.SeverityLkpId.Value);

        if (req.FlagTypeLkpId.HasValue)
            q = q.Where(c => c.FlagTypeLkpId == req.FlagTypeLkpId.Value);

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            q = q.Where(c =>
                   EF.Functions.Like(c.FlagCode,        $"%{f}%")
                || EF.Functions.Like(c.FlagName,        $"%{f}%")
                || EF.Functions.Like(c.FlagDescription, $"%{f}%"));
        }

        // Sort ------------------------------------------------------
        q = ApplySort(q, req.Sort);

        var total = await q.CountAsync(ct);

        // Project — scopeCount via correlated subquery (EF folds into one CROSS APPLY).
        var items = await q
            .Skip(req.Skip)
            .Take(req.Take)
            .Select(c => new FlagCatalogueListItemDto
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
                scopeCount         = _db.FlagScopes.Count(s => s.FlagId == c.FlagId && s.ActiveFlag),
                createdDate        = c.CreatedDate,
                lastUpdatedBy      = c.LastUpdatedBy,
                lastUpdatedDate    = c.LastUpdatedDate
            })
            .ToListAsync(ct);

        return new PagedResult<FlagCatalogueListItemDto>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private static IQueryable<FlagCatalogue> ApplySort(IQueryable<FlagCatalogue> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return q.OrderByDescending(c => c.CreatedDate);

        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return q.OrderByDescending(c => c.CreatedDate);

        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var dir   = parts.ElementAtOrDefault(1)?.ToLowerInvariant();
        var desc  = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "flagname"        => desc ? q.OrderByDescending(c => c.FlagName)        : q.OrderBy(c => c.FlagName),
            "flagcode"        => desc ? q.OrderByDescending(c => c.FlagCode)        : q.OrderBy(c => c.FlagCode),
            "createddate"     => desc ? q.OrderByDescending(c => c.CreatedDate)     : q.OrderBy(c => c.CreatedDate),
            "lastupdateddate" => desc ? q.OrderByDescending(c => c.LastUpdatedDate) : q.OrderBy(c => c.LastUpdatedDate),
            "defaultweight"   => desc ? q.OrderByDescending(c => c.DefaultWeight)   : q.OrderBy(c => c.DefaultWeight),
            _                 => q.OrderByDescending(c => c.CreatedDate)
        };
    }
}

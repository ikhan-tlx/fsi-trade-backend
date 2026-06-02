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
        // Soft-deleted flags are never returned.
        IQueryable<FlagCatalogue> q = _db.FlagCatalogues.AsNoTracking()
            .Where(c => !c.IsDeleted);

        // Filter ----------------------------------------------------
        if (req.ActiveFlag.HasValue)
            q = q.Where(c => c.ActiveFlag == req.ActiveFlag.Value);

        // Scope filter ("All scopes" dropdown). GlobalScopeOnly wins; else
        // a productId match also pulls in global (all-products) scopes.
        if (req.GlobalScopeOnly == true)
        {
            q = q.Where(c => _db.FlagScopes.Any(s => s.FlagId == c.FlagId && s.ProductId == null));
        }
        else if (req.ProductId.HasValue)
        {
            var pid = req.ProductId.Value;
            var tid = req.TabId;
            q = q.Where(c => _db.FlagScopes.Any(s =>
                    s.FlagId == c.FlagId
                 && (s.ProductId == null
                     || (s.ProductId == pid
                         && (tid == null || s.TabId == null || s.TabId == tid)))));
        }

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
                severityLkpId      = c.SeverityLkpId,
                defaultWeight      = c.DefaultWeight,
                requiresEvidence   = c.RequiresEvidence,
                sourceSystem       = c.SourceSystem,
                activeFlag         = c.ActiveFlag,
                isBuiltIn          = c.IsBuiltIn,
                scopeCount         = _db.FlagScopes.Count(s => s.FlagId == c.FlagId && s.ActiveFlag),
                createdDate        = c.CreatedDate,
                lastUpdatedBy      = c.LastUpdatedBy,
                lastUpdatedDate    = c.LastUpdatedDate
            })
            .ToListAsync(ct);

        // Enrich each page item with its scopes (resolved product / tab
        // names) in ONE round-trip — no N+1. Left-joins so a global scope
        // (ProductId NULL) keeps a null productName for the FE to label
        // as "All products".
        var flagIds = items.Select(i => i.flagId).ToList();
        if (flagIds.Count > 0)
        {
            var scopeRows = await (
                from s in _db.FlagScopes.AsNoTracking().Where(s => flagIds.Contains(s.FlagId))
                // Cast to int? so the join key types match (s.ProductId is
                // nullable for all-products scopes). A global scope won't
                // match any product → left-join yields null productName.
                join p in _db.Products.AsNoTracking() on s.ProductId equals (int?)p.ProductId into pj
                from p in pj.DefaultIfEmpty()
                join t in _db.Tabs.AsNoTracking() on s.TabId equals (int?)t.TabId into tj
                from t in tj.DefaultIfEmpty()
                orderby s.ProductId, s.TabId, s.SortOrder
                select new
                {
                    s.FlagId,
                    Summary = new FlagScopeSummaryDto
                    {
                        flagScopeId = s.FlagScopeId,
                        productId   = s.ProductId,
                        productName = p != null ? p.ProductName : null,
                        tabId       = s.TabId,
                        tabName     = t != null ? t.TabName : null,
                        activeFlag  = s.ActiveFlag
                    }
                })
                .ToListAsync(ct);

            var byFlag = scopeRows
                .GroupBy(r => r.FlagId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Summary).ToList());

            foreach (var item in items)
                if (byFlag.TryGetValue(item.flagId, out var sc))
                    item.scopes = sc;
        }

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

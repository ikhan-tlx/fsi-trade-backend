using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.CompanyBranches.List;

/// <summary>
/// Server-side paged / sorted / filtered list over <c>TmX_Company_Branch</c>.
/// Mirrors the Role list handler: filter (LIKE across whitelisted columns) →
/// sort (whitelist switch) → Count → Skip/Take → project to DTO.
/// </summary>
public class ListCompanyBranchesQueryHandler
    : IRequestHandler<ListCompanyBranchesQuery, PagedResult<CompanyBranchListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListCompanyBranchesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<CompanyBranchListItemDto>> Handle(
        ListCompanyBranchesQuery req,
        CancellationToken ct)
    {
        IQueryable<CompanyBranch> q = _db.CompanyBranches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            q = q.Where(b =>
                   EF.Functions.Like(b.BranchCode, $"%{f}%")
                || (b.BranchName != null && EF.Functions.Like(b.BranchName, $"%{f}%"))
                || (b.BranchDescription != null && EF.Functions.Like(b.BranchDescription, $"%{f}%")));
        }

        q = ApplySort(q, req.Sort);

        var total = await q.CountAsync(ct);

        var items = await q
            .Skip(req.Skip)
            .Take(req.Take)
            .Select(b => new CompanyBranchListItemDto
            {
                companyBranchId    = b.CompanyBranchId,
                branchCode         = b.BranchCode,
                branchName         = b.BranchName,
                branchDescription  = b.BranchDescription,
                locationId         = b.LocationId,
                companyId          = b.CompanyId,
                status             = b.Status ?? (b.ActiveFlag ? "Active" : "Inactive"),
                activeFlag         = b.ActiveFlag,
                effectiveStartDate = b.EffectiveStartDate,
                effectiveEndDate   = b.EffectiveEndDate,
                createdDate        = b.CreatedDate
            })
            .ToListAsync(ct);

        return new PagedResult<CompanyBranchListItemDto>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private static IQueryable<CompanyBranch> ApplySort(IQueryable<CompanyBranch> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return q.OrderBy(b => b.BranchName);

        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
        if (first is null) return q.OrderBy(b => b.BranchName);

        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var desc  = string.Equals(parts.ElementAtOrDefault(1), "desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "branchname"      => desc ? q.OrderByDescending(b => b.BranchName)  : q.OrderBy(b => b.BranchName),
            "branchcode"      => desc ? q.OrderByDescending(b => b.BranchCode)  : q.OrderBy(b => b.BranchCode),
            "status"          => desc ? q.OrderByDescending(b => b.Status)      : q.OrderBy(b => b.Status),
            "companybranchid" => desc ? q.OrderByDescending(b => b.CompanyBranchId) : q.OrderBy(b => b.CompanyBranchId),
            _                 => q.OrderBy(b => b.BranchName)
        };
    }
}

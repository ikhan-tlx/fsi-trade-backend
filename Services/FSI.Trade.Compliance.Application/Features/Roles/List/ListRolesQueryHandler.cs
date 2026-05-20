using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.List;

public class ListRolesQueryHandler : IRequestHandler<ListRolesQuery, PagedResult<RoleListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListRolesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<RoleListItemDto>> Handle(ListRolesQuery req, CancellationToken ct)
    {
        // NOTE — Active_Flag intentionally NOT filtered (see BACKLOG).
        IQueryable<Role> q = _db.Roles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            q = q.Where(r => EF.Functions.Like(r.Name, $"%{f}%")
                          || (r.Description != null && EF.Functions.Like(r.Description, $"%{f}%")));
        }

        q = ApplySort(q, req.Sort);

        var total = await q.CountAsync(ct);

        // Project + paginate. The user-count subquery joins TmX_User_Role_Mapping;
        // EF translates this to one CROSS APPLY in SQL Server, no N+1.
        var items = await q
            .Skip(req.Skip)
            .Take(req.Take)
            .Select(r => new RoleListItemDto
            {
                roleId          = r.Id,
                roleName        = r.Name,
                roleDescription = r.Description,
                isActive        = r.IsActive,
                createdDate     = r.CreatedDate,
                createdBy       = r.CreatedBy,
                lastUpdatedDate = r.LastUpdatedDate,
                lastUpdatedBy   = r.LastUpdatedBy,
                userCount       = _db.UserRoleMappings.Count(urm => urm.RoleId == r.Id)
            })
            .ToListAsync(ct);

        return new PagedResult<RoleListItemDto>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private static IQueryable<Role> ApplySort(IQueryable<Role> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return q.OrderByDescending(r => r.CreatedDate);

        // Format: "field-direction" (Kendo). Multiple sorts separated by comma; only first honoured.
        var first    = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return q.OrderByDescending(r => r.CreatedDate);

        var parts    = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field    = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var dir      = parts.ElementAtOrDefault(1)?.ToLowerInvariant();
        var desc     = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "rolename"        => desc ? q.OrderByDescending(r => r.Name)        : q.OrderBy(r => r.Name),
            "roledescription" => desc ? q.OrderByDescending(r => r.Description) : q.OrderBy(r => r.Description),
            "isactive"        => desc ? q.OrderByDescending(r => r.IsActive)    : q.OrderBy(r => r.IsActive),
            "createddate"     => desc ? q.OrderByDescending(r => r.CreatedDate) : q.OrderBy(r => r.CreatedDate),
            _                 => q.OrderByDescending(r => r.CreatedDate)
        };
    }
}

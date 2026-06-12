using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.List;

public class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, PagedResult<UserListItemDto>>
{
    private readonly IApplicationDbContext _db;
    public ListUsersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<UserListItemDto>> Handle(ListUsersQuery req, CancellationToken ct)
    {
        IQueryable<ApplicationUser> q = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            // Match the term across the individual columns AND against the
            // combined "First Last" display name, so a search like "John Doe"
            // (the value shown in the grid's Name column) actually matches —
            // not just a single first/last token.
            q = q.Where(u =>
                   (u.UserName  != null && EF.Functions.Like(u.UserName,  $"%{f}%"))
                || (u.Email     != null && EF.Functions.Like(u.Email,     $"%{f}%"))
                || (u.FirstName != null && EF.Functions.Like(u.FirstName, $"%{f}%"))
                || (u.LastName  != null && EF.Functions.Like(u.LastName,  $"%{f}%"))
                || EF.Functions.Like(
                       ((u.FirstName ?? "") + " " + (u.LastName ?? "")), $"%{f}%"));
        }

        q = ApplySort(q, req.Sort);

        var total = await q.CountAsync(ct);

        // Project base user rows with two correlated subqueries — `now`-vs-`LockoutEndDateUtc`
        // for the lockout flag, and the role-IDs subquery (we hydrate role names below).
        var pageData = await q
            .Skip(req.Skip)
            .Take(req.Take)
            .Select(u => new
            {
                u.Id, u.UserName, u.Email, u.FirstName, u.MiddleName, u.LastName,
                u.PhoneNumber, u.Status, u.ActiveFlag,
                u.LockoutEndDateUtc, u.LastLoginDate, u.CreatedDate, u.CreatedBy
            })
            .ToListAsync(ct);

        var pageIds = pageData.Select(u => u.Id).ToList();

        // Hydrate roles for the page in one query — avoids N+1 across the page.
        var roleMap = await _db.UserRoleMappings
            .AsNoTracking()
            .Where(urm => pageIds.Contains(urm.UserId))
            .Join(
                _db.Roles.AsNoTracking(),
                urm => urm.RoleId,
                r   => r.Id,
                (urm, r) => new { urm.UserId, RoleId = r.Id, RoleName = r.Name })
            .ToListAsync(ct);

        var roleLookup = roleMap
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new UserRoleRefDto { roleId = x.RoleId, roleName = x.RoleName }).ToList());

        // Hydrate each user's branch for the page in one query. This is a
        // DISPLAY column, so we do NOT gate on Active_Flag or the effective-date
        // window (the legacy grid shows the mapped branch even for inactive
        // mappings). Prefer an active mapping when the user has several.
        var branchMap = await _db.CompanyBranchUserMappings
            .AsNoTracking()
            .Where(m => pageIds.Contains(m.UserId))
            .Join(
                _db.CompanyBranches.AsNoTracking(),
                m => m.CompanyBranchId,
                b => b.CompanyBranchId,
                (m, b) => new { m.UserId, b.CompanyBranchId, b.BranchName, m.ActiveFlag })
            .ToListAsync(ct);

        var branchLookup = branchMap
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.ActiveFlag)
                      .Select(x => new { x.CompanyBranchId, x.BranchName })
                      .FirstOrDefault());

        var now   = DateTime.UtcNow;
        var items = pageData.Select(u => new UserListItemDto
        {
            userId        = u.Id,
            userName      = u.UserName,
            emailAddress  = u.Email,
            firstName     = u.FirstName,
            middleName    = u.MiddleName,
            lastName      = u.LastName,
            phoneNumber   = u.PhoneNumber,
            status        = u.Status,
            isActive      = u.ActiveFlag,
            isLockedOut   = u.LockoutEndDateUtc.HasValue && u.LockoutEndDateUtc.Value > now,
            lastLoginDate = u.LastLoginDate,
            createdDate   = u.CreatedDate,
            createdBy     = u.CreatedBy,
            branchId      = branchLookup.TryGetValue(u.Id, out var br) ? br?.CompanyBranchId : null,
            branchName    = branchLookup.TryGetValue(u.Id, out var br2) ? br2?.BranchName : null,
            roles         = roleLookup.TryGetValue(u.Id, out var rs) ? rs : new List<UserRoleRefDto>()
        }).ToList();

        return new PagedResult<UserListItemDto>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private static IQueryable<ApplicationUser> ApplySort(IQueryable<ApplicationUser> q, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return q.OrderByDescending(u => u.CreatedDate);

        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return q.OrderByDescending(u => u.CreatedDate);

        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var dir   = parts.ElementAtOrDefault(1)?.ToLowerInvariant();
        var desc  = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "username"      => desc ? q.OrderByDescending(u => u.UserName)      : q.OrderBy(u => u.UserName),
            "emailaddress"  => desc ? q.OrderByDescending(u => u.Email)         : q.OrderBy(u => u.Email),
            "firstname"     => desc ? q.OrderByDescending(u => u.FirstName)     : q.OrderBy(u => u.FirstName),
            "lastname"      => desc ? q.OrderByDescending(u => u.LastName)      : q.OrderBy(u => u.LastName),
            "status"        => desc ? q.OrderByDescending(u => u.Status)        : q.OrderBy(u => u.Status),
            "isactive"      => desc ? q.OrderByDescending(u => u.ActiveFlag)    : q.OrderBy(u => u.ActiveFlag),
            "lastlogindate" => desc ? q.OrderByDescending(u => u.LastLoginDate) : q.OrderBy(u => u.LastLoginDate),
            "createddate"   => desc ? q.OrderByDescending(u => u.CreatedDate)   : q.OrderBy(u => u.CreatedDate),
            _               => q.OrderByDescending(u => u.CreatedDate)
        };
    }
}

using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPrivilegeService"/>. Single canonical
/// query — given a set of role names, return every privilege code wired to
/// any of those roles:
///
///   SELECT DISTINCT p.Privilege_Name
///   FROM   dbo.TmX_Role r
///   JOIN   dbo.TmX_Role_Privilege_Mapping rpm ON rpm.Role_ID = r.Role_ID
///   JOIN   dbo.TmX_Privilege p                ON p.Privilege_ID = rpm.Privilege_ID
///   WHERE  r.Role_Name IN @roleNames
///     AND  p.Privilege_Name IS NOT NULL
///
/// NOTE — Active_Flag intentionally NOT filtered. Per FSI team direction
/// (May 2026) the lifecycle semantics for Active_Flag on these RBAC tables
/// haven't been formalised, so every row is treated as live. Tracked in
/// BACKLOG.md ("Active_Flag handling on RBAC tables").
/// </summary>
public class PrivilegeService : IPrivilegeService
{
    private readonly IApplicationDbContext _db;
    public PrivilegeService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyCollection<string>> GetPrivilegesForRolesAsync(
        IEnumerable<string> roleNames,
        CancellationToken   ct = default)
    {
        if (roleNames is null) return Array.Empty<string>();

        // Materialise + uppercase-normalise to a deterministic set so the EF
        // translation produces a stable IN-clause.
        var names = roleNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0) return Array.Empty<string>();

        // Resolve role IDs first. Two trips is cheaper than a 3-way join over a
        // string IN-list because Role_Name isn't indexed and Role_ID is the PK.
        var roleIds = await _db.Roles
            .AsNoTracking()
            .Where(r => names.Contains(r.Name))
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (roleIds.Count == 0) return Array.Empty<string>();

        var codes = await _db.RolePrivilegeMappings
            .AsNoTracking()
            .Where(rpm => roleIds.Contains(rpm.RoleId))
            .Join(
                _db.Privileges.AsNoTracking(),
                rpm => rpm.PrivilegeId,
                p   => p.Id,
                (_, p) => p.Name)
            .Where(name => name != null)
            .Select(name => name!)
            .Distinct()
            .ToListAsync(ct);

        return codes;
    }
}

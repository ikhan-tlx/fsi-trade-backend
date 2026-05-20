using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core 8 implementation of IRoleQueryService. Mirrors the canonical SQL:
///
///   SELECT r.Role_Name
///   FROM   dbo.TmX_User_Role_Mapping urm
///   JOIN   dbo.TmX_Role r ON r.Role_ID = urm.Role_Id
///   WHERE  urm.User_Id = @userId
/// </summary>
public class RoleQueryService : IRoleQueryService
{
    private readonly IApplicationDbContext _db;
    public RoleQueryService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken ct = default)
    {
        return await _db.UserRoleMappings
            .AsNoTracking()
            .Where(urm => urm.UserId == userId)
            .Join(
                _db.Roles.AsNoTracking(),
                urm => urm.RoleId,
                r   => r.Id,
                (_, r) => r.Name)
            .ToListAsync(ct);
    }
}

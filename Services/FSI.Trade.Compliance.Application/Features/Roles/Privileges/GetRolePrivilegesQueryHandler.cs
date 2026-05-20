using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.Privileges;

public class GetRolePrivilegesQueryHandler : IRequestHandler<GetRolePrivilegesQuery, List<RolePrivilegeDto>>
{
    private readonly IApplicationDbContext _db;
    public GetRolePrivilegesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<RolePrivilegeDto>> Handle(GetRolePrivilegesQuery req, CancellationToken ct)
    {
        var exists = await _db.Roles.AsNoTracking().AnyAsync(r => r.Id == req.RoleId, ct);
        if (!exists)
            throw new NotFoundException("role_not_found", $"Role {req.RoleId} not found.");

        // NOTE — Active_Flag intentionally NOT filtered (see BACKLOG).
        return await _db.RolePrivilegeMappings
            .AsNoTracking()
            .Where(rpm => rpm.RoleId == req.RoleId)
            .Join(
                _db.Privileges.AsNoTracking(),
                rpm => rpm.PrivilegeId,
                p   => p.Id,
                (_, p) => new RolePrivilegeDto
                {
                    privilegeId = p.Id,
                    code        = p.Name ?? "",
                    description = p.Description
                })
            .OrderBy(d => d.code)
            .ToListAsync(ct);
    }
}

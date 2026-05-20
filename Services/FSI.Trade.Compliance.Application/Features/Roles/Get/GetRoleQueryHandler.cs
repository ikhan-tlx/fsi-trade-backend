using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.Get;

public class GetRoleQueryHandler : IRequestHandler<GetRoleQuery, RoleDetailDto>
{
    private readonly IApplicationDbContext _db;
    public GetRoleQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<RoleDetailDto> Handle(GetRoleQuery req, CancellationToken ct)
    {
        var dto = await _db.Roles
            .AsNoTracking()
            .Where(r => r.Id == req.RoleId)
            .Select(r => new RoleDetailDto
            {
                roleId             = r.Id,
                tenantId           = r.TenantId,
                roleName           = r.Name,
                roleDescription    = r.Description,
                isActive           = r.IsActive,
                effectiveStartDate = r.EffectiveStartDate,
                effectiveEndDate   = r.EffectiveEndDate,
                createdDate        = r.CreatedDate,
                createdBy          = r.CreatedBy,
                lastUpdatedDate    = r.LastUpdatedDate,
                lastUpdatedBy      = r.LastUpdatedBy,
                userCount          = _db.UserRoleMappings.Count(urm => urm.RoleId == r.Id)
            })
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException("role_not_found", $"Role {req.RoleId} not found.");
    }
}

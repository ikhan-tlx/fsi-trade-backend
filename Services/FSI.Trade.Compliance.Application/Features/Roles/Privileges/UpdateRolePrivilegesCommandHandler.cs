using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.Privileges;

public class UpdateRolePrivilegesCommandHandler : IRequestHandler<UpdateRolePrivilegesCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public UpdateRolePrivilegesCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(UpdateRolePrivilegesCommand req, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.roleId, ct)
                   ?? throw new NotFoundException("role_not_found", $"Role {req.roleId} not found.");

        // Distinct-and-positive set of requested privilege IDs.
        var requested = req.privilegeIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        // Validate that every requested ID exists. Reject the whole batch
        // rather than silently ignore unknown IDs (FE bug → loud error).
        if (requested.Count > 0)
        {
            var validIds = await _db.Privileges
                .AsNoTracking()
                .Where(p => requested.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct);

            var missing = requested.Except(validIds).ToList();
            if (missing.Count > 0)
                throw new NotFoundException(
                    "privilege_not_found",
                    $"Unknown privilege IDs: {string.Join(", ", missing)}.");
        }

        var existing = await _db.RolePrivilegeMappings
            .Where(rpm => rpm.RoleId == role.Id)
            .ToListAsync(ct);
        var existingIds = existing.Select(e => e.PrivilegeId).ToHashSet();

        // Diff — additions and removals.
        var toAdd    = requested.Where(id => !existingIds.Contains(id)).ToList();
        var toRemove = existing.Where(e => !requested.Contains(e.PrivilegeId)).ToList();

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var now   = DateTime.UtcNow;
        var far   = new DateTime(9999, 12, 31);

        foreach (var id in toAdd)
        {
            _db.RolePrivilegeMappings.Add(new RolePrivilegeMapping
            {
                TenantId           = role.TenantId,
                RoleId             = role.Id,
                PrivilegeId        = id,
                IsActive           = true,
                EffectiveStartDate = now,
                EffectiveEndDate   = far,
                CreatedBy          = actor,
                CreatedDate        = now,
                LastUpdatedBy      = null,
                LastUpdatedDate    = null
            });
        }

        if (toRemove.Count > 0)
            _db.RolePrivilegeMappings.RemoveRange(toRemove);

        // Touch the role's audit columns so the change is visible at the role level too.
        role.LastUpdatedBy   = actor;
        role.LastUpdatedDate = now;

        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}

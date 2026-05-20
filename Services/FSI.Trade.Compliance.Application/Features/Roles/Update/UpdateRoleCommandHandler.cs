using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.Update;

public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public UpdateRoleCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(UpdateRoleCommand req, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.roleId, ct)
                   ?? throw new NotFoundException("role_not_found", $"Role {req.roleId} not found.");

        var newName = req.roleName.Trim();

        // Uniqueness — only if the name is actually changing.
        if (!string.Equals(role.Name, newName, StringComparison.Ordinal))
        {
            var taken = await _db.Roles
                .AsNoTracking()
                .AnyAsync(r => r.Id != req.roleId && r.Name == newName, ct);
            if (taken)
                throw new ConflictException("role_name_taken", $"A role named '{newName}' already exists.");
        }

        role.Name            = newName;
        role.Description     = string.IsNullOrWhiteSpace(req.roleDescription) ? null : req.roleDescription.Trim();
        role.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        role.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Roles.SetActive;

public class SetRoleActiveCommandHandler : IRequestHandler<SetRoleActiveCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public SetRoleActiveCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<Unit> Handle(SetRoleActiveCommand req, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.RoleId, ct)
                   ?? throw new NotFoundException("role_not_found", $"Role {req.RoleId} not found.");

        if (role.IsActive == req.Active)
        {
            // No-op — already in the requested state. Return success quietly so
            // the FE can hit the endpoint without first checking current state.
            return Unit.Value;
        }

        role.IsActive        = req.Active;
        role.LastUpdatedBy   = _current.UserName ?? _current.UserId ?? "unknown";
        role.LastUpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

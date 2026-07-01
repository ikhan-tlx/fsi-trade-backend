using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.SetActive;

public class SetUserActiveCommandHandler : IRequestHandler<SetUserActiveCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IDeviceService        _devices;

    public SetUserActiveCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService   current,
        IDeviceService        devices)
    {
        _db      = db;
        _current = current;
        _devices = devices;
    }

    public async Task<Unit> Handle(SetUserActiveCommand req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, ct)
                   ?? throw new NotFoundException("user_not_found", $"User '{req.UserId}' not found.");

        if (user.ActiveFlag == req.Active)
            return Unit.Value;   // idempotent — no-op if already in requested state

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var now   = DateTime.UtcNow;

        user.ActiveFlag      = req.Active;
        user.Status          = req.Active ? "Active" : "InActive";
        user.LastUpdatedBy   = actor;
        user.LastUpdatedDate = now;

        if (req.Active)
        {
            // Reactivation clears the deactivation stamp.
            user.DeactivatedAt = null;
            user.DeactivatedBy = null;
        }
        else
        {
            user.DeactivatedAt = now;
            user.DeactivatedBy = actor;
        }

        await _db.SaveChangesAsync(ct);

        // Security: a deactivated account must not keep working on existing
        // tokens. Revoke all devices + refresh tokens immediately so the next
        // request (or refresh) is rejected.
        if (!req.Active)
            await _devices.RevokeAllForUserAsync(user.Id, "AccountDeactivated", null, ct);

        return Unit.Value;
    }
}

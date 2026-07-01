using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Users.Lock;

public class LockUserCommandHandler : IRequestHandler<LockUserCommand, Unit>
{
    // Far-future sentinel = "locked until an admin unlocks". Login/refresh both
    // gate on LockoutEndDateUtc > now, so this blocks access indefinitely.
    private static readonly DateTime IndefiniteLockUntil = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IDeviceService        _devices;

    public LockUserCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService   current,
        IDeviceService        devices)
    {
        _db      = db;
        _current = current;
        _devices = devices;
    }

    public async Task<Unit> Handle(LockUserCommand req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, ct)
                   ?? throw new NotFoundException("user_not_found", $"User '{req.UserId}' not found.");

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var now   = DateTime.UtcNow;

        user.LockoutEndDateUtc = IndefiniteLockUntil;
        user.LockoutReason     = "AdminLock";
        user.LockedAt          = now;
        user.LockedBy          = actor;
        user.UnlockedAt        = null;
        user.UnlockedBy        = null;
        user.LastUpdatedBy     = actor;
        user.LastUpdatedDate   = now;

        await _db.SaveChangesAsync(ct);

        // Kill live sessions so the lock takes effect immediately.
        await _devices.RevokeAllForUserAsync(user.Id, "AccountLocked", null, ct);

        return Unit.Value;
    }
}

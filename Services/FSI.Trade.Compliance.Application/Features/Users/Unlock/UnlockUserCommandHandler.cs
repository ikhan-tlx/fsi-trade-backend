using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.Unlock;

public class UnlockUserCommandHandler : IRequestHandler<UnlockUserCommand, Unit>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly ICurrentUserService        _current;

    public UnlockUserCommandHandler(IUserAuthenticationService userAuth, ICurrentUserService current)
    {
        _userAuth = userAuth;
        _current  = current;
    }

    public async Task<Unit> Handle(UnlockUserCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.UserId, ct)
                   ?? throw new NotFoundException("user_not_found", $"User '{req.UserId}' not found.");

        // Generic primitive — UserManager-backed: clears LockoutEndDateUtc and
        // resets AccessFailedCount. Audit is appended via the LastUpdatedBy/Date
        // touch, applied through UpdateUserAsync so the timestamp lands on the row.
        await _userAuth.UnlockAsync(user, ct);

        var actor = _current.UserName ?? _current.UserId ?? "unknown";
        var now   = DateTime.UtcNow;

        // Clear lock telemetry (auto or admin) and stamp who/when unlocked.
        user.LockoutReason   = null;
        user.LockedAt        = null;
        user.LockedBy        = null;
        user.UnlockedAt      = now;
        user.UnlockedBy      = actor;
        user.LastUpdatedBy   = actor;
        user.LastUpdatedDate = now;
        await _userAuth.UpdateUserAsync(user, ct);

        return Unit.Value;
    }
}

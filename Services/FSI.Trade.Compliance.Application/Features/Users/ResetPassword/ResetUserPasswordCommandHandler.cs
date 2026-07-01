using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.ResetPassword;

public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, Unit>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly ICurrentUserService        _current;
    private readonly IDeviceService             _devices;

    public ResetUserPasswordCommandHandler(
        IUserAuthenticationService userAuth,
        ICurrentUserService        current,
        IDeviceService             devices)
    {
        _userAuth = userAuth;
        _current  = current;
        _devices  = devices;
    }

    public async Task<Unit> Handle(ResetUserPasswordCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.userId, ct)
                   ?? throw new NotFoundException("user_not_found", $"User '{req.userId}' not found.");

        var (ok, errors) = await _userAuth.SetPasswordAsync(user, req.newPassword, ct);
        if (!ok)
            throw new ConflictException("password_reset_failed",
                errors.Count > 0 ? string.Join(" ", errors) : "The password could not be reset.");

        var actor = _current.UserName ?? _current.UserId ?? "unknown";

        // Force a change on next login (both the legacy gate flag and the new
        // canonical flag), and stamp the audit fields.
        user.FirstPasswordChange = true;
        user.MustChangePassword  = true;
        user.LastUpdatedBy       = actor;
        user.LastUpdatedDate     = DateTime.UtcNow;
        await _userAuth.UpdateUserAsync(user, ct);

        // Kill existing sessions so the old password can't keep a session alive.
        await _devices.RevokeAllForUserAsync(user.Id, "PasswordResetByAdmin", null, ct);

        return Unit.Value;
    }
}

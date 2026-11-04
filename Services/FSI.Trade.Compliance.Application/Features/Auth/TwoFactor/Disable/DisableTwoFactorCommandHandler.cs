using MediatR;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Disable;

public class DisableTwoFactorCommandHandler : IRequestHandler<DisableTwoFactorCommand, Unit>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly IOptionsSnapshot<TwoFactorOptions> _twoFactor;

    public DisableTwoFactorCommandHandler(
        IUserAuthenticationService userAuth,
        IOptionsSnapshot<TwoFactorOptions> twoFactor)
    {
        _userAuth  = userAuth;
        _twoFactor = twoFactor;
    }

    public async Task<Unit> Handle(DisableTwoFactorCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.UserId, ct)
                   ?? throw new AuthenticationException("user_not_found");

        // When 2FA is enforced org-wide, a user can't turn off their own 2FA — the
        // next login would just force re-enrolment anyway. The only way to clear it is
        // an admin "Reset 2FA" (e.g. lost device). Block self-disable with a clear reason.
        if (_twoFactor.Value.Enabled)
            throw new ForbiddenException(
                "two_factor_enforced",
                "Two-factor authentication is enforced by your administrator and can't be turned off. " +
                "If you've lost your device, ask an administrator to reset it.");

        if (!await _userAuth.CheckPasswordAsync(user, req.CurrentPassword, ct))
            throw new AuthenticationException("invalid_grant", "Current password is incorrect.");

        user.TwoFactorEnabled          = false;
        user.TwoFactorAuthenticatorKey = null;
        await _userAuth.UpdateUserAsync(user, ct);

        return Unit.Value;
    }
}

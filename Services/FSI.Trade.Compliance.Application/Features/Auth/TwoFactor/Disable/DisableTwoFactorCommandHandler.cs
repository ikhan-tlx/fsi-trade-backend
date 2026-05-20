using MediatR;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Disable;

public class DisableTwoFactorCommandHandler : IRequestHandler<DisableTwoFactorCommand, Unit>
{
    private readonly IUserAuthenticationService _userAuth;

    public DisableTwoFactorCommandHandler(IUserAuthenticationService userAuth)
        => _userAuth = userAuth;

    public async Task<Unit> Handle(DisableTwoFactorCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.UserId, ct)
                   ?? throw new AuthenticationException("user_not_found");

        if (!await _userAuth.CheckPasswordAsync(user, req.CurrentPassword, ct))
            throw new AuthenticationException("invalid_grant", "Current password is incorrect.");

        user.TwoFactorEnabled          = false;
        user.TwoFactorAuthenticatorKey = null;
        await _userAuth.UpdateUserAsync(user, ct);

        return Unit.Value;
    }
}

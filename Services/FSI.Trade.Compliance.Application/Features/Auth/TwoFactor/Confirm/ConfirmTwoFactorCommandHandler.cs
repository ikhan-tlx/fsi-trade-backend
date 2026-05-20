using MediatR;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Confirm;

public class ConfirmTwoFactorCommandHandler : IRequestHandler<ConfirmTwoFactorCommand, Unit>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly ITwoFactorVerifier         _verifier;

    public ConfirmTwoFactorCommandHandler(
        IUserAuthenticationService userAuth,
        ITwoFactorVerifier         verifier)
    {
        _userAuth = userAuth;
        _verifier = verifier;
    }

    public async Task<Unit> Handle(ConfirmTwoFactorCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.UserId, ct)
                   ?? throw new AuthenticationException("user_not_found");

        if (string.IsNullOrEmpty(user.TwoFactorAuthenticatorKey))
            throw new AuthenticationException(
                "otp_not_provisioned",
                "Call EnableTwoFactor first to provision a secret.");

        if (!_verifier.Verify(user.TwoFactorAuthenticatorKey, req.Otp))
            throw new AuthenticationException("invalid_otp", "One-time password is invalid.");

        user.TwoFactorEnabled = true;
        await _userAuth.UpdateUserAsync(user, ct);

        return Unit.Value;
    }
}

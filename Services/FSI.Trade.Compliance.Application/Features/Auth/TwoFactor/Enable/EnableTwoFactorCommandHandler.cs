using MediatR;
using Microsoft.Extensions.Options;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Enable;

public class EnableTwoFactorCommandHandler : IRequestHandler<EnableTwoFactorCommand, EnableTwoFactorResponse>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly ITwoFactorSecretGenerator  _secretGen;
    private readonly TwoFactorOptions           _opt;

    public EnableTwoFactorCommandHandler(
        IUserAuthenticationService userAuth,
        ITwoFactorSecretGenerator  secretGen,
        IOptions<TwoFactorOptions> opt)
    {
        _userAuth  = userAuth;
        _secretGen = secretGen;
        _opt       = opt.Value;
    }

    public async Task<EnableTwoFactorResponse> Handle(EnableTwoFactorCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.UserId, ct)
                   ?? throw new AuthenticationException("user_not_found");

        var secret = _secretGen.GenerateSecret();
        var uri    = _secretGen.BuildProvisioningUri(secret,
                        accountName: user.UserName ?? user.Id,
                        issuer:      _opt.Issuer);

        // Store the secret but do NOT flip TwoFactorEnabled until the user
        // confirms with a valid code from their authenticator.
        user.TwoFactorAuthenticatorKey = secret;
        user.TwoFactorEnabled          = false;
        await _userAuth.UpdateUserAsync(user, ct);

        return new EnableTwoFactorResponse
        {
            Secret          = secret,
            ProvisioningUri = uri
        };
    }
}

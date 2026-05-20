using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Enable;

/// <summary>
/// Step 1 of 2FA setup. Authenticated. Generates a fresh TOTP secret and
/// returns it with a provisioning URI. Does NOT enable 2FA yet —
/// <see cref="Confirm.ConfirmTwoFactorCommand"/> finalises after the user
/// proves the secret was registered correctly.
/// </summary>
public sealed class EnableTwoFactorCommand : IRequest<EnableTwoFactorResponse>
{
    public string UserId { get; init; } = "";
}

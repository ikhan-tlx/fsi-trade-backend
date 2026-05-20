using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Confirm;

/// <summary>
/// Step 2 of 2FA setup. Verifies the user typed a valid OTP from their
/// authenticator app — proves they registered the secret correctly. On
/// success, sets <c>TwoFactorEnabled = true</c>.
/// </summary>
public sealed class ConfirmTwoFactorCommand : IRequest<Unit>
{
    public string UserId { get; init; } = "";
    public string Otp    { get; init; } = "";
}

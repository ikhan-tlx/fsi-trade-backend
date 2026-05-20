using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Disable;

/// <summary>
/// Disables 2FA for the caller. Requires the current password — defends
/// against a stolen Bearer being used to drop 2FA without the legitimate
/// user knowing.
/// </summary>
public sealed class DisableTwoFactorCommand : IRequest<Unit>
{
    public string UserId          { get; init; } = "";
    public string CurrentPassword { get; init; } = "";
}

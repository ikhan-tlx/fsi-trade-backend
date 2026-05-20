using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.ResetExpiredPassword;

/// <summary>
/// Anonymous endpoint. Lets a user out of the FirstPasswordChange /
/// PasswordExpiry trap without admin intervention. Verifies current password,
/// and only allows the reset if the user is actually in a "needs reset" state.
/// </summary>
public sealed class ResetExpiredPasswordCommand : IRequest<Unit>
{
    public string  Username        { get; init; } = "";
    public string  CurrentPassword { get; init; } = "";
    public string  NewPassword     { get; init; } = "";
    public string? Ip              { get; init; }
}

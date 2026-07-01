using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.ResetPassword;

/// <summary>Admin-initiated password reset. Sets a temporary password and forces
/// the user to change it on next login. No old-password check.</summary>
public class ResetUserPasswordCommand : IRequest<Unit>
{
    public string userId      { get; set; } = "";
    public string newPassword { get; set; } = "";
}

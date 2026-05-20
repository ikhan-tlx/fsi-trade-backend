using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.ChangePassword;

public sealed class ChangePasswordCommand : IRequest<Unit>
{
    public string  UserId      { get; init; } = "";
    public string  OldPassword { get; init; } = "";
    public string  NewPassword { get; init; } = "";
    public string? Ip          { get; init; }
}

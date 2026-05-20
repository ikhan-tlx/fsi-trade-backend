using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.Logout;

public sealed class LogoutCommand : IRequest<Unit>
{
    public string  UserId        { get; init; } = "";
    public string? refresh_token { get; init; }   // null = revoke all caller's tokens
    public string? Ip            { get; init; }
}

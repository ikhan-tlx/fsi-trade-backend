using MediatR;
using FSI.Trade.Compliance.Application.Common.Models;

namespace FSI.Trade.Compliance.Application.Features.Auth.Refresh;

public sealed class RefreshTokenCommand : IRequest<AuthResponse>
{
    public string  refresh_token { get; init; } = "";
    public string? Ip            { get; init; }
}

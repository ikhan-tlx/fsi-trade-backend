using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.Sessions;

public sealed class RevokeSessionCommand : IRequest<Unit>
{
    public string CallerUserId { get; init; } = "";
    public string DeviceId     { get; init; } = "";
    public string? Ip          { get; init; }
}

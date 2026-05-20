using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.Sessions;

public sealed class RevokeOtherSessionsCommand : IRequest<Unit>
{
    public string CallerUserId    { get; init; } = "";
    public string CurrentDeviceId { get; init; } = "";
    public string? Ip             { get; init; }
}

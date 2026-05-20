using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Auth.Sessions;

public sealed class ListSessionsQuery : IRequest<IReadOnlyList<SessionDto>>
{
    public string UserId          { get; init; } = "";
    public string? CurrentDeviceId { get; init; }
}

public class SessionDto
{
    public string   DeviceId      { get; set; } = "";
    public string?  Label         { get; set; }
    public string?  UserAgent     { get; set; }
    public DateTime FirstSeenAt   { get; set; }
    public DateTime LastSeenAt    { get; set; }
    public string?  LastSeenIp    { get; set; }
    public bool     IsTrusted     { get; set; }
    public bool     IsCurrent     { get; set; }
}

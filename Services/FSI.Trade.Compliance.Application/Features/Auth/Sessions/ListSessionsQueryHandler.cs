using MediatR;
using FSI.Trade.Compliance.Application.Contracts.Identity;

namespace FSI.Trade.Compliance.Application.Features.Auth.Sessions;

public class ListSessionsQueryHandler : IRequestHandler<ListSessionsQuery, IReadOnlyList<SessionDto>>
{
    private readonly IDeviceService _devices;
    public ListSessionsQueryHandler(IDeviceService devices) => _devices = devices;

    public async Task<IReadOnlyList<SessionDto>> Handle(ListSessionsQuery req, CancellationToken ct)
    {
        var devices = await _devices.ListForUserAsync(req.UserId, ct);
        return devices.Select(d => new SessionDto
        {
            DeviceId    = d.DeviceId,
            Label       = d.Label,
            UserAgent   = d.UserAgent,
            FirstSeenAt = d.FirstSeenAt,
            LastSeenAt  = d.LastSeenAt,
            LastSeenIp  = d.LastSeenIp,
            IsTrusted   = d.IsTrusted,
            IsCurrent   = string.Equals(d.DeviceId, req.CurrentDeviceId, StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }
}

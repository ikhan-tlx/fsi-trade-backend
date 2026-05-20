using MediatR;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Features.Auth.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IRefreshTokenStore    _refresh;
    private readonly ICurrentDeviceService _currentDevice;
    private readonly ILoginAuditService    _audit;

    public LogoutCommandHandler(
        IRefreshTokenStore    refresh,
        ICurrentDeviceService currentDevice,
        ILoginAuditService    audit)
    {
        _refresh       = refresh;
        _currentDevice = currentDevice;
        _audit         = audit;
    }

    public async Task<Unit> Handle(LogoutCommand req, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.refresh_token))
            await _refresh.RevokeAsync(req.refresh_token, "Logout", req.Ip, ct);
        else
            await _refresh.RevokeAllForUserAsync(req.UserId, "Logout", req.Ip, ct);

        await _audit.LogAsync(new LoginAuditEntry
        {
            UserId    = req.UserId,
            DeviceId  = _currentDevice.DeviceId,
            IpAddress = req.Ip,
            UserAgent = _currentDevice.UserAgent,
            Action    = LoginAuditAction.Logout,
            Result    = LoginAuditResult.Success
        }, ct);

        return Unit.Value;
    }
}

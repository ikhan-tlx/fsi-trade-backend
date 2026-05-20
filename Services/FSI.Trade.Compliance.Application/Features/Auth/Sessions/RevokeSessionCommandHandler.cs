using MediatR;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Features.Auth.Sessions;

public class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand, Unit>
{
    private readonly IDeviceService     _devices;
    private readonly ILoginAuditService _audit;

    public RevokeSessionCommandHandler(IDeviceService devices, ILoginAuditService audit)
    {
        _devices = devices;
        _audit   = audit;
    }

    public async Task<Unit> Handle(RevokeSessionCommand req, CancellationToken ct)
    {
        var device = await _devices.FindActiveAsync(req.DeviceId, ct)
                     ?? throw new AuthenticationException("device_not_found", "Device not found or already revoked.");

        // Caller can only revoke their own devices in slice 1. Admin-driven
        // cross-user revocation belongs in slice 3 (AdminController).
        if (!string.Equals(device.UserId, req.CallerUserId, StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException("forbidden", "You can only revoke your own devices.");

        await _devices.RevokeAsync(req.DeviceId, "UserRevoked", req.Ip, ct);

        await _audit.LogAsync(new LoginAuditEntry
        {
            UserId   = req.CallerUserId,
            DeviceId = req.DeviceId,
            IpAddress= req.Ip,
            Action   = LoginAuditAction.DeviceRevoked,
            Result   = LoginAuditResult.Success,
            Detail   = "User-initiated session revocation."
        }, ct);

        return Unit.Value;
    }
}

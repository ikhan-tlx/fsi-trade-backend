using MediatR;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Features.Auth.Sessions;

public class RevokeOtherSessionsCommandHandler : IRequestHandler<RevokeOtherSessionsCommand, Unit>
{
    private readonly IDeviceService     _devices;
    private readonly ILoginAuditService _audit;

    public RevokeOtherSessionsCommandHandler(IDeviceService devices, ILoginAuditService audit)
    {
        _devices = devices;
        _audit   = audit;
    }

    public async Task<Unit> Handle(RevokeOtherSessionsCommand req, CancellationToken ct)
    {
        await _devices.RevokeAllExceptAsync(req.CallerUserId, req.CurrentDeviceId, "UserRevokedOthers", req.Ip, ct);

        await _audit.LogAsync(new LoginAuditEntry
        {
            UserId   = req.CallerUserId,
            DeviceId = req.CurrentDeviceId,
            IpAddress= req.Ip,
            Action   = LoginAuditAction.DeviceRevoked,
            Result   = LoginAuditResult.Success,
            Detail   = "User logged out of all other devices."
        }, ct);

        return Unit.Value;
    }
}

using MediatR;
using Microsoft.Extensions.Options;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Features.Auth.ChangePassword;

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IUserAuthenticationService    _userAuth;
    private readonly IPasswordChangeAuditService   _audit;
    private readonly IRefreshTokenStore            _refresh;
    private readonly ICurrentDeviceService         _currentDevice;
    private readonly ILoginAuditService            _loginAudit;
    private readonly PasswordPolicyOptions         _policy;

    public ChangePasswordCommandHandler(
        IUserAuthenticationService     userAuth,
        IPasswordChangeAuditService    audit,
        IRefreshTokenStore             refresh,
        ICurrentDeviceService          currentDevice,
        ILoginAuditService             loginAudit,
        IOptions<PasswordPolicyOptions> policy)
    {
        _userAuth      = userAuth;
        _audit         = audit;
        _refresh       = refresh;
        _currentDevice = currentDevice;
        _loginAudit    = loginAudit;
        _policy        = policy.Value;
    }

    public async Task<Unit> Handle(ChangePasswordCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.UserId, ct)
                   ?? throw new AuthenticationException("user_not_found");

        var (ok, errors) = await _userAuth.ChangePasswordAsync(user, req.OldPassword, req.NewPassword, ct);
        if (!ok)
        {
            await _loginAudit.LogAsync(new LoginAuditEntry
            {
                UserId    = req.UserId,
                DeviceId  = _currentDevice.DeviceId,
                IpAddress = req.Ip,
                UserAgent = _currentDevice.UserAgent,
                Action    = LoginAuditAction.PasswordChanged,
                Result    = LoginAuditResult.BadCredentials,
                Detail    = string.Join("; ", errors)
            }, ct);
            throw new AuthenticationException("change_password_failed", string.Join("; ", errors));
        }

        user.FirstPasswordChange = false;
        user.PasswordExpiryDate  = DateTime.UtcNow.AddDays(_policy.ExpiryDays);
        await _userAuth.UpdateUserAsync(user, ct);

        await _audit.LogAsync(user.Id, user.PasswordHash, user.Id, ct);
        await _refresh.RevokeAllForUserAsync(user.Id, "PasswordChanged", req.Ip, ct);

        await _loginAudit.LogAsync(new LoginAuditEntry
        {
            UserId    = req.UserId,
            DeviceId  = _currentDevice.DeviceId,
            IpAddress = req.Ip,
            UserAgent = _currentDevice.UserAgent,
            Action    = LoginAuditAction.PasswordChanged,
            Result    = LoginAuditResult.Success
        }, ct);

        return Unit.Value;
    }
}

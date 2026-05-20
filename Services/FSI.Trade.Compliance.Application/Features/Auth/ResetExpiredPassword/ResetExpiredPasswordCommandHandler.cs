using MediatR;
using Microsoft.Extensions.Options;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Features.Auth.ResetExpiredPassword;

public class ResetExpiredPasswordCommandHandler : IRequestHandler<ResetExpiredPasswordCommand, Unit>
{
    private readonly IUserAuthenticationService     _userAuth;
    private readonly IPasswordChangeAuditService    _audit;
    private readonly IRefreshTokenStore             _refresh;
    private readonly ICurrentDeviceService          _currentDevice;
    private readonly ILoginAuditService             _loginAudit;
    private readonly PasswordPolicyOptions          _policy;

    public ResetExpiredPasswordCommandHandler(
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

    public async Task<Unit> Handle(ResetExpiredPasswordCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByUsernameAsync(req.Username, ct);
        if (user is null || !await _userAuth.CheckPasswordAsync(user, req.CurrentPassword, ct))
        {
            await _loginAudit.LogAsync(new LoginAuditEntry
            {
                UserId          = user?.Id,
                UsernameAttempt = req.Username,
                DeviceId        = _currentDevice.DeviceId,
                IpAddress       = req.Ip,
                UserAgent       = _currentDevice.UserAgent,
                Action          = LoginAuditAction.PasswordResetExpired,
                Result          = LoginAuditResult.BadCredentials
            }, ct);
            throw new AuthenticationException("invalid_grant", "Username, password, or eligibility is invalid.");
        }

        var firstChangeRequired = _policy.EnforceFirstPasswordChange && user.FirstPasswordChange;
        var passwordExpired     = _policy.EnforceExpiry
                                  && user.PasswordExpiryDate.HasValue
                                  && user.PasswordExpiryDate.Value < DateTime.UtcNow;

        if (!firstChangeRequired && !passwordExpired)
        {
            await _loginAudit.LogAsync(new LoginAuditEntry
            {
                UserId    = user.Id,
                DeviceId  = _currentDevice.DeviceId,
                IpAddress = req.Ip,
                UserAgent = _currentDevice.UserAgent,
                Action    = LoginAuditAction.PasswordResetExpired,
                Result    = LoginAuditResult.Forbidden,
                Detail    = "User is not in a reset-eligible state."
            }, ct);
            throw new AuthenticationException(
                "no_reset_required",
                "Account does not require a password reset. Use the authenticated ChangePassword endpoint instead.");
        }

        var (ok, errors) = await _userAuth.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword, ct);
        if (!ok)
            throw new AuthenticationException("change_password_failed", string.Join("; ", errors));

        user.FirstPasswordChange = false;
        user.PasswordExpiryDate  = DateTime.UtcNow.AddDays(_policy.ExpiryDays);
        await _userAuth.UpdateUserAsync(user, ct);

        await _audit.LogAsync(user.Id, user.PasswordHash, user.Id, ct);
        await _refresh.RevokeAllForUserAsync(user.Id, "PasswordResetExpired", req.Ip, ct);

        await _loginAudit.LogAsync(new LoginAuditEntry
        {
            UserId    = user.Id,
            DeviceId  = _currentDevice.DeviceId,
            IpAddress = req.Ip,
            UserAgent = _currentDevice.UserAgent,
            Action    = LoginAuditAction.PasswordResetExpired,
            Result    = LoginAuditResult.Success
        }, ct);

        return Unit.Value;
    }
}

using MediatR;
using Microsoft.Extensions.Options;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Identity;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Features.Auth.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponse>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly IRoleQueryService          _roles;
    private readonly IJwtTokenService           _jwt;
    private readonly IRefreshTokenStore         _refresh;
    private readonly ITwoFactorVerifier         _otp;
    private readonly IDeviceService             _devices;
    private readonly ICurrentDeviceService      _currentDevice;
    private readonly ILoginAuditService         _audit;
    private readonly PasswordPolicyOptions      _policy;
    private readonly TwoFactorOptions           _twoFactor;
    private readonly AuthOptions                _authOpt;

    public LoginCommandHandler(
        IUserAuthenticationService     userAuth,
        IRoleQueryService              roles,
        IJwtTokenService               jwt,
        IRefreshTokenStore             refresh,
        ITwoFactorVerifier             otp,
        IDeviceService                 devices,
        ICurrentDeviceService          currentDevice,
        ILoginAuditService             audit,
        IOptions<PasswordPolicyOptions> policy,
        IOptions<TwoFactorOptions>      twoFactor,
        IOptions<AuthOptions>           authOpt)
    {
        _userAuth      = userAuth;
        _roles         = roles;
        _jwt           = jwt;
        _refresh       = refresh;
        _otp           = otp;
        _devices       = devices;
        _currentDevice = currentDevice;
        _audit         = audit;
        _policy        = policy.Value;
        _twoFactor     = twoFactor.Value;
        _authOpt       = authOpt.Value;
    }

    public async Task<AuthResponse> Handle(LoginCommand req, CancellationToken ct)
    {
        if (req.isEncrypted)
        {
            await AuditFail(null, req.username, LoginAuditResult.Error, "encrypted_credentials_unsupported", req, ct);
            throw new AuthenticationException(
                "encrypted_credentials_unsupported",
                "Encrypted credentials (isEncrypted=true) are not supported in this release.");
        }

        // 1 — find user
        var user = await _userAuth.FindByUsernameAsync(req.username, ct);
        if (user is null)
        {
            await AuditFail(null, req.username, LoginAuditResult.BadCredentials, "user_not_found", req, ct);
            throw new AuthenticationException("invalid_grant", "Username or password is incorrect.");
        }

        // 2 — verify password (Identity auto-upgrades V2 hashes silently)
        if (!await _userAuth.CheckPasswordAsync(user, req.password, ct))
        {
            await _userAuth.RecordFailedAccessAsync(user, ct);
            await AuditFail(user.Id, req.username, LoginAuditResult.BadCredentials, "wrong_password", req, ct);
            throw new AuthenticationException("invalid_grant", "Username or password is incorrect.");
        }
        await _userAuth.ResetFailedAccessAsync(user, ct);

        // 3 — always-on gates
        try { AuthGuards.EnsureUserActive(user); }
        catch (AuthenticationException) { await AuditFail(user.Id, req.username, LoginAuditResult.AccountInactive, null, req, ct); throw; }

        if (await _userAuth.IsLockedOutAsync(user, ct))
        {
            await AuditFail(user.Id, req.username, LoginAuditResult.AccountLocked, null, req, ct);
            throw new AuthenticationException("account_locked", "Account is temporarily locked. Try again later.");
        }

        // 4 — config-gated policy gates
        if (_policy.EnforceFirstPasswordChange)
        {
            try { AuthGuards.EnsureNotFirstPasswordChange(user); }
            catch (AuthenticationException) { await AuditFail(user.Id, req.username, LoginAuditResult.FirstChange, null, req, ct); throw; }
        }
        if (_policy.EnforceDormancy)
        {
            try { AuthGuards.EnsureNotDormant(user, _policy.DormancyDays); }
            catch (AuthenticationException) { await AuditFail(user.Id, req.username, LoginAuditResult.Dormant, null, req, ct); throw; }
        }

        var roles = await _roles.GetRoleNamesAsync(user.Id, ct);

        if (_policy.EnforceExpiry)
        {
            try { AuthGuards.EnsurePasswordNotExpired(user, roles, _policy.DisableExpiryForRoles); }
            catch (AuthenticationException) { await AuditFail(user.Id, req.username, LoginAuditResult.PasswordExpired, null, req, ct); throw; }
        }

        if (_twoFactor.Enabled)
        {
            try { AuthGuards.VerifyTwoFactor(user, req.otp, _otp); }
            catch (AuthenticationException ex)
            {
                var result = ex.Code == "otp_required" ? LoginAuditResult.OtpRequired : LoginAuditResult.OtpInvalid;
                await AuditFail(user.Id, req.username, result, ex.Code, req, ct);
                throw;
            }
        }

        // 5 — device binding. If the FE sent X-Device-Id and it's a known
        // device for this user, reuse it. Otherwise register a fresh one.
        var presentedId = _currentDevice.DeviceId;
        UserDevice device;
        if (!string.IsNullOrWhiteSpace(presentedId))
        {
            var existing = await _devices.FindActiveAsync(presentedId, ct);
            if (existing is not null && string.Equals(existing.UserId, user.Id, StringComparison.OrdinalIgnoreCase))
            {
                await _devices.TouchAsync(existing.DeviceId, req.Ip, ct);
                device = existing;
            }
            else
            {
                device = await _devices.RegisterAsync(user.Id, _currentDevice.UserAgent, req.Ip, label: null, ct);
            }
        }
        else
        {
            device = await _devices.RegisterAsync(user.Id, _currentDevice.UserAgent, req.Ip, label: null, ct);
        }

        // 6 — record successful login
        var isFirst = user.LastLoginDate is null;
        user.LastLoginDate = DateTime.UtcNow;
        await _userAuth.UpdateUserAsync(user, ct);

        // 7 — issue tokens (refresh token bound to the device)
        var (jwt, expiresAt) = _jwt.IssueAccessToken(user, roles);
        var refresh           = await _refresh.IssueAsync(user.Id, device.DeviceId, expiresAt, req.Ip, ct);

        // 7a — concurrent-login policy. Models legacy RESTRICT_CONCURRENT_LOGIN:
        // when enabled, this login revokes every OTHER active device for the user
        // (and cascades to their refresh tokens), so only the just-logged-in
        // session survives. The current device is preserved.
        if (_authOpt.RestrictConcurrentLogin)
        {
            await _devices.RevokeAllExceptAsync(user.Id, device.DeviceId, "ConcurrentLoginPolicy", req.Ip, ct);
            await _audit.LogAsync(new LoginAuditEntry
            {
                UserId    = user.Id,
                DeviceId  = device.DeviceId,
                IpAddress = req.Ip,
                UserAgent = _currentDevice.UserAgent,
                Action    = LoginAuditAction.DeviceRevoked,
                Result    = LoginAuditResult.Success,
                Detail    = "Concurrent-login policy: other devices revoked on new sign-in."
            }, ct);
        }

        await _audit.LogAsync(new LoginAuditEntry
        {
            UserId           = user.Id,
            UsernameAttempt  = req.username,
            DeviceId         = device.DeviceId,
            IpAddress        = req.Ip,
            UserAgent        = _currentDevice.UserAgent,
            Action           = LoginAuditAction.Login,
            Result           = LoginAuditResult.Success
        }, ct);

        return new AuthResponse
        {
            Success      = 1,
            UserId       = user.Id,
            UserName     = user.UserName,
            IsFirstLogin = isFirst,
            AccessToken  = jwt,
            RefreshToken = refresh,
            ExpiresIn    = (int)(expiresAt - DateTime.UtcNow).TotalSeconds,
            DeviceId     = device.DeviceId
        };
    }

    private Task AuditFail(string? userId, string usernameAttempt, string result, string? detail, LoginCommand req, CancellationToken ct)
        => _audit.LogAsync(new LoginAuditEntry
        {
            UserId           = userId,
            UsernameAttempt  = usernameAttempt,
            DeviceId         = _currentDevice.DeviceId,
            IpAddress        = req.Ip,
            UserAgent        = _currentDevice.UserAgent,
            Action           = LoginAuditAction.LoginFailed,
            Result           = result,
            Detail           = detail
        }, ct);
}

using MediatR;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Identity;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Features.Auth.Refresh;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly IRoleQueryService          _roles;
    private readonly IJwtTokenService           _jwt;
    private readonly IRefreshTokenStore         _refresh;
    private readonly ICurrentDeviceService      _currentDevice;
    private readonly ILoginAuditService         _audit;

    public RefreshTokenCommandHandler(
        IUserAuthenticationService userAuth,
        IRoleQueryService          roles,
        IJwtTokenService           jwt,
        IRefreshTokenStore         refresh,
        ICurrentDeviceService      currentDevice,
        ILoginAuditService         audit)
    {
        _userAuth      = userAuth;
        _roles         = roles;
        _jwt           = jwt;
        _refresh       = refresh;
        _currentDevice = currentDevice;
        _audit         = audit;
    }

    public async Task<AuthResponse> Handle(RefreshTokenCommand req, CancellationToken ct)
    {
        var token = await _refresh.FindAsync(req.refresh_token, ct)
                    ?? throw new AuthenticationException("invalid_refresh_token", "Refresh token not found.");

        // Reuse-of-revoked → likely token theft; revoke entire chain and reject.
        if (token.RevokedAt is not null)
        {
            await _refresh.RevokeChainAsync(token, req.Ip, ct);
            await _audit.LogAsync(new LoginAuditEntry
            {
                UserId    = token.UserId,
                DeviceId  = token.DeviceId,
                IpAddress = req.Ip,
                UserAgent = _currentDevice.UserAgent,
                Action    = LoginAuditAction.RefreshChainRevoked,
                Result    = LoginAuditResult.Error,
                Detail    = "Reuse of a revoked refresh token detected. Chain revoked."
            }, ct);
            throw new AuthenticationException("invalid_refresh_token", "Refresh token has been revoked.");
        }

        if (token.ExpiresAt <= DateTime.UtcNow)
            throw new AuthenticationException("expired_refresh_token", "Refresh token has expired.");

        // Device-binding check: if the FE sent X-Device-Id and it disagrees
        // with the token's bound device, reject.
        var presentedId = _currentDevice.DeviceId;
        if (!string.IsNullOrWhiteSpace(presentedId)
            && !string.IsNullOrWhiteSpace(token.DeviceId)
            && !string.Equals(presentedId, token.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            await _refresh.RevokeChainAsync(token, req.Ip, ct);
            throw new AuthenticationException("device_user_mismatch", "Refresh token is not bound to this device.");
        }

        var user = await _userAuth.FindByIdAsync(token.UserId, ct)
                   ?? throw new AuthenticationException("invalid_refresh_token", "User no longer exists.");

        AuthGuards.EnsureUserActive(user);
        if (await _userAuth.IsLockedOutAsync(user, ct))
            throw new AuthenticationException("account_locked", "Account is locked.");

        var roles               = await _roles.GetRoleNamesAsync(user.Id, ct);
        var (jwtStr, expiresAt) = _jwt.IssueAccessToken(user, roles);
        var newRefresh          = await _refresh.RotateAsync(token, expiresAt, req.Ip, ct);

        return new AuthResponse
        {
            Success      = 1,
            UserId       = user.Id,
            UserName     = user.UserName,
            AccessToken  = jwtStr,
            RefreshToken = newRefresh,
            ExpiresIn    = (int)(expiresAt - DateTime.UtcNow).TotalSeconds,
            DeviceId     = token.DeviceId   // returned so FE can repair localStorage if it ever loses it
        };
    }
}

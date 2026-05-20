using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Features.Auth.ChangePassword;
using FSI.Trade.Compliance.Application.Features.Auth.Login;
using FSI.Trade.Compliance.Application.Features.Auth.Logout;
using FSI.Trade.Compliance.Application.Features.Auth.Refresh;
using FSI.Trade.Compliance.Application.Features.Auth.ResetExpiredPassword;
using FSI.Trade.Compliance.Application.Features.Auth.Sessions;
using FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Confirm;
using FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Disable;
using FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Enable;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Token / credential lifecycle + per-device session management. Slice 1.5 surface:
///
///   POST   /api/v1/Auth/Login                  (form, anonymous)
///   POST   /api/v1/Auth/Refresh                (json, anonymous)
///   POST   /api/v1/Auth/Logout                 (Bearer)
///   POST   /api/v1/Auth/ChangePassword         (Bearer)
///   POST   /api/v1/Auth/ResetExpiredPassword   (json, anonymous)
///   POST   /api/v1/Auth/EnableTwoFactor        (Bearer)
///   POST   /api/v1/Auth/ConfirmTwoFactor       (Bearer)
///   POST   /api/v1/Auth/DisableTwoFactor       (Bearer)
///   GET    /api/v1/Auth/Sessions               (Bearer) — list caller's devices
///   DELETE /api/v1/Auth/Sessions/{deviceId}    (Bearer) — revoke one device
///   DELETE /api/v1/Auth/Sessions/Other         (Bearer) — revoke every device except current
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator             _mediator;
    private readonly ICurrentUserService   _current;
    private readonly ICurrentDeviceService _currentDevice;

    public AuthController(IMediator mediator, ICurrentUserService current, ICurrentDeviceService currentDevice)
    {
        _mediator      = mediator;
        _current       = current;
        _currentDevice = currentDevice;
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromForm] LoginCommand form, CancellationToken ct)
    {
        var ip  = HttpContext.Connection.RemoteIpAddress?.ToString();
        var cmd = new LoginCommand
        {
            username    = form.username,
            password    = form.password,
            isEncrypted = form.isEncrypted,
            otp         = form.otp,
            Ip          = ip
        };

        var result = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<AuthResponse>.Ok(result));
    }

    [HttpPost("Refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshBody body, CancellationToken ct)
    {
        var ip     = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _mediator.Send(new RefreshTokenCommand
        {
            refresh_token = body.refresh_token,
            Ip            = ip
        }, ct);

        return Ok(ResponseViewModel<AuthResponse>.Ok(result));
    }

    [HttpPost("Logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutBody? body, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new UnauthorizedAccessException("No user context.");

        await _mediator.Send(new LogoutCommand
        {
            UserId        = userId,
            refresh_token = body?.refresh_token,
            Ip            = _current.IpAddress
        }, ct);

        return Ok(ResponseViewModel<object>.Ok(new { Success = 1 }, "Logged out."));
    }

    [HttpPost("ChangePassword")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordBody body, CancellationToken ct)
    {
        var callerId = _current.UserId
                       ?? throw new UnauthorizedAccessException("No user context.");

        if (!string.Equals(body.UserId, callerId, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, ResponseViewModel<object>.Fail(403, "forbidden",
                "You can only change your own password in this release."));

        await _mediator.Send(new ChangePasswordCommand
        {
            UserId      = body.UserId,
            OldPassword = body.OldPassword,
            NewPassword = body.NewPassword,
            Ip          = _current.IpAddress
        }, ct);

        return Ok(ResponseViewModel<object>.Ok(new { Success = 1 }, "Password changed."));
    }

    [HttpPost("ResetExpiredPassword")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetExpiredPassword([FromBody] ResetExpiredPasswordBody body, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _mediator.Send(new ResetExpiredPasswordCommand
        {
            Username        = body.Username,
            CurrentPassword = body.CurrentPassword,
            NewPassword     = body.NewPassword,
            Ip              = ip
        }, ct);

        return Ok(ResponseViewModel<object>.Ok(new { Success = 1 }, "Password reset. Please sign in."));
    }

    [HttpPost("EnableTwoFactor")]
    [Authorize]
    public async Task<IActionResult> EnableTwoFactor(CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new UnauthorizedAccessException("No user context.");
        var resp = await _mediator.Send(new EnableTwoFactorCommand { UserId = userId }, ct);
        return Ok(ResponseViewModel<EnableTwoFactorResponse>.Ok(resp));
    }

    [HttpPost("ConfirmTwoFactor")]
    [Authorize]
    public async Task<IActionResult> ConfirmTwoFactor([FromBody] ConfirmTwoFactorBody body, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new UnauthorizedAccessException("No user context.");
        await _mediator.Send(new ConfirmTwoFactorCommand { UserId = userId, Otp = body.Otp }, ct);
        return Ok(ResponseViewModel<object>.Ok(new { Success = 1 }, "Two-factor authentication enabled."));
    }

    [HttpPost("DisableTwoFactor")]
    [Authorize]
    public async Task<IActionResult> DisableTwoFactor([FromBody] DisableTwoFactorBody body, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new UnauthorizedAccessException("No user context.");
        await _mediator.Send(new DisableTwoFactorCommand { UserId = userId, CurrentPassword = body.CurrentPassword }, ct);
        return Ok(ResponseViewModel<object>.Ok(new { Success = 1 }, "Two-factor authentication disabled."));
    }

    // ---------------------------- Sessions ----------------------------

    [HttpGet("Sessions")]
    [Authorize]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new UnauthorizedAccessException("No user context.");
        var list = await _mediator.Send(new ListSessionsQuery
        {
            UserId          = userId,
            CurrentDeviceId = _currentDevice.DeviceId
        }, ct);
        return Ok(ResponseViewModel<IReadOnlyList<SessionDto>>.Ok(list));
    }

    [HttpDelete("Sessions/{deviceId}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession([FromRoute] string deviceId, CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new UnauthorizedAccessException("No user context.");
        await _mediator.Send(new RevokeSessionCommand
        {
            CallerUserId = userId,
            DeviceId     = deviceId,
            Ip           = _current.IpAddress
        }, ct);
        return Ok(ResponseViewModel<object>.Ok(new { Success = 1 }, "Session revoked."));
    }

    [HttpDelete("Sessions/Other")]
    [Authorize]
    public async Task<IActionResult> RevokeOtherSessions(CancellationToken ct)
    {
        var userId = _current.UserId
                     ?? throw new UnauthorizedAccessException("No user context.");
        var current = _currentDevice.DeviceId
                      ?? throw new UnauthorizedAccessException("X-Device-Id header missing.");
        await _mediator.Send(new RevokeOtherSessionsCommand
        {
            CallerUserId    = userId,
            CurrentDeviceId = current,
            Ip              = _current.IpAddress
        }, ct);
        return Ok(ResponseViewModel<object>.Ok(new { Success = 1 }, "Other sessions revoked."));
    }

    // ---------------------------- Bodies ------------------------------

    public record RefreshBody(string refresh_token);
    public record LogoutBody(string? refresh_token);
    public record ChangePasswordBody(string UserId, string OldPassword, string NewPassword);
    public record ResetExpiredPasswordBody(string Username, string CurrentPassword, string NewPassword);
    public record ConfirmTwoFactorBody(string Otp);
    public record DisableTwoFactorBody(string CurrentPassword);
}

using System.Security.Claims;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FSI.Trade.Compliance.API.Authentication;

/// <summary>
/// Runs after JwtBearer authentication. For every authenticated request that
/// isn't on the device-check exempt list:
///
///   - Reads <c>X-Device-Id</c> from the request headers.
///   - 400 if missing (when <c>Auth:RequireDeviceIdHeader</c>).
///   - Looks up the device; 401 if unknown / revoked / belongs to a different user.
///   - Bumps <c>Last_Seen_At</c> on a hit.
///
/// Anonymous endpoints (Login, Refresh, ResetExpiredPassword, Health) are
/// short-circuited by the exempt list so they can run before the FE has a
/// device ID at all.
/// </summary>
public class DeviceTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthOptions     _opt;

    public DeviceTrackingMiddleware(RequestDelegate next, IOptions<AuthOptions> opt)
    {
        _next = next;
        _opt  = opt.Value;
    }

    public async Task Invoke(HttpContext ctx, IDeviceService devices)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Exempt paths — pass through with no device check.
        foreach (var exempt in _opt.DeviceCheckExemptPaths)
        {
            if (path.StartsWith(exempt, StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }
        }

        // Anonymous requests skip device validation (let upstream auth deal with the missing JWT first).
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            await _next(ctx);
            return;
        }

        if (!_opt.RequireDeviceIdHeader)
        {
            await _next(ctx);
            return;
        }

        var deviceId = ctx.Request.Headers.TryGetValue(_opt.DeviceIdHeaderName, out var v) ? v.ToString() : null;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await WriteEnvelopeAsync(ctx, 400, "device_id_required",
                $"Header '{_opt.DeviceIdHeaderName}' is required for this endpoint.");
            return;
        }

        var device = await devices.FindActiveAsync(deviceId, ctx.RequestAborted);
        if (device is null)
        {
            await WriteEnvelopeAsync(ctx, 401, "device_unknown",
                "The device is not registered or has been revoked. Sign in again.");
            return;
        }

        var callerUserId = ctx.User.FindFirstValue("userId")
                           ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.Equals(callerUserId, device.UserId, StringComparison.OrdinalIgnoreCase))
        {
            await WriteEnvelopeAsync(ctx, 403, "device_user_mismatch",
                "The device does not belong to the authenticated user.");
            return;
        }

        // Fire-and-forget — don't await TouchAsync so a slow DB doesn't slow the request.
        // CRITICAL: Touch runs in its OWN DI scope, NOT the request's scope. Otherwise
        // it shares the request's scoped DbContext with the downstream handler and
        // produces "A second operation was started on this context instance" the
        // moment any handler does its own DB read. The cost of a fresh scope is a
        // few microseconds; the bug it prevents is intermittent + hard to diagnose.
        var scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>();
        var ip           = ctx.Connection.RemoteIpAddress?.ToString();

        _ = Task.Run(async () =>
        {
            using var scope    = scopeFactory.CreateScope();
            var scopedDevices  = scope.ServiceProvider.GetRequiredService<IDeviceService>();
            try
            {
                await scopedDevices.TouchAsync(deviceId, ip, CancellationToken.None);
            }
            catch
            {
                // Best-effort touch. Real logging lives inside DeviceService.
            }
        });

        await _next(ctx);
    }

    private static async Task WriteEnvelopeAsync(HttpContext ctx, int status, string code, string description)
    {
        ctx.Response.StatusCode  = status;
        ctx.Response.ContentType = "application/json";
        var envelope = new ResponseViewModel<object>
        {
            status = ResponseStatus.Error(status, code, description),
            data   = new { Success = 0, Code = code }
        };
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(envelope,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}

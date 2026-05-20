using FSI.Trade.Compliance.Application.Common.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.API.Authentication;

/// <summary>
/// Whitelists configured paths (Login, Refresh, Health) and otherwise requires a
/// successfully-validated JWT. JwtBearer middleware (registered in Program.cs)
/// has already populated HttpContext.User by the time we get here — we just gate
/// the request based on Identity status and optional concurrent-login enforcement.
/// </summary>
public class CustomAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthOptions     _opt;

    public CustomAuthorizationMiddleware(RequestDelegate next, IOptions<AuthOptions> opt)
    {
        _next = next;
        _opt  = opt.Value;
    }

    public async Task Invoke(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Whitelist — case-insensitive prefix match.
        foreach (var w in _opt.WhitelistedPaths)
        {
            if (path.StartsWith(w, StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }
        }

        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("{\"status\":{\"code\":401,\"message\":\"unauthorized\"}}");
            return;
        }

        // RestrictConcurrentLogin enforcement happens at LOGIN time inside
        // LoginCommandHandler (revokes other devices' refresh tokens). At this
        // middleware layer, an already-revoked device just shows up as
        // "device_unknown" via DeviceTrackingMiddleware — no work needed here.
        await _next(ctx);
    }
}

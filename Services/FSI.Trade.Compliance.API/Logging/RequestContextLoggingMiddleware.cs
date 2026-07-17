using System.Security.Claims;

namespace FSI.Trade.Compliance.API.Logging;

/// <summary>
/// Opens a logging scope for the lifetime of each <c>/api/*</c> request so that
/// EVERY log line emitted downstream (controllers, MediatR handlers, services,
/// the <see cref="FSI.Trade.Compliance.Application.Common.Logging.WorkflowLog"/>
/// step trail) automatically carries the request's correlation identifiers:
///
///   RequestId      — stable per request (ASP.NET <c>TraceIdentifier</c>).
///   CorrelationId  — caller-supplied <c>X-Correlation-ID</c> header, else the
///                    RequestId. Echoed back on the response so a client/gateway
///                    can stitch its call to our logs.
///   UserId         — the authenticated principal (sub / NameIdentifier / name),
///                    when available.
///
/// Serilog promotes scope state to structured properties, so these appear on
/// every line (see the file sink's <c>{Properties:j}</c> in Program.cs) and are
/// fully searchable. Registered AFTER <c>UseAuthentication</c> so the principal
/// is populated; it still wraps the authorization gates and controllers, so
/// their logs (including 401/403 paths) are correlated too.
/// </summary>
public class RequestContextLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestContextLoggingMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, ILoggerFactory loggerFactory)
    {
        var path = (ctx.Request.PathBase + ctx.Request.Path).Value ?? "";
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var requestId = ctx.TraceIdentifier;

        var correlationId = ctx.Request.Headers.TryGetValue("X-Correlation-ID", out var hdr)
                            && !string.IsNullOrWhiteSpace(hdr)
            ? hdr.ToString()
            : requestId;

        // Echo the correlation id back so the caller can tie its request to our logs.
        ctx.Response.Headers["X-Correlation-ID"] = correlationId;

        var state = new Dictionary<string, object>
        {
            ["RequestId"]     = requestId,
            ["CorrelationId"] = correlationId,
        };

        var userId = ResolveUserId(ctx.User);
        if (!string.IsNullOrWhiteSpace(userId))
            state["UserId"] = userId!;

        var logger = loggerFactory.CreateLogger("Application.Request");
        using (logger.BeginScope(state))
        {
            await _next(ctx);
        }
    }

    private static string? ResolveUserId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.Identity?.Name;
    }
}

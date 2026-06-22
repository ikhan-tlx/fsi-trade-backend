using System.Diagnostics;
using System.Text;

namespace FSI.Trade.Compliance.API.Logging;

/// <summary>
/// End-to-end API request/response logger. For every <c>/api/*</c> call it emits
/// ONE structured entry — method, path, query, request body, response status,
/// response body, elapsed ms — into the per-module file
/// (Application.&lt;Module&gt;-*.log) via a logger whose category is
/// <c>Application.{Module}</c> (see <see cref="LogModules"/>). 4xx logs at
/// Warning, 5xx at Error; everything else Information. Unhandled exceptions and
/// their inner details are logged by <c>ExceptionHandlingFilter</c> into the
/// same module file.
///
/// Safeguards: request bodies of sensitive endpoints (login / refresh / password
/// reset) are redacted; bodies are truncated to a cap; non-text responses
/// (file/PDF downloads) are summarised, not dumped.
/// </summary>
public class ApiRequestLoggingMiddleware
{
    private readonly RequestDelegate    _next;
    private readonly ILoggerFactory     _loggerFactory;

    private const int MaxBodyChars = 8 * 1024;   // truncate beyond 8 KB

    // Endpoints whose request body carries credentials — never log the body.
    private static readonly string[] SensitiveBodyPaths =
        { "/auth/login", "/auth/refresh", "/auth/resetexpiredpassword" };

    // File/binary DOWNLOAD endpoints — do NOT buffer the response (avoids holding
    // a whole PDF/Excel/document in memory and any FileResult streaming edge
    // case). The request + final status are still logged; the body is not.
    private static readonly string[] DownloadPaths =
    {
        "/report/generatepdffromhtml", "/report/reportexcel",
        "/document/", "/workflow/designer",
    };

    public ApiRequestLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next          = next;
        _loggerFactory = loggerFactory;
    }

    public async Task Invoke(HttpContext ctx)
    {
        // Include PathBase so the "/api" guard still fires under an IIS application
        // path: Request.Path is "/v1/..." there ("/api" lives in PathBase), so
        // matching on Path alone would skip logging for every request under IIS.
        // PathBase is empty off-IIS, so this is a no-op for Kestrel/dev.
        var path = (ctx.Request.PathBase + ctx.Request.Path).Value ?? "";
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var module = LogModules.FromPath(path);
        var log    = _loggerFactory.CreateLogger(LogModules.Category(module));
        var sw     = Stopwatch.StartNew();

        // Download endpoints: don't buffer the response — pass straight through
        // (no Response.Body swap), log request + status only.
        if (DownloadPaths.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            var reqBodyDl = await CaptureRequestBodyAsync(ctx, path);
            try { await _next(ctx); }
            finally
            {
                sw.Stop();
                var st = ctx.Response.StatusCode;
                log.Log(st >= 500 ? LogLevel.Error : st >= 400 ? LogLevel.Warning : LogLevel.Information,
                    "{Method} {Path}{Query} -> HTTP {Status} in {Elapsed} ms{NL}REQUEST : {Request}{NL}RESPONSE: [download — body not captured]",
                    ctx.Request.Method, path, ctx.Request.QueryString.Value ?? "",
                    st, sw.ElapsedMilliseconds, Environment.NewLine, reqBodyDl, Environment.NewLine);
            }
            return;
        }

        var requestBody = await CaptureRequestBodyAsync(ctx, path);

        // Buffer the response so we can read it, then copy back to the real stream.
        var originalResponseBody = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        try
        {
            await _next(ctx);
        }
        finally
        {
            sw.Stop();
            var responseBody = await ReadResponseBodyAsync(ctx, buffer);
            buffer.Position = 0;
            await buffer.CopyToAsync(originalResponseBody);
            ctx.Response.Body = originalResponseBody;

            var status = ctx.Response.StatusCode;
            var level  = status >= 500 ? LogLevel.Error
                       : status >= 400 ? LogLevel.Warning
                       : LogLevel.Information;

            log.Log(level,
                "{Method} {Path}{Query} -> HTTP {Status} in {Elapsed} ms{NL}REQUEST : {Request}{NL}RESPONSE: {Response}",
                ctx.Request.Method, path, ctx.Request.QueryString.Value ?? "",
                status, sw.ElapsedMilliseconds, Environment.NewLine, requestBody, Environment.NewLine, responseBody);
        }
    }

    private static async Task<string> CaptureRequestBodyAsync(HttpContext ctx, string path)
    {
        if (SensitiveBodyPaths.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return "[REDACTED — sensitive endpoint]";

        if (ctx.Request.ContentLength is null or 0) return "(empty)";

        var ct = ctx.Request.ContentType ?? "";
        if (!IsTextLike(ct)) return $"[non-text request, {ct}, {ctx.Request.ContentLength} bytes]";

        ctx.Request.EnableBuffering();               // allow the action to re-read it
        ctx.Request.Body.Position = 0;
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;               // rewind for model binding
        return Truncate(body);
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext ctx, MemoryStream buffer)
    {
        buffer.Position = 0;
        var ct = ctx.Response.ContentType ?? "";
        if (buffer.Length == 0) return "(empty)";
        if (!IsTextLike(ct)) return $"[non-text response, {ct}, {buffer.Length} bytes]";

        using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return Truncate(body);
    }

    private static bool IsTextLike(string contentType) =>
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("urlencoded", StringComparison.OrdinalIgnoreCase) ||
        contentType.Length == 0;   // many empty-body responses report no content-type

    private static string Truncate(string s) =>
        string.IsNullOrEmpty(s) ? "(empty)"
        : s.Length <= MaxBodyChars ? s
        : s[..MaxBodyChars] + $"…[truncated, {s.Length} chars total]";
}

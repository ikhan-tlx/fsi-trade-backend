using System.Reflection;
using FSI.Trade.Compliance.Application.Contracts.Audit;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace FSI.Trade.Compliance.API.Filters;

/// <summary>
/// Global action filter that records an audit row for any action carrying
/// <see cref="AuditedAttribute"/> — covering READ / LOGIN / EXPORT and capturing the
/// final outcome (SUCCESS / FAILURE), HTTP status and failure reason. Mutations are
/// already audited by the EF interceptor, so only non-mutation/explicit events are
/// annotated. Writes are best-effort and never interrupt the response.
/// </summary>
public sealed class AuditActionFilter : IAsyncActionFilter
{
    private static readonly string[] KeyArgNames = { "id", "code", "email", "username", "userName", "login" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attr = FindAttribute(context);
        if (attr is null) { await next(); return; }

        var key = ResolveKey(context, attr);
        var executed = await next();

        var writer = context.HttpContext.RequestServices.GetService(typeof(IAuditWriter)) as IAuditWriter;
        if (writer is null) return;

        var status = ResolveStatus(executed);
        var success = executed.Exception is null && status < 400;
        var failure = executed.Exception?.Message ?? (success ? null : $"HTTP {status}");

        var who = key is null ? attr.EntityType : $"{attr.EntityType} #{key}";
        var description = success
            ? $"{attr.Action} {who}"
            : $"{attr.Action} {who} — failed";

        await writer.WriteAsync(new AuditEvent
        {
            Action        = attr.Action,
            EntityType    = attr.EntityType,
            EntityKey     = key,
            Outcome       = success ? "SUCCESS" : "FAILURE",
            StatusCode    = status,
            FailureReason = failure,
            Description   = description,
        });
    }

    private static AuditedAttribute? FindAttribute(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor cad) return null;
        return cad.MethodInfo.GetCustomAttribute<AuditedAttribute>()
               ?? cad.ControllerTypeInfo.GetCustomAttribute<AuditedAttribute>();
    }

    private static string? ResolveKey(ActionExecutingContext context, AuditedAttribute attr)
    {
        // 1) explicit route key, then common route keys
        if (attr.RouteKey is not null && context.RouteData.Values.TryGetValue(attr.RouteKey, out var rv) && rv is not null)
            return rv.ToString();
        foreach (var rk in new[] { "id", "code" })
            if (context.RouteData.Values.TryGetValue(rk, out var v) && v is not null)
                return v.ToString();

        // 2) a username-like action argument (e.g., login command)
        foreach (var (name, value) in context.ActionArguments)
        {
            if (value is null) continue;
            if (KeyArgNames.Contains(name, StringComparer.OrdinalIgnoreCase) && value is string s && s.Length > 0)
                return s;
            // scan one level into a command/DTO object
            foreach (var prop in value.GetType().GetProperties())
            {
                if (!KeyArgNames.Contains(prop.Name, StringComparer.OrdinalIgnoreCase)) continue;
                var pv = prop.GetValue(value)?.ToString();
                if (!string.IsNullOrWhiteSpace(pv)) return pv;
            }
        }
        return null;
    }

    private static int ResolveStatus(ActionExecutedContext executed)
    {
        if (executed.Exception is not null) return 500;
        if (executed.Result is IStatusCodeActionResult sc && sc.StatusCode is int code) return code;
        return executed.HttpContext.Response.StatusCode;
    }
}

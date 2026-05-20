using System.Security.Claims;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.API.Filters;

/// <summary>
/// Action filter — gates a controller / action behind a single privilege code.
/// Reads role-name claims from the JWT, resolves the privilege set for those
/// roles via <see cref="IPrivilegeService"/>, and rejects the request with 403
/// if the required code isn't in the set.
///
/// Per-request caching: the resolved privilege set is stored in
/// <c>HttpContext.Items["__privset"]</c> on first use, so an action guarded by
/// multiple privileges (or a request that hits multiple guarded
/// sub-operations) does ONE SQL query then O(1) hash-set checks.
///
/// Bootstrap escape hatch: if the caller is in any role listed in
/// <c>Auth:BootstrapAdminRoles</c>, the privilege check is short-circuited.
/// This is the deliberate "the matrix is empty on day one" workaround —
/// intended to be removed once admins have wired role → privilege grants.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPrivilegeAttribute : Attribute, IAsyncAuthorizationFilter
{
    private const string CacheKey = "__privset";

    public string Code { get; }

    public RequiresPrivilegeAttribute(string code) => Code = code;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var http = ctx.HttpContext;

        if (http.User?.Identity?.IsAuthenticated != true)
        {
            ctx.Result = WriteEnvelope(401, "unauthenticated", "Authentication required.");
            return;
        }

        var roleNames = http.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        // Bootstrap escape hatch — see Auth:BootstrapAdminRoles in appsettings.
        var auth = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (auth.BootstrapAdminRoles is { Count: > 0 } bootstrap)
        {
            foreach (var br in bootstrap)
                foreach (var r in roleNames)
                    if (string.Equals(r, br, StringComparison.OrdinalIgnoreCase))
                        return;  // caller is a bootstrap admin — let through
        }

        // Pull (or build once) the per-request privilege set.
        if (http.Items[CacheKey] is not HashSet<string> privSet)
        {
            var svc = http.RequestServices.GetRequiredService<IPrivilegeService>();
            var raw = await svc.GetPrivilegesForRolesAsync(roleNames, http.RequestAborted);
            privSet = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);
            http.Items[CacheKey] = privSet;
        }

        if (!privSet.Contains(Code))
        {
            ctx.Result = WriteEnvelope(
                403,
                "forbidden_privilege",
                $"This action requires the '{Code}' privilege.",
                required: Code);
        }
    }

    private static IActionResult WriteEnvelope(int status, string code, string description, string? required = null)
    {
        var envelope = new ResponseViewModel<object>
        {
            status = ResponseStatus.Error(status, code, description),
            data   = required is null
                ? new { Success = 0, Code = code }
                : (object)new { Success = 0, Code = code, Required = required }
        };
        return new ObjectResult(envelope) { StatusCode = status };
    }
}

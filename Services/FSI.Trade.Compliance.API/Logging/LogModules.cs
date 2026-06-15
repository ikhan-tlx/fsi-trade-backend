using System.Text.RegularExpressions;

namespace FSI.Trade.Compliance.API.Logging;

/// <summary>
/// Maps an API request route to a functional "module" used to route its
/// request/response log entry to a dedicated file (Application.&lt;Module&gt;-*.log).
/// The module is derived from the controller segment of the route
/// (/api/v{n}/{Controller}/…). Anything not explicitly mapped falls back to
/// "Trade" (the bulk of the trade-compliance surface). Adjust the map to add a
/// module file.
/// </summary>
public static class LogModules
{
    // NOTE: route uses the [controller] token, so segments are the SINGULAR
    // controller name minus "Controller" — UserController -> "User",
    // RoleController -> "Role" (not "Users"/"Roles").
    // UserManagement = the user & role management (identity/admin) surface:
    // User, Role, Privileges, UserTypes, and Auth (login/identity). Everything
    // else (Transaction, Kyc, Flags, Workflow, Reports, Product, Documents,
    // Branch, Company, Location, Lookups, Dashboard, Presence, …) → Trade.
    private static readonly Dictionary<string, string> ControllerToModule =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["User"]       = "UserManagement",
            ["Role"]       = "UserManagement",
            ["Privileges"] = "UserManagement",
            ["UserTypes"]  = "UserManagement",
            ["Auth"]       = "UserManagement",
        };

    public const string DefaultModule = "Trade";

    /// <summary>Resolve the module name for a request path.</summary>
    public static string FromPath(string? path)
    {
        var controller = ControllerFromPath(path);
        return ControllerToModule.TryGetValue(controller, out var m) ? m : DefaultModule;
    }

    /// <summary>Serilog SourceContext used for a module's request/response logger.</summary>
    public static string Category(string module) => $"Application.{module}";

    private static string ControllerFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // …/api/v{n}/{controller}/…  — return the segment right after v{n}.
        for (var i = 0; i < segs.Length; i++)
        {
            if (Regex.IsMatch(segs[i], "^v\\d", RegexOptions.IgnoreCase) && i + 1 < segs.Length)
                return segs[i + 1];
        }
        // Fallback: /api/{controller}/… or first non-"api" segment.
        return segs.Length >= 2 && segs[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            ? segs[1] : (segs.Length > 0 ? segs[0] : "");
    }
}

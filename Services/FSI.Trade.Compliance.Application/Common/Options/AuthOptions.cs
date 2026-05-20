namespace FSI.Trade.Compliance.Application.Common.Options;

public class AuthOptions
{
    public const string SectionName = "Auth";

    public List<string> WhitelistedPaths        { get; set; } = new();

    /// <summary>
    /// When true, a successful login revokes every OTHER active device for the user.
    /// Models the legacy RESTRICT_CONCURRENT_LOGIN behaviour. Defaults to false
    /// (multi-device permitted).
    /// </summary>
    public bool         RestrictConcurrentLogin { get; set; }

    /// <summary>Header name carrying the server-issued device ID.</summary>
    public string DeviceIdHeaderName { get; set; } = "X-Device-Id";

    /// <summary>
    /// When true, every authenticated request MUST carry the device-ID header.
    /// Login is always allowed without it (server issues one on first use).
    /// </summary>
    public bool RequireDeviceIdHeader { get; set; } = true;

    /// <summary>
    /// Paths exempt from the device-ID header requirement (Login, Refresh,
    /// Health, ResetExpiredPassword — these run before the FE has a device ID).
    /// </summary>
    public List<string> DeviceCheckExemptPaths { get; set; } = new();

    /// <summary>
    /// Bootstrap escape hatch for <c>[RequiresPrivilege]</c>. Until an admin
    /// has wired role → privilege grants in <c>TmX_Role_Privilege_Mapping</c>
    /// via the new role-edit UI, callers in any of these role names skip the
    /// privilege check (still must be authenticated). Designed to be removed
    /// once the matrix is populated — see BACKLOG.md.
    /// Default: ["IT Admin"] — matches the legacy super-admin role.
    /// </summary>
    public List<string> BootstrapAdminRoles { get; set; } = new();
}

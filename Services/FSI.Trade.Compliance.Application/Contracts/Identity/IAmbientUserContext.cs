namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// Lets in-process (non-HTTP) code — e.g. the periodic-review background flow —
/// run as a specific principal so <see cref="ICurrentUserService"/> attributes
/// its writes (Created_By, workflow actor, audit) to that identity. Backed by an
/// AsyncLocal so the impersonation flows across awaits within the scope and is
/// cleared on dispose. HTTP requests never set this (HttpContext wins).
/// </summary>
public interface IAmbientUserContext
{
    AmbientUser? Current { get; }

    /// <summary>Impersonate for the lifetime of the returned scope; dispose restores the previous value.</summary>
    IDisposable Impersonate(string userId, string? userName, int tenantId);
}

public sealed record AmbientUser(string UserId, string? UserName, int TenantId);

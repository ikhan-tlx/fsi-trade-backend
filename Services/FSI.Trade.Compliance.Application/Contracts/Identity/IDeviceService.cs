using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// Manages the per-user device registry. Server-issued IDs only; this port
/// never accepts a client-generated value as authoritative.
/// </summary>
public interface IDeviceService
{
    /// <summary>Registers a new device for the user and returns it.</summary>
    Task<UserDevice> RegisterAsync(string userId, string? userAgent, string? ip, string? label, CancellationToken ct = default);

    /// <summary>Looks up a device by ID. Returns null if not found or revoked.</summary>
    Task<UserDevice?> FindActiveAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Bumps Last_Seen_At + Last_Seen_Ip on an existing active device.</summary>
    Task TouchAsync(string deviceId, string? ip, CancellationToken ct = default);

    /// <summary>Lists every active device for a user.</summary>
    Task<IReadOnlyList<UserDevice>> ListForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Revokes a single device. Cascades to revoke any active refresh tokens bound to it.</summary>
    Task RevokeAsync(string deviceId, string reason, string? actingIp, CancellationToken ct = default);

    /// <summary>Revokes every device for a user EXCEPT the one supplied. The "log me out everywhere else" action.</summary>
    Task RevokeAllExceptAsync(string userId, string keepDeviceId, string reason, string? actingIp, CancellationToken ct = default);
}

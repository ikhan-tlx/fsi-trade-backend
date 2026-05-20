using System.Security.Cryptography;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;

public class DeviceService : IDeviceService
{
    private readonly IApplicationDbContext _db;
    public DeviceService(IApplicationDbContext db) => _db = db;

    public async Task<UserDevice> RegisterAsync(string userId, string? userAgent, string? ip, string? label, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var device = new UserDevice
        {
            DeviceId     = NewDeviceId(),
            UserId       = userId,
            Label        = label ?? DeriveLabel(userAgent),
            UserAgent    = userAgent,
            FirstSeenAt  = now,
            LastSeenAt   = now,
            FirstSeenIp  = ip,
            LastSeenIp   = ip,
            IsTrusted    = false
        };
        _db.UserDevices.Add(device);
        await _db.SaveChangesAsync(ct);
        return device;
    }

    public Task<UserDevice?> FindActiveAsync(string deviceId, CancellationToken ct = default)
        => _db.UserDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.RevokedAt == null, ct);

    public async Task TouchAsync(string deviceId, string? ip, CancellationToken ct = default)
    {
        var d = await _db.UserDevices.FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (d is null || d.RevokedAt != null) return;
        d.LastSeenAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(ip)) d.LastSeenIp = ip;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UserDevice>> ListForUserAsync(string userId, CancellationToken ct = default)
    {
        return await _db.UserDevices
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.RevokedAt == null)
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync(ct);
    }

    public async Task RevokeAsync(string deviceId, string reason, string? actingIp, CancellationToken ct = default)
    {
        var d = await _db.UserDevices.FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (d is null || d.RevokedAt != null) return;

        var now = DateTime.UtcNow;
        d.RevokedAt    = now;
        d.RevokeReason = reason;

        // Cascade: revoke any active refresh token bound to this device.
        var tokens = await _db.RefreshTokens
            .Where(t => t.DeviceId == deviceId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in tokens)
        {
            t.RevokedAt    = now;
            t.RevokeReason = reason;
            t.RevokedByIp  = actingIp;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllExceptAsync(string userId, string keepDeviceId, string reason, string? actingIp, CancellationToken ct = default)
    {
        var others = await _db.UserDevices
            .Where(d => d.UserId == userId && d.DeviceId != keepDeviceId && d.RevokedAt == null)
            .ToListAsync(ct);
        if (others.Count == 0) return;

        var now    = DateTime.UtcNow;
        var ids    = others.Select(d => d.DeviceId).ToList();
        var tokens = await _db.RefreshTokens
            .Where(t => t.DeviceId != null && ids.Contains(t.DeviceId) && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var d in others)
        {
            d.RevokedAt    = now;
            d.RevokeReason = reason;
        }
        foreach (var t in tokens)
        {
            t.RevokedAt    = now;
            t.RevokeReason = reason;
            t.RevokedByIp  = actingIp;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string NewDeviceId()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string DeriveLabel(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown device";
        // Cheap UA → friendly label heuristic; replace with UAParser later if needed.
        var ua = userAgent.ToLowerInvariant();
        var browser = ua.Contains("edg/")     ? "Edge"
                    : ua.Contains("chrome/")  ? "Chrome"
                    : ua.Contains("firefox/") ? "Firefox"
                    : ua.Contains("safari/")  ? "Safari"
                    : "Browser";
        var os      = ua.Contains("windows")  ? "Windows"
                    : ua.Contains("mac os")   ? "macOS"
                    : ua.Contains("linux")    ? "Linux"
                    : ua.Contains("android")  ? "Android"
                    : ua.Contains("iphone") || ua.Contains("ipad") ? "iOS"
                    : "Unknown";
        return $"{browser} on {os}";
    }
}

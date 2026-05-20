using System.Security.Cryptography;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

public class RefreshTokenStore : IRefreshTokenStore
{
    private readonly IApplicationDbContext _db;
    private readonly JwtOptions _jwt;

    public RefreshTokenStore(IApplicationDbContext db, IOptions<JwtOptions> jwt)
    { _db = db; _jwt = jwt.Value; }

    public async Task<string> IssueAsync(string userId, string? deviceId, DateTime accessTokenExpiresUtc, string? createdByIp, CancellationToken ct = default)
    {
        var token = NewToken();
        var now   = DateTime.UtcNow;
        var ttl   = (accessTokenExpiresUtc - now).TotalMinutes * _jwt.RefreshTokenFactor;

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id          = token,
            UserId      = userId,
            DeviceId    = deviceId,
            IssuedAt    = now,
            ExpiresAt   = now.AddMinutes(ttl),
            CreatedByIp = createdByIp
        });
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public Task<RefreshToken?> FindAsync(string tokenId, CancellationToken ct = default)
        => _db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == tokenId, ct);

    public async Task<string> RotateAsync(RefreshToken existing, DateTime newAccessTokenExpiresUtc, string? actingIp, CancellationToken ct = default)
    {
        var now      = DateTime.UtcNow;
        var newToken = NewToken();
        var ttl      = (newAccessTokenExpiresUtc - now).TotalMinutes * _jwt.RefreshTokenFactor;

        existing.RevokedAt    = now;
        existing.RevokedByIp  = actingIp;
        existing.RevokeReason = "Rotated";
        existing.ReplacedBy   = newToken;

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id          = newToken,
            UserId      = existing.UserId,
            DeviceId    = existing.DeviceId,    // rotation keeps the device binding
            IssuedAt    = now,
            ExpiresAt   = now.AddMinutes(ttl),
            CreatedByIp = actingIp
        });
        await _db.SaveChangesAsync(ct);
        return newToken;
    }

    public async Task RevokeAsync(string tokenId, string reason, string? actingIp, CancellationToken ct = default)
    {
        var t = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Id == tokenId, ct);
        if (t is null || t.RevokedAt != null) return;
        t.RevokedAt    = DateTime.UtcNow;
        t.RevokeReason = reason;
        t.RevokedByIp  = actingIp;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(string userId, string reason, string? actingIp, CancellationToken ct = default)
    {
        var now    = DateTime.UtcNow;
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var t in active)
        {
            t.RevokedAt    = now;
            t.RevokeReason = reason;
            t.RevokedByIp  = actingIp;
        }
        if (active.Count > 0) await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeChainAsync(RefreshToken suspect, string? actingIp, CancellationToken ct = default)
    {
        var now  = DateTime.UtcNow;
        var seen = new HashSet<string>();
        var queue = new Queue<RefreshToken>();
        queue.Enqueue(suspect);

        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (!seen.Add(t.Id)) continue;

            if (t.RevokedAt is null)
            {
                t.RevokedAt    = now;
                t.RevokeReason = "ChainCompromised";
                t.RevokedByIp  = actingIp;
            }

            if (!string.IsNullOrEmpty(t.ReplacedBy))
            {
                var next = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Id == t.ReplacedBy, ct);
                if (next != null) queue.Enqueue(next);
            }

            var prev = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.ReplacedBy == t.Id, ct);
            if (prev != null) queue.Enqueue(prev);
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

public interface IRefreshTokenStore
{
    Task<string> IssueAsync(string userId, string? deviceId, DateTime accessTokenExpiresUtc, string? createdByIp, CancellationToken ct = default);
    Task<RefreshToken?> FindAsync(string tokenId, CancellationToken ct = default);
    Task<string> RotateAsync(RefreshToken existing, DateTime newAccessTokenExpiresUtc, string? actingIp, CancellationToken ct = default);
    Task RevokeAsync(string tokenId, string reason, string? actingIp, CancellationToken ct = default);
    Task RevokeAllForUserAsync(string userId, string reason, string? actingIp, CancellationToken ct = default);
    Task RevokeChainAsync(RefreshToken suspect, string? actingIp, CancellationToken ct = default);
}

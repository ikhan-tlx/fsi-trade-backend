using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Presence;

/// <summary>
/// Computes presence as MAX(<c>UserDevices.LastSeenAt</c>) per requested user
/// (active, non-revoked devices only) and flags "available" when that is within
/// <see cref="FreshnessMinutes"/> of now. Every requested id is echoed back so
/// the FE can render a row per pool member even when a user has never signed in
/// on a tracked device (→ lastSeenAt null, isAvailable false).
/// </summary>
public class GetUsersPresenceQueryHandler
    : IRequestHandler<GetUsersPresenceQuery, IReadOnlyList<UserPresenceDto>>
{
    /// <summary>Activity window that still counts as "available now".</summary>
    public const int FreshnessMinutes = 5;

    private readonly IApplicationDbContext _db;

    public GetUsersPresenceQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserPresenceDto>> Handle(
        GetUsersPresenceQuery req,
        CancellationToken      ct)
    {
        var ids = (req.UserIds ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0) return Array.Empty<UserPresenceDto>();

        var now       = DateTime.UtcNow;
        var threshold = now.AddMinutes(-FreshnessMinutes);

        // One grouped read: latest activity per user across their active devices.
        var lastSeenByUser = (await _db.UserDevices.AsNoTracking()
                .Where(d => ids.Contains(d.UserId) && d.RevokedAt == null)
                .GroupBy(d => d.UserId)
                .Select(g => new { UserId = g.Key, LastSeen = g.Max(x => x.LastSeenAt) })
                .ToListAsync(ct))
            .ToDictionary(x => x.UserId, x => x.LastSeen, StringComparer.OrdinalIgnoreCase);

        return ids.Select(id =>
        {
            var has = lastSeenByUser.TryGetValue(id, out var lastSeen);
            return new UserPresenceDto
            {
                userId      = id,
                lastSeenAt  = has ? lastSeen : null,
                isAvailable = has && lastSeen >= threshold
            };
        }).ToList();
    }
}

using FSI.Trade.Compliance.Application.Contracts.Locations;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Locations;

/// <summary>
/// In-memory walker over <c>TmX_Location</c>. Loads every (active) row once,
/// builds a <c>parentId → children[]</c> map, and BFS-walks from the root.
///
/// Performance note: today the table is &lt;500 rows so the single
/// <c>ToListAsync</c> + dictionary walk completes in low milliseconds, and
/// the parent→children map is small enough to garbage-collect quickly. If
/// the table grows past ~10k rows we'd want either:
/// <list type="bullet">
///   <item>A recursive CTE keyed by <paramref name="rootLocationId"/>
///         (one SQL round-trip, no caching), OR</item>
///   <item>An <c>IMemoryCache</c>-backed parent→children map refreshed on
///         a TTL (mirrors the legacy 1-day cache in <c>LocationService</c>).</item>
/// </list>
/// </summary>
public class LocationHierarchyService : ILocationHierarchyService
{
    private readonly IApplicationDbContext _db;

    public LocationHierarchyService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyCollection<int>> GetSelfAndDescendantIdsAsync(int rootLocationId, CancellationToken ct)
    {
        // Read flat — child→parent pairs. Filtering by Active_Flag matches the
        // legacy behaviour of treating inactive locations as if they don't
        // exist for scoping purposes. Effective-date check intentionally not
        // applied here — legacy didn't either; the FSI direction (May 2026)
        // was "treat effective dates as informational on location rows".
        var pairs = await _db.Locations
            .AsNoTracking()
            .Where(l => l.ActiveFlag)
            .Select(l => new { l.Id, l.ParentLocationId })
            .ToListAsync(ct);

        // parentId → list of child ids
        var childMap = pairs
            .Where(p => p.ParentLocationId.HasValue)
            .GroupBy(p => p.ParentLocationId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new HashSet<int> { rootLocationId };
        var queue  = new Queue<int>();
        queue.Enqueue(rootLocationId);

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!childMap.TryGetValue(parent, out var children)) continue;
            foreach (var child in children)
                if (result.Add(child))
                    queue.Enqueue(child);
        }

        return result;
    }
}

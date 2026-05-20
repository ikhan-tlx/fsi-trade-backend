namespace FSI.Trade.Compliance.Application.Contracts.Locations;

/// <summary>
/// Resolves "what locations are under this one" for scoping queries by a
/// caller's organisational reach (e.g. Slice 6 Transaction list — a regional
/// manager sees every branch in their subtree).
///
/// Today's implementation walks <c>TmX_Location</c> in memory after one
/// <c>ToListAsync</c>; the location table is small (&lt;500 rows in this
/// deployment) so this is faster than a recursive CTE and far simpler. If
/// the table ever crosses ~10k rows, swap the implementation to a recursive
/// SQL CTE — the consumers don't change.
///
/// NOTE on legacy parity: <c>LocationService.GetChildLocationsIdsRecursive</c>
/// returned descendants ONLY (excluding the root). Our predicate scopes by
/// "locations the user is responsible for", which logically INCLUDES the
/// user's home location. So <see cref="GetSelfAndDescendantIdsAsync"/>
/// always includes the input <paramref name="rootLocationId"/> in the result.
/// </summary>
public interface ILocationHierarchyService
{
    /// <summary>
    /// Returns the input location ID plus the IDs of every active descendant,
    /// transitively. If <paramref name="rootLocationId"/> isn't found in the
    /// table, returns just the input (so the caller's predicate degenerates
    /// safely to "only my exact location").
    /// </summary>
    Task<IReadOnlyCollection<int>> GetSelfAndDescendantIdsAsync(int rootLocationId, CancellationToken ct);
}

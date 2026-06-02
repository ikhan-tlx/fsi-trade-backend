using System.Collections.Concurrent;

namespace FSI.Trade.Compliance.Application.Common.Caching;

/// <summary>
/// Slice 12 §A — per-product version stamp used as a cache-key segment by
/// the form-schema read handlers (<c>GetProductFormDefinitionQueryHandler</c>,
/// <c>ListProductTabsQueryHandler</c>, <c>GetTabFieldsQueryHandler</c>).
///
/// <para>
/// Why this exists: <c>IMemoryCache</c> doesn't support wildcard / tag-based
/// eviction. After a field admin edit (<c>PUT /FieldSetup/{id}</c>), all
/// three caches must miss for that product across every culture variant.
/// Embedding <see cref="Get"/> in the cache key lets us invalidate every
/// variant in one shot by calling <see cref="Bump"/> — the next read
/// computes a new key and re-populates the cache.
/// </para>
///
/// <para>
/// Process-local — multi-instance deployments will warm independently
/// after an edit (the edit happens on one instance; other instances keep
/// serving stale reads until their own bump or natural 15-min expiry).
/// That's acceptable for admin-edited form schema; if it becomes an issue,
/// replace with a distributed cache invalidation hook (Redis pub/sub,
/// SignalR, etc.).
/// </para>
/// </summary>
public static class FormSchemaCacheVersion
{
    private static readonly ConcurrentDictionary<int, int> Versions = new();

    /// <summary>Current version stamp for a product. Starts at 1.</summary>
    public static int Get(int productId)
        => Versions.GetOrAdd(productId, _ => 1);

    /// <summary>
    /// Increments the version for a product, invalidating every cached
    /// form-schema entry for that product on the next read. Idempotent
    /// under concurrent calls — uses <c>AddOrUpdate</c>.
    /// </summary>
    public static void Bump(int productId)
        => Versions.AddOrUpdate(productId, 2, (_, v) => v + 1);
}

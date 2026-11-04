using Microsoft.Extensions.Primitives;

namespace FSI.Trade.Compliance.Application.Features.Lookups.Caching;

/// <summary>
/// Process-wide invalidation signal for the cached v2 grouped lookup catalogue
/// (<see cref="GetGroupedByCulture.GetLookupsGroupedByCultureQueryHandler"/>).
///
/// That catalogue is held in <c>IMemoryCache</c> per (tenant, culture) with a
/// 30-day TTL, and the assembled data is cross-tenant — so a single lookup
/// change must drop EVERY cached (tenant, culture) entry at once, not just the
/// approver's. <c>IMemoryCache</c> can't enumerate keys, so instead every
/// catalogue entry is registered against the shared <see cref="Token"/>;
/// calling <see cref="Invalidate"/> trips that token and expires all of them
/// together. The lookup approval path calls <see cref="Invalidate"/> so a newly
/// approved lookup shows up on the very next form load instead of waiting on the
/// TTL or an app restart.
///
/// NOTE: <c>IMemoryCache</c> is per process. In a multi-worker IIS app pool
/// (web garden) or a load-balanced farm each worker/node has its own cache and
/// its own signal, so this evicts only the node that processed the approval. A
/// multi-instance deployment needs distributed invalidation (a shared cache or a
/// broadcast) for cross-node freshness.
/// </summary>
public interface ILookupCatalogueCacheSignal
{
    /// <summary>
    /// Change token a catalogue cache entry attaches to (via
    /// <c>MemoryCacheEntryOptions.AddExpirationToken</c>). Trips on the next
    /// <see cref="Invalidate"/>.
    /// </summary>
    IChangeToken Token { get; }

    /// <summary>Expire every catalogue cache entry registered against the current token.</summary>
    void Invalidate();
}

/// <inheritdoc />
public sealed class LookupCatalogueCacheSignal : ILookupCatalogueCacheSignal
{
    private volatile CancellationTokenSource _cts = new();

    public IChangeToken Token => new CancellationChangeToken(Volatile.Read(ref _cts).Token);

    public void Invalidate()
    {
        // Swap in a fresh source first so new reads bind to a live token, then trip
        // the retired one to expire every catalogue entry registered against it.
        // The old source is intentionally left for GC rather than disposed: a reader
        // that just captured it must not hit ObjectDisposedException when MemoryCache
        // registers its expiration callback. Lookup writes are rare (admin, maker-
        // checker) so the handful of retired sources over the app lifetime is trivial.
        var retired = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        retired.Cancel();
    }
}

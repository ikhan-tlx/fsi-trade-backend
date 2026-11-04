using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Lookups.Caching;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetGroupedByCulture;

/// <summary>
/// Builds the v2 grouped catalogue. Same cross-tenant cascade + culture
/// resolution as v1; differs only in projection shape (dictionary
/// keyed by lookupType, lean row records).
///
/// CACHING: per (callerTenantId, culture) in-memory with 30-day TTL.
/// The lookup catalogue is reference data — it changes rarely (admin
/// edits, occasional bulk imports) and is read on every app boot, so
/// keeping the assembled dictionary in process memory is a big win on
/// startup latency. Cache eviction strategies:
/// <list type="bullet">
///   <item>Automatic — 30-day sliding/absolute expiry.</item>
///   <item>App restart — process cache dies with the process.</item>
///   <item>Manual — bump <c>CacheKeyVersion</c> below and redeploy when
///         the schema or projection changes shape.</item>
/// </list>
/// If admin edit endpoints land later, they should call
/// <c>IMemoryCache.Remove</c> for the relevant key(s) to force the next
/// read to refresh. The cache is also tenant-scoped so per-customer
/// overrides don't leak between tenants.
/// </summary>
public class GetLookupsGroupedByCultureQueryHandler
    : IRequestHandler<GetLookupsGroupedByCultureQuery, LookupCatalogueDto>
{
    private const int PlatformDefaultTenantId = 1;

    /// <summary>Bump on any shape/contract change so old entries auto-invalidate after deploy.</summary>
    private const string CacheKeyVersion = "v1";

    /// <summary>30-day TTL — "monthly" per FSI direction (2026-05-29).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);

    private readonly IApplicationDbContext        _db;
    private readonly ICurrentUserService          _current;
    private readonly IMemoryCache                 _cache;
    private readonly ILookupCatalogueCacheSignal  _cacheSignal;

    public GetLookupsGroupedByCultureQueryHandler(
        IApplicationDbContext       db,
        ICurrentUserService         current,
        IMemoryCache                cache,
        ILookupCatalogueCacheSignal cacheSignal)
    {
        _db          = db;
        _current     = current;
        _cache       = cache;
        _cacheSignal = cacheSignal;
    }

    public async Task<LookupCatalogueDto> Handle(GetLookupsGroupedByCultureQuery req, CancellationToken ct)
    {
        var callerTenantId = _current.TenantId ?? PlatformDefaultTenantId;
        var cultureLower   = (req.Culture ?? "en").Trim().ToLowerInvariant();
        var isEnglish      = cultureLower.Length == 0 || cultureLower == "en";

        // Per-(tenant, culture) cache key. Tenant is included because the
        // cascade picks different rows depending on the caller's tenant —
        // we cannot share entries across tenants.
        var cacheKey = $"Lookups::Grouped::{CacheKeyVersion}::tenant={callerTenantId}::culture={cultureLower}";

        if (_cache.TryGetValue<LookupCatalogueDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        var raw = await _db.Lookups.AsNoTracking()
            .Where(l => l.IsActive == true && l.LookupType != null)
            .Select(l => new
            {
                l.Id,
                l.ParentLookupId,
                l.LookupType,
                l.TenantId,
                l.HiddenValue,
                l.VisibleValue,
                l.LocaleLabel,
                l.SortOrder
            })
            .ToListAsync(ct);

        // Cross-tenant lookups (2026-06-09, per FSI): the previous logic picked a
        // SINGLE owning tenant per lookup type (caller > tenant 1 > lowest) and
        // dropped every other tenant's rows. But the ICBC trade flow's reference
        // data is split across tenants — e.g. APPLICATION_STATUS lives partly
        // under 245 (Sent To Authorizer 422, Assigned To Authorizer 3053) and
        // partly under 248 ("Proceeded To Screen" 424) — so the FE roster, owned
        // by 245, could not label the post-Proceed status. Legacy did NOT
        // tenant-filter the lookup catalogue.
        //
        // We now drop the single-tenant filter and include EVERY tenant's rows
        // per type, de-duplicated by visible value with the same preference order
        // (caller tenant > platform tenant 1 > lowest tenant id). Cross-tenant
        // DISTINCT values (like "Proceeded To Screen") are all surfaced, while a
        // value that genuinely exists in multiple tenants collapses to one stable
        // id — so no duplicate dropdown options appear. This keeps the catalogue
        // consistent with the (already tenant-agnostic) status-sync resolver.
        var grouped = raw
            .GroupBy(r => r.LookupType!)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(r => (r.VisibleValue ?? string.Empty).Trim().ToLowerInvariant())
                    .Select(vg => vg
                        .OrderBy(r => TenantRank(r.TenantId, callerTenantId))
                        .ThenBy(r => r.TenantId)
                        .First())
                    .OrderBy(r => r.SortOrder)
                    .ThenBy(r => r.VisibleValue)
                    .Select(r => new LookupOptionDto
                    {
                        id     = r.Id,
                        code   = r.HiddenValue,
                        label  = isEnglish
                                     ? r.VisibleValue
                                     : (string.IsNullOrEmpty(r.LocaleLabel) ? r.VisibleValue : r.LocaleLabel),
                        parent = r.ParentLookupId
                    })
                    .ToList());

        var result = new LookupCatalogueDto
        {
            culture = string.IsNullOrEmpty(cultureLower) ? "en" : cultureLower,
            lookups = grouped
        };

        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            // Reference-data working set — keep the entry through
            // memory-pressure evictions unless it actually expires.
            Priority = CacheItemPriority.High
        };
        // Bind the entry to the shared invalidation token so an approved lookup
        // change (any tenant/culture) drops every cached catalogue entry at once —
        // see ILookupCatalogueCacheSignal. Without this the entry would linger for
        // the full 30-day TTL and new lookups would never appear on forms.
        entryOptions.AddExpirationToken(_cacheSignal.Token);
        _cache.Set(cacheKey, result, entryOptions);

        return result;
    }

    /// <summary>
    /// Cross-tenant de-dup preference (replaces the old single-owning-tenant
    /// cascade): caller tenant first, then platform tenant 1, then any other
    /// (lowest tenant id wins via the secondary ThenBy on TenantId). Used to
    /// pick which row survives when the same visible value exists under more
    /// than one tenant, so the catalogue surfaces every distinct value while
    /// avoiding duplicate options.
    /// </summary>
    private static int TenantRank(int tenantId, int callerTenantId) =>
        tenantId == callerTenantId ? 0 : tenantId == PlatformDefaultTenantId ? 1 : 2;
}

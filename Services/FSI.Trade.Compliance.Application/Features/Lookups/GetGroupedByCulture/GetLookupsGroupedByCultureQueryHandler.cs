using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
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

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IMemoryCache          _cache;

    public GetLookupsGroupedByCultureQueryHandler(
        IApplicationDbContext db,
        ICurrentUserService   current,
        IMemoryCache          cache)
    {
        _db      = db;
        _current = current;
        _cache   = cache;
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

        // Per-LookupType ownership (caller > Tenant 1 > lowest).
        var tenantByType = raw
            .GroupBy(r => r.LookupType!)
            .ToDictionary(
                g => g.Key,
                g => ResolveOwningTenant(g.Select(x => x.TenantId).Distinct(), callerTenantId));

        var grouped = raw
            .Where(r => tenantByType[r.LookupType!] == r.TenantId)
            .GroupBy(r => r.LookupType!)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.SortOrder)
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

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            // Reference-data working set — keep the entry through
            // memory-pressure evictions unless it actually expires.
            Priority = CacheItemPriority.High
        });

        return result;
    }

    /// <summary>Same cascade as v1 — see <c>GetLookupsByCultureQueryHandler.ResolveOwningTenant</c>.</summary>
    private static int ResolveOwningTenant(IEnumerable<int> tenantsWithData, int callerTenantId)
    {
        var set = tenantsWithData as ICollection<int> ?? tenantsWithData.ToList();
        if (set.Contains(callerTenantId))         return callerTenantId;
        if (set.Contains(PlatformDefaultTenantId)) return PlatformDefaultTenantId;
        return set.Min();
    }
}

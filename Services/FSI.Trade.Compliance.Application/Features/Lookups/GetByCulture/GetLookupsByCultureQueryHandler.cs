using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;

/// <summary>
/// Returns the lookup catalogue filtered to active rows. Cross-tenant by
/// design: the deployed schema partitions reference data across multiple
/// Tenant_ID values (Tenant 1 holds the bulk catalogue — HSCodeList ~7,600
/// rows, Industry ~500, Port ~43,000, etc. — while Tenants 12 / 245 /
/// 248 hold per-customer overrides). The legacy stored procedure
/// <c>sp_GetLookupByCulture</c> applies no tenant filter at all; we
/// implement a cascade so a caller on Tenant 245 still sees Tenant 1's
/// catalogue rows where Tenant 245 hasn't defined its own.
///
/// Cascade rule (per <c>Lookup_Type</c>):
/// <list type="number">
///   <item>If the caller's tenant has at least one active row, use those.</item>
///   <item>Else fall back to Tenant 1 (platform default).</item>
///   <item>Else use the lowest Tenant_ID that has data (rare).</item>
/// </list>
///
/// Culture handling matches legacy: en (or unspecified) returns
/// <c>Visible_Value</c> verbatim; any other culture returns
/// <c>Locale_Label</c> if present, falling back to <c>Visible_Value</c>.
/// No <c>Locale_Label.StartsWith</c> filter — Locale_Label is a display
/// column, not a row-filter key.
/// </summary>
public class GetLookupsByCultureQueryHandler
    : IRequestHandler<GetLookupsByCultureQuery, List<LookupItemDto>>
{
    private const int PlatformDefaultTenantId = 1;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;

    public GetLookupsByCultureQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    {
        _db      = db;
        _current = current;
    }

    public async Task<List<LookupItemDto>> Handle(GetLookupsByCultureQuery req, CancellationToken ct)
    {
        var callerTenantId = _current.TenantId ?? PlatformDefaultTenantId;
        var cultureLower   = (req.Culture ?? "en").Trim().ToLowerInvariant();
        var isEnglish      = cultureLower.Length == 0 || cultureLower == "en";

        // Pull every active lookup row + its (LookupType, TenantId) pair into
        // memory once. The catalogue is bounded (~60K rows total in this
        // deployment), and the cascade decision is per-LookupType which is
        // awkward to express in pure SQL/LINQ without a correlated subquery.
        // In-memory dedupe is the simpler, equally-correct path.
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
                l.SortOrder,
                l.IsActive
            })
            .ToListAsync(ct);

        // Decide the "owning" tenant per lookup type:
        //   caller's tenant if it has data, else Tenant 1, else lowest tenant.
        var tenantByType = raw
            .GroupBy(r => r.LookupType!)
            .ToDictionary(
                g => g.Key,
                g => ResolveOwningTenant(g.Select(x => x.TenantId).Distinct(), callerTenantId));

        // Filter raw to only the owning tenant's rows per type, then project
        // and order.
        var rows = raw
            .Where(r => tenantByType[r.LookupType!] == r.TenantId)
            .OrderBy(r => r.LookupType)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.VisibleValue)
            .Select(r => new LookupItemDto
            {
                lookupId       = r.Id,
                parentLookupId = r.ParentLookupId,
                lookupType     = r.LookupType ?? "",
                hiddenValue    = r.HiddenValue,
                visibleValue   = isEnglish
                                    ? r.VisibleValue
                                    : (string.IsNullOrEmpty(r.LocaleLabel) ? r.VisibleValue : r.LocaleLabel),
                sortOrder      = r.SortOrder,
                isActive       = r.IsActive
            })
            .ToList();

        return rows;
    }

    /// <summary>
    /// Per-type tenant ownership rule. Caller's tenant wins; otherwise
    /// fall back to Tenant 1; otherwise the lowest tenant id with data.
    /// </summary>
    private static int ResolveOwningTenant(IEnumerable<int> tenantsWithData, int callerTenantId)
    {
        var set = tenantsWithData as ICollection<int> ?? tenantsWithData.ToList();
        if (set.Contains(callerTenantId))         return callerTenantId;
        if (set.Contains(PlatformDefaultTenantId)) return PlatformDefaultTenantId;
        return set.Min();
    }
}

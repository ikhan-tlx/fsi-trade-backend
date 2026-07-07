using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Dashboard.Summary;

/// <summary>
/// Phase 1 operations-dashboard aggregate. Tenant-scoped, cached for a short
/// TTL (the landing page is hit a lot and the data changes slowly). See
/// <see cref="GetDashboardSummaryQuery"/> for the Phase-1 scope / nulls.
/// </summary>
public class GetDashboardSummaryQueryHandler
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private const int PlatformDefaultTenantId = 1;
    private const string CancelledStatusLookupType   = "APPLICATION_STATUS";
    private const string CancelledStatusVisibleValue = "Application Cancelled";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IMemoryCache          _cache;

    public GetDashboardSummaryQueryHandler(
        IApplicationDbContext db, ICurrentUserService current, IMemoryCache cache)
    {
        _db      = db;
        _current = current;
        _cache   = cache;
    }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery req, CancellationToken ct)
    {
        var (range, from, to) = DashboardRange.Resolve(req.Range);

        // Tenant scoping intentionally removed (2026-07): the dashboard now aggregates
        // across ALL tenants. The tenant field is being retired application-wide; until
        // then no Tenant_Id predicate is applied to any dashboard aggregate below.
        var cacheKey = $"Dashboard::Summary::v1::range={range}";
        if (_cache.TryGetValue<DashboardSummaryDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        var dto = new DashboardSummaryDto { range = range, asOf = to };

        // ---- Customers (distinct by Customer_Code; risk from snapshot UDF) ----
        var snaps = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.CustomerCode != null)
            .Select(c => new { c.CustomerCode, c.CreatedDate, c.UdfData })
            .ToListAsync(ct);

        int low = 0, medium = 0, high = 0;   // high includes PEP
        int totalCustomers = 0, onboarded = 0;

        foreach (var grp in snaps.GroupBy(s => s.CustomerCode!))
        {
            totalCustomers++;
            if (grp.Min(x => x.CreatedDate) >= from) onboarded++;

            // Latest snapshot's RiskScore wins (only KYC-onboarded customers have one).
            var latest = grp.OrderByDescending(x => x.CreatedDate).First();
            var band   = RiskBandClassifier.Classify(ExtractRiskScore(latest.UdfData));
            switch (band)
            {
                case RiskBand.Low:    low++;    break;
                case RiskBand.Medium: medium++; break;
                case RiskBand.High:
                case RiskBand.Pep:    high++;   break;
                // Unknown → counted in total only (non-KYC / no risk yet).
            }
        }

        dto.kpis.activeCustomers   = new KpiActiveCustomersDto { value = totalCustomers, onboarded = onboarded };
        dto.riskMix                = new RiskMixDto { total = totalCustomers, low = low, medium = medium, high = high, inEdd = null };
        dto.kpis.highRiskCustomers = new KpiHighRiskDto
        {
            value     = high,
            delta     = null,
            pctOfBook = totalCustomers > 0 ? Math.Round(high * 100.0 / totalCustomers, 1) : null,
            inEdd     = null
        };

        // ---- Instruments (open = not "Application Cancelled") ----
        var cancelledLkp = await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == CancelledStatusLookupType && l.VisibleValue == CancelledStatusVisibleValue)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

        var txRows = await _db.Transactions.AsNoTracking()
            .Where(t => cancelledLkp == null || t.TransactionStatusLkp != cancelledLkp)
            .Select(t => new { t.ProductId, t.SubProductTabId })
            .ToListAsync(ct);

        dto.kpis.activeInstruments = new KpiActiveInstrumentsDto { value = txRows.Count, deltaPct = null, mix = null };
        dto.kpis.tradeExposureUsd  = new KpiTradeExposureDto { value = null, deltaUsd = null };   // Phase 2

        // Instrument-type labels: sub-product tab name when set, else product name.
        var productIds = txRows.Select(r => r.ProductId).Distinct().ToList();
        var prodNames = (await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductName })
                .ToListAsync(ct))
            .ToDictionary(x => x.ProductId, x => x.ProductName);

        var tabIds = txRows.Where(r => r.SubProductTabId.HasValue).Select(r => r.SubProductTabId!.Value).Distinct().ToList();
        var tabNames = tabIds.Count == 0
            ? new Dictionary<int, string?>()
            : (await _db.Tabs.AsNoTracking()
                    .Where(t => tabIds.Contains(t.TabId))
                    .Select(t => new { t.TabId, Name = t.LocaleLabel ?? t.TabName })
                    .ToListAsync(ct))
                .ToDictionary(x => x.TabId, x => x.Name);

        dto.instrumentsByType = txRows
            .GroupBy(r => ResolveInstrumentLabel(r.SubProductTabId, r.ProductId, tabNames, prodNames))
            .Select(g => new InstrumentTypeDto { type = g.Key, count = g.Count(), exposureUsd = null })
            .OrderByDescending(i => i.count)
            .ToList();

        // ---- Trend (Phase 1.5 — COUNT series, not USD) ----
        // New instruments per bucket over the range, from Created_Date.
        var trendDates = await _db.Transactions.AsNoTracking()
            .Where(t => t.CreatedDate >= from && t.CreatedDate <= to
                     && (cancelledLkp == null || t.TransactionStatusLkp != cancelledLkp))
            .Select(t => t.CreatedDate)
            .ToListAsync(ct);
        dto.exposureTrend = DashboardTrend.Build(range, from, to, trendDates);

        // ---- Phase 2 (not modeled yet) ----
        dto.countryExposure = new List<CountryExposureDto>();

        _cache.Set(cacheKey, dto, CacheTtl);
        return dto;
    }

    private static string ResolveInstrumentLabel(
        int? subProductTabId, int productId,
        IReadOnlyDictionary<int, string?> tabNames, IReadOnlyDictionary<int, string?> prodNames)
    {
        if (subProductTabId.HasValue && tabNames.TryGetValue(subProductTabId.Value, out var tn) && !string.IsNullOrWhiteSpace(tn))
            return tn!;
        if (prodNames.TryGetValue(productId, out var pn) && !string.IsNullOrWhiteSpace(pn))
            return pn!;
        return "Unknown";
    }

    /// <summary>
    /// Reads a risk value from a customer-snapshot UDF blob. Matches the key
    /// case-insensitively against common spellings ("RiskScore" / "riskScore" /
    /// "Risk_Score" / "RiskLevel" / "Risk_Level"), since the writer convention
    /// varies between the new wizard and legacy data.
    /// </summary>
    private static string? ExtractRiskScore(string? udf)
    {
        if (string.IsNullOrWhiteSpace(udf)) return null;
        try
        {
            using var doc = JsonDocument.Parse(udf);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var name = p.Name.Replace("_", "");   // RiskScore / Risk_Score / RiskLevel …
                if (name.Equals("RiskScore", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("RiskLevel", StringComparison.OrdinalIgnoreCase))
                {
                    return p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        _                    => null
                    };
                }
            }
        }
        catch (JsonException) { /* corrupt UDF → no risk */ }
        return null;
    }
}

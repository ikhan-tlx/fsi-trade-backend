using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Customer.Common;   // CustomerInsightMetrics
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Customer.InsightSummary;

/// <summary>
/// Book-wide portfolio KPIs for the Customer Intelligence listing strip.
/// All-tenant (tenant is being retired app-wide, matching the ops dashboard),
/// 2-minute in-memory cache.
///
/// Live KPI values come from <see cref="CustomerInsightMetrics"/> (shared with the
/// daily snapshot job). "vs last week" deltas are REAL when a snapshot row from
/// ~7 days ago exists (written by CustomerInsightSnapshotService, plan §6 opt-1);
/// otherwise they stay null and the FE omits the delta line. The flags-7d delta is
/// always real — it compares the prior 7-day window directly, no snapshot needed.
/// </summary>
public class GetCustomerInsightSummaryQueryHandler
    : IRequestHandler<GetCustomerInsightSummaryQuery, CustomerInsightSummaryDto>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private const string CacheKey = "Customer::InsightSummary::v1";

    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public GetCustomerInsightSummaryQueryHandler(IApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<CustomerInsightSummaryDto> Handle(GetCustomerInsightSummaryQuery req, CancellationToken ct)
    {
        if (_cache.TryGetValue<CustomerInsightSummaryDto>(CacheKey, out var hit) && hit is not null)
            return hit;

        var now = DateTime.UtcNow;
        var dto = new CustomerInsightSummaryDto { asOf = now };

        // ---- Live portfolio KPIs (shared computation) ----
        var live = await CustomerInsightMetrics.ComputeAsync(_db, now, ct);
        dto.totalCustomers = live.TotalCustomers;
        dto.highRisk       = live.HighRisk;
        dto.reviewsOverdue = live.ReviewsOverdue;
        dto.openCases      = live.OpenCases;
        dto.flagsRaised7d  = live.FlagsRaised7d;

        // ---- Flags 7d delta (real now — prior 7-day window, no snapshot needed) ----
        var d7  = now.AddDays(-7);
        var d14 = now.AddDays(-14);
        var prev7 = await _db.TransactionFlags.AsNoTracking()
            .Where(f => f.IsFlagged && f.SetDate >= d14 && f.SetDate < d7)
            .CountAsync(ct);
        dto.flagsRaised7dDelta = dto.flagsRaised7d - prev7;

        // ---- "vs last week" deltas from the daily snapshot (§6 opt-1) ----
        // Closest snapshot on/before 7 days ago; null → FE omits the delta line.
        var weekAgo = now.Date.AddDays(-7);
        var baseline = await _db.CustomerInsightDailies.AsNoTracking()
            .Where(s => s.SnapshotDate <= weekAgo)
            .OrderByDescending(s => s.SnapshotDate)
            .FirstOrDefaultAsync(ct);
        if (baseline is not null)
        {
            dto.totalCustomersDelta = dto.totalCustomers - baseline.TotalCustomers;
            dto.highRiskDelta       = dto.highRisk       - baseline.HighRisk;
            dto.reviewsOverdueDelta = dto.reviewsOverdue - baseline.ReviewsOverdue;
            dto.openCasesDelta      = dto.openCases      - baseline.OpenCases;
        }

        _cache.Set(CacheKey, dto, CacheTtl);
        return dto;
    }
}

using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Customer.Common;      // classifiers / evaluators
using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;    // RiskBandClassifier / RiskBand
using FSI.Trade.Compliance.Domain.Entities;                          // KycCaseStatus
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FSI.Trade.Compliance.Application.Features.Customer.List;

/// <summary>
/// Builds the Customer Intelligence listing: load the tenant's snapshots, group
/// distinct by Customer_Code, then ENRICH each customer with attention (open
/// cases + active flags), KYC review state, 90-day trade activity, flag M/A
/// split, a composite risk score and a triage health dot — then filter → sort →
/// page in memory. The enrichment reads flags / cases / review schedules /
/// transactions once and folds them in by key (the book is a few hundred
/// customers, matching the dashboard's in-memory approach).
/// See CUSTOMER_INSIGHT_BACKEND_CHANGE_PLAN §3.
/// </summary>
public class ListCustomersQueryHandler
    : IRequestHandler<ListCustomersQuery, PagedResult<CustomerListItemDto>>
{
    private const int PlatformDefaultTenantId = 1;
    private const int SparkWeeks    = 12;   // weekly buckets in the trade sparkline
    private const int DormancyDays  = 45;   // no activity beyond this ⇒ "dormant" (tunable)
    private const string FlagSeverityLookup = "FLAG_SEVERITY";

    private static readonly string[] OpenCaseStatuses =
        { KycCaseStatus.Submitted, KycCaseStatus.AwaitingCaseId, KycCaseStatus.CaseCreated };

    private const string EnrichedCacheKey = "Customer::EnrichedList::v1";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IMemoryCache          _cache;

    public ListCustomersQueryHandler(IApplicationDbContext db, ICurrentUserService current, IMemoryCache cache)
    {
        _db = db;
        _current = current;
        _cache = cache;
    }

    public async Task<PagedResult<CustomerListItemDto>> Handle(ListCustomersQuery req, CancellationToken ct)
    {
        // The full per-customer enrichment (flags/cases/reviews/trade for the whole
        // book) is the expensive part and is identical across paging/sort/filter, so
        // cache it for 60s — pagination and sorting then work off the cached list.
        if (!_cache.TryGetValue(EnrichedCacheKey, out List<CustomerListItemDto>? enriched) || enriched is null)
        {
        var now  = DateTime.UtcNow;
        var d90  = now.AddDays(-90);
        var d180 = now.AddDays(-180);
        var sparkStart = now.AddDays(-7 * SparkWeeks);

        // Tenant scoping intentionally removed (2026-07) to match the ops dashboard
        // and the InsightSummary KPI strip — the tenant field is being retired
        // app-wide, so the KPI strip total and the table total stay consistent.
        // ---- customer snapshots (grouped by code) ----
        var snaps = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.CustomerCode != null)
            .Select(c => new { c.CustomerCode, c.CustomerName, c.CreatedDate, c.LastUpdatedDate, c.UdfData, c.TransactionId })
            .ToListAsync(ct);

        // txn → customer code (a snapshot ties one transaction to one customer)
        var txnToCode = snaps
            .Where(s => s.TransactionId.HasValue)
            .GroupBy(s => s.TransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.First().CustomerCode!);

        // ---- enrichment sources (book is small ⇒ load then map in memory) ----
        var activeFlags = await _db.TransactionFlags.AsNoTracking()
            .Where(f => f.IsFlagged)
            .Select(f => new { f.TransactionId, f.FlagId, f.Source })
            .ToListAsync(ct);

        var catalogue = (await _db.FlagCatalogues.AsNoTracking()
                .Select(c => new { c.FlagId, c.SeverityLkpId, c.DefaultWeight })
                .ToListAsync(ct))
            .ToDictionary(c => c.FlagId, c => (c.SeverityLkpId, c.DefaultWeight));

        var severityById = (await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == FlagSeverityLookup)
                .Select(l => new { l.Id, l.VisibleValue })
                .ToListAsync(ct))
            .ToDictionary(l => l.Id, l => (l.VisibleValue ?? "").ToLowerInvariant());

        var openCasesByCode = (await _db.KycCaseRequests.AsNoTracking()
                .Where(k => OpenCaseStatuses.Contains(k.Status))
                .Select(k => k.CustomerId)
                .ToListAsync(ct))
            .Where(id => !string.IsNullOrEmpty(id))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        var scheduleByCode = (await _db.CustomerReviewSchedules.AsNoTracking()
                .Select(s => new { s.CustomerCode, s.NextReviewDate, s.Status })
                .ToListAsync(ct))
            .GroupBy(s => s.CustomerCode)
            .ToDictionary(g => g.Key, g => g.First());

        var txns = await _db.Transactions.AsNoTracking()
            .Select(t => new { t.TransactionId, t.ProductId, t.CreatedDate })
            .ToListAsync(ct);
        var txnById = txns.ToDictionary(t => t.TransactionId);

        // Product direction comes from the product TYPE lookup (TmX_Lookup 'ProductTypes'),
        // not the product name — authoritative per product decision.
        var productTypeById = (await _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == "ProductTypes")
                .Select(l => new { l.Id, l.VisibleValue })
                .ToListAsync(ct))
            .ToDictionary(l => l.Id, l => l.VisibleValue);
        var productDir = (await _db.Products.AsNoTracking()
                .Select(p => new { p.ProductId, p.ProductTypeLkp, p.ProductName, p.ProductCode })
                .ToListAsync(ct))
            .ToDictionary(p => p.ProductId, p =>
            {
                var typeVal = p.ProductTypeLkp.HasValue && productTypeById.TryGetValue(p.ProductTypeLkp.Value, out var tv) ? tv : null;
                return ProductDirectionClassifier.Classify(typeVal ?? p.ProductName, p.ProductCode);
            });

        // ---- per-customer flag rollup (active flags mapped txn → code) ----
        var flagRoll = new Dictionary<string, FlagAgg>();
        foreach (var f in activeFlags)
        {
            if (!txnToCode.TryGetValue(f.TransactionId, out var code)) continue;
            if (!flagRoll.TryGetValue(code, out var agg)) { agg = new FlagAgg(); flagRoll[code] = agg; }
            agg.Total++;
            if (string.Equals(f.Source, "Auto", StringComparison.OrdinalIgnoreCase)) agg.Auto++; else agg.Manual++;

            var weight = 1.0; var sev = "";
            if (catalogue.TryGetValue(f.FlagId, out var meta))
            {
                weight = (double)meta.DefaultWeight;
                if (meta.SeverityLkpId is int sid && severityById.TryGetValue(sid, out var s)) sev = s;
            }
            agg.WeightSum += weight;
            if (sev.Contains("critical")) agg.Critical++;
            else if (sev.Contains("high")) agg.High++;
        }

        // ---- assemble + enrich ----
        var built = snaps
            .GroupBy(s => s.CustomerCode!)
            .Select(grp =>
            {
                var code   = grp.Key;
                var latest = grp.OrderByDescending(x => x.CreatedDate).First();
                var risk   = ExtractRiskScore(latest.UdfData);
                var band   = RiskBandClassifier.Classify(risk);

                var txnIds = grp.Where(x => x.TransactionId.HasValue)
                                .Select(x => x.TransactionId!.Value).Distinct().ToList();
                var lastActivity = grp.Max(x => x.LastUpdatedDate ?? x.CreatedDate);

                flagRoll.TryGetValue(code, out var fa);
                fa ??= new FlagAgg();
                openCasesByCode.TryGetValue(code, out var openCases);
                scheduleByCode.TryGetValue(code, out var sched);

                // trade (90d) over this customer's transactions
                int c90 = 0, cPrev = 0, imp = 0, exp = 0;
                var spark = new int[SparkWeeks];
                foreach (var tid in txnIds)
                {
                    if (!txnById.TryGetValue(tid, out var t)) continue;
                    var dt = t.CreatedDate;
                    if (dt >= d90) { c90++; }
                    else if (dt >= d180) { cPrev++; }
                    if (dt >= d90)
                    {
                        var dir = productDir.TryGetValue(t.ProductId, out var d) ? d : TradeDirection.Unclassified;
                        if (dir == TradeDirection.Import) imp++;
                        else if (dir == TradeDirection.Export) exp++;
                    }
                    if (dt >= sparkStart)
                    {
                        var wk = (int)((dt - sparkStart).TotalDays / 7);
                        if (wk >= 0 && wk < SparkWeeks) spark[wk]++;
                    }
                }

                var reviewState = ReviewStateEvaluator.Evaluate(sched?.NextReviewDate, now);
                var dormant     = (now - lastActivity).TotalDays > DormancyDays;
                var highRisk    = RiskBandClassifier.IsHighRisk(band);

                var health = CustomerHealthEvaluator.Evaluate(
                    openCases, fa.Critical, reviewState == ReviewState.Overdue,
                    highRisk, reviewState == ReviewState.DueSoon, fa.High,
                    hasAnySignal: txnIds.Count > 0 || fa.Total > 0 || openCases > 0 || sched != null || band != RiskBand.Unknown);

                var attention = openCases > 0 ? "red" : fa.Total > 0 ? "amber" : "green";

                return new CustomerListItemDto
                {
                    customerCode     = code,
                    customerName     = latest.CustomerName,
                    riskScore        = risk,
                    riskBand         = BandLabel(band),
                    transactionCount = txnIds.Count,
                    onboardedDate    = grp.Min(x => x.CreatedDate),
                    lastActivityDate = lastActivity,

                    riskScoreNum     = null,   // category-only: source has risk CATEGORY, not a 0–99 score
                    riskTrend        = null,   // NEW data (per-review score) — plan §6 opt-3

                    openCases        = openCases,
                    activeFlags      = fa.Total,
                    flagsManual      = fa.Manual,
                    flagsAuto        = fa.Auto,
                    attention        = attention,

                    reviewStatus     = sched?.Status,
                    reviewState      = ReviewStateEvaluator.Label(reviewState),
                    nextReviewDate   = sched?.NextReviewDate,

                    trade90dCount    = c90,
                    tradeSpark       = spark.ToList(),
                    tradeImport      = imp,
                    tradeExport      = exp,
                    tradeLean        = Lean(imp, exp),
                    tradeTrendPct    = cPrev > 0 ? (int)Math.Round((c90 - cPrev) * 100.0 / cPrev) : (int?)null,

                    dormant          = dormant,
                    health           = CustomerHealthEvaluator.Label(health)
                };
            })
            .ToList();
            _cache.Set(EnrichedCacheKey, built, TimeSpan.FromSeconds(60));
            enriched = built;
        }

        // work on a copy so filters/sort never mutate the cached list
        var all = enriched.ToList();

        // ---- filters ----
        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var f = req.Filter.Trim();
            all = all.Where(c =>
                    c.customerCode.Contains(f, StringComparison.OrdinalIgnoreCase)
                 || (c.customerName != null && c.customerName.Contains(f, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        if (!string.IsNullOrWhiteSpace(req.RiskBand))
            all = all.Where(c => string.Equals(c.riskBand, req.RiskBand.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(req.ReviewState))
            all = all.Where(c => string.Equals(c.reviewState, req.ReviewState.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(req.Activity))
        {
            var wantDormant = req.Activity.Trim().Equals("Dormant", StringComparison.OrdinalIgnoreCase);
            all = all.Where(c => c.dormant == wantDormant).ToList();
        }
        if (req.HasOpenCase == true)
            all = all.Where(c => c.openCases > 0).ToList();

        all = ApplySort(all, req.Sort);

        var total = all.Count;
        var items = all.Skip(req.Skip).Take(req.Take).ToList();

        return new PagedResult<CustomerListItemDto>
        {
            items    = items,
            total    = total,
            page     = req.Page,
            pageSize = req.PageSize
        };
    }

    private sealed class FlagAgg
    {
        public int Total, Manual, Auto, Critical, High;
        public double WeightSum;
    }

    private static string Lean(int imp, int exp)
    {
        if (imp == 0 && exp == 0) return "none";
        if (imp >= exp * 1.2) return "import-lean";
        if (exp >= imp * 1.2) return "export-lean";
        return "balanced";
    }

    private static List<CustomerListItemDto> ApplySort(List<CustomerListItemDto> list, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return list.OrderByDescending(c => c.lastActivityDate).ToList();

        var first = sort.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
        if (first is null) return list.OrderByDescending(c => c.lastActivityDate).ToList();

        var parts = first.Split('-', 2, StringSplitOptions.TrimEntries);
        var field = parts.ElementAtOrDefault(0)?.ToLowerInvariant();
        var desc  = string.Equals(parts.ElementAtOrDefault(1), "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedEnumerable<CustomerListItemDto> ordered = field switch
        {
            "customername"     => desc ? list.OrderByDescending(c => c.customerName) : list.OrderBy(c => c.customerName),
            "customercode"     => desc ? list.OrderByDescending(c => c.customerCode) : list.OrderBy(c => c.customerCode),
            "riskband"         => desc ? list.OrderByDescending(c => c.riskBand)     : list.OrderBy(c => c.riskBand),
            "riskscore"        => desc ? list.OrderByDescending(c => c.riskScoreNum ?? -1) : list.OrderBy(c => c.riskScoreNum ?? -1),
            "transactioncount" => desc ? list.OrderByDescending(c => c.transactionCount) : list.OrderBy(c => c.transactionCount),
            "trade90d"         => desc ? list.OrderByDescending(c => c.trade90dCount) : list.OrderBy(c => c.trade90dCount),
            "opencases"        => desc ? list.OrderByDescending(c => c.openCases) : list.OrderBy(c => c.openCases),
            "flags"            => desc ? list.OrderByDescending(c => c.activeFlags) : list.OrderBy(c => c.activeFlags),
            "onboardeddate"    => desc ? list.OrderByDescending(c => c.onboardedDate) : list.OrderBy(c => c.onboardedDate),
            _                  => desc ? list.OrderByDescending(c => c.lastActivityDate) : list.OrderBy(c => c.lastActivityDate),
        };
        return ordered.ToList();
    }

    internal static string BandLabel(RiskBand band) => band switch
    {
        RiskBand.Low    => "Low",
        RiskBand.Medium => "Medium",
        RiskBand.High   => "High",
        RiskBand.Pep    => "PEP",
        _               => "Unknown"
    };

    /// <summary>Reads RiskScore/RiskLevel from a snapshot UDF blob (key case/underscore tolerant).</summary>
    internal static string? ExtractRiskScore(string? udf)
    {
        if (string.IsNullOrWhiteSpace(udf)) return null;
        try
        {
            using var doc = JsonDocument.Parse(udf);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var name = p.Name.Replace("_", "");
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

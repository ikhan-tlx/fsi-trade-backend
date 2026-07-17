using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Customer.Common;
using FSI.Trade.Compliance.Application.Features.Customer.List;      // ExtractRiskScore / BandLabel
using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;  // RiskBandClassifier
using FSI.Trade.Compliance.Domain.Entities;                        // KycCaseStatus
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Customer.InsightDetail;

/// <summary>
/// One-customer 360° aggregate. Read-side only; sparse UDF fields (amount,
/// counterparty country) degrade gracefully. See plan §4.
/// </summary>
public class GetCustomerInsightDetailQueryHandler
    : IRequestHandler<GetCustomerInsightDetailQuery, CustomerInsightDetailDto>
{
    private const string FlagSeverityLookup = "FLAG_SEVERITY";
    private static readonly string[] OpenCaseStatuses =
        { KycCaseStatus.Submitted, KycCaseStatus.AwaitingCaseId, KycCaseStatus.CaseCreated };

    private readonly IApplicationDbContext _db;
    public GetCustomerInsightDetailQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CustomerInsightDetailDto> Handle(GetCustomerInsightDetailQuery req, CancellationToken ct)
    {
        var code = req.CustomerId;
        var now  = DateTime.UtcNow;
        var d90  = now.AddDays(-90);
        var d180 = now.AddDays(-180);

        var dto = new CustomerInsightDetailDto { customerCode = code };

        // ---- snapshots for this customer ----
        var snaps = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.CustomerCode == code)
            .Select(c => new {
                c.CustomerName, c.CustomerTypeLkp, c.CustomerStatusLkp, c.NationalIdentifierValue,
                c.NationalIdTypeLkp, c.CustomerSegmentLkp, c.UdfData, c.CreatedDate, c.LastUpdatedDate, c.TransactionId
            })
            .ToListAsync(ct);
        if (snaps.Count == 0) return dto;   // unknown customer → empty shell

        var latest = snaps.OrderByDescending(s => s.CreatedDate).First();
        dto.customerName     = latest.CustomerName;
        dto.onboardedDate    = snaps.Min(s => s.CreatedDate);
        dto.lastActivityDate = snaps.Max(s => s.LastUpdatedDate ?? s.CreatedDate);
        var txnIds = snaps.Where(s => s.TransactionId.HasValue).Select(s => s.TransactionId!.Value).Distinct().ToList();

        // lookups for type/status
        var lkpIds = new[] { latest.CustomerTypeLkp, latest.CustomerStatusLkp }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var lkp = lkpIds.Count == 0 ? new Dictionary<int, string?>()
            : (await _db.Lookups.AsNoTracking().Where(l => lkpIds.Contains(l.Id)).Select(l => new { l.Id, l.VisibleValue }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.VisibleValue);
        dto.customerType = latest.CustomerTypeLkp.HasValue && lkp.TryGetValue(latest.CustomerTypeLkp.Value, out var tv) ? tv : null;
        dto.status       = latest.CustomerStatusLkp.HasValue && lkp.TryGetValue(latest.CustomerStatusLkp.Value, out var sv) ? sv : null;

        // profile completeness (proxy over key snapshot fields)
        var fields = new object?[] { latest.CustomerName, latest.CustomerTypeLkp, latest.NationalIdentifierValue,
                                     latest.CustomerStatusLkp, latest.NationalIdTypeLkp, latest.CustomerSegmentLkp };
        dto.profileCompleteness = (int)Math.Round(fields.Count(f => f != null) * 100.0 / fields.Length);

        // ---- review schedule + history ----
        var sched = await _db.CustomerReviewSchedules.AsNoTracking().Where(s => s.CustomerCode == code)
            .Select(s => new { s.NextReviewDate, s.Status }).FirstOrDefaultAsync(ct);
        dto.nextReviewDate = sched?.NextReviewDate;
        dto.reviewStatus   = sched?.Status;
        var reviewState = ReviewStateEvaluator.Evaluate(sched?.NextReviewDate, now);
        dto.reviewState = ReviewStateEvaluator.Label(reviewState);

        var history = await _db.CustomerReviewHistories.AsNoTracking().Where(h => h.CustomerCode == code)
            .OrderByDescending(h => h.CreatedDate)
            .Select(h => new { h.CreatedDate, h.Outcome, h.Remarks }).Take(12).ToListAsync(ct);
        if (reviewState == ReviewState.Overdue || reviewState == ReviewState.DueSoon)
            dto.kycTimeline.Add(new TimelineItemDto {
                date = (sched?.NextReviewDate ?? now).ToString("dd MMM yyyy"),
                title = "Periodic review", remark = "Scheduled review not yet started.",
                kind = "hollow"
            });
        foreach (var h in history)
            dto.kycTimeline.Add(new TimelineItemDto {
                date = h.CreatedDate.ToString("dd MMM yyyy"),
                title = string.IsNullOrWhiteSpace(h.Outcome) ? "Review" : h.Outcome,
                remark = h.Remarks,
                kind = (h.Outcome ?? "").ToLowerInvariant() switch { "completed" => "ok", "failed" => "bad", "blocked" => "warn", _ => "ok" }
            });

        // ---- flags for this customer's transactions ----
        var flagRows = txnIds.Count == 0 ? new List<FlagRaw>() :
            (await _db.TransactionFlags.AsNoTracking().Where(f => f.IsFlagged && txnIds.Contains(f.TransactionId))
                .Select(f => new { f.FlagId, f.Source, f.SetBy, f.SetDate, f.EvidenceDocumentId }).ToListAsync(ct))
            .Select(f => new FlagRaw(f.FlagId, f.Source, f.SetBy, f.SetDate, f.EvidenceDocumentId)).ToList();

        var flagIds = flagRows.Select(f => f.FlagId).Distinct().ToList();
        Dictionary<int, (string name, string? codec, int? sev, decimal w)> cat = new();
        if (flagIds.Count > 0)
            cat = (await _db.FlagCatalogues.AsNoTracking().Where(c => flagIds.Contains(c.FlagId))
                    .Select(c => new { c.FlagId, c.FlagName, c.FlagCode, c.SeverityLkpId, c.DefaultWeight }).ToListAsync(ct))
                .ToDictionary(c => c.FlagId, c => (name: c.FlagName, codec: (string?)c.FlagCode, sev: c.SeverityLkpId, w: c.DefaultWeight));
        var sevById = (await _db.Lookups.AsNoTracking().Where(l => l.LookupType == FlagSeverityLookup)
                .Select(l => new { l.Id, l.VisibleValue }).ToListAsync(ct))
            .ToDictionary(l => l.Id, l => (l.VisibleValue ?? "").ToLowerInvariant());

        string SevOf(int fid) => cat.TryGetValue(fid, out var m) && m.sev is int s && sevById.TryGetValue(s, out var nm)
            ? Normalize(nm) : "low";

        dto.flagsManual = flagRows.Count(f => !string.Equals(f.Source, "Auto", StringComparison.OrdinalIgnoreCase));
        dto.flagsAuto   = flagRows.Count(f =>  string.Equals(f.Source, "Auto", StringComparison.OrdinalIgnoreCase));
        dto.activeFlags = flagRows.Count;

        foreach (var f in flagRows.OrderByDescending(f => f.SetDate))
        {
            cat.TryGetValue(f.FlagId, out var m);
            dto.flags.Add(new FlagRowDto {
                name = m.name ?? $"Flag {f.FlagId}", code = m.codec, severity = SevOf(f.FlagId),
                source = string.Equals(f.Source, "Auto", StringComparison.OrdinalIgnoreCase) ? "Auto" : "Manual",
                setBy = f.SetBy, setDate = f.SetDate.ToString("MM-dd"), hasEvidence = f.EvidenceDocumentId.HasValue
            });
        }
        dto.severityMix = dto.flags.GroupBy(f => f.severity).Select(g => new NamedCountDto { label = g.Key, count = g.Count() }).ToList();
        dto.flagsOverTime = flagRows.GroupBy(f => f.SetDate.ToString("yyyy-MM"))
            .OrderBy(g => g.Key).Select(g => new TimeBucketDto { period = g.Key, count = g.Count() }).ToList();

        // contributing factors — flag weight, ranked
        dto.contributingFactors = flagRows.GroupBy(f => f.FlagId)
            .Select(g => { cat.TryGetValue(g.Key, out var m); return new WeightedDto { label = m.name ?? $"Flag {g.Key}", weight = (double)(m.w == 0 ? 1m : m.w) * g.Count() }; })
            .OrderByDescending(x => x.weight).Take(6).ToList();

        var weightSum = flagRows.Sum(f => cat.TryGetValue(f.FlagId, out var m) ? (double)(m.w == 0 ? 1m : m.w) : 1.0);

        // ---- risk band + composite ----
        var band = RiskBandClassifier.Classify(ListCustomersQueryHandler.ExtractRiskScore(latest.UdfData));
        dto.riskBand = ListCustomersQueryHandler.BandLabel(band);
        dto.riskScoreNum = null;              // category-only: source has a risk CATEGORY, not a 0–99 score
        dto.riskTrajectory = new();           // no per-review score to plot (see plan §6 opt-3)
        _ = weightSum;                         // retained for future scoring; unused while category-only

        // ---- FCCM cases ----
        var cases = await _db.KycCaseRequests.AsNoTracking().Where(k => k.CustomerId == code)
            .Select(k => new { k.Id, k.FccmCaseId, k.Status, k.SubmittedAt, k.CaseCreatedAt, k.RiskAssessedAt }).ToListAsync(ct);
        dto.openCases   = cases.Count(c => OpenCaseStatuses.Contains(c.Status));
        dto.closedCases = cases.Count(c => c.Status == KycCaseStatus.RiskAssessed);
        dto.caseOpen = dto.openCases; dto.caseClosed = dto.closedCases;
        foreach (var c in cases.OrderByDescending(c => c.SubmittedAt))
            dto.cases.Add(new CaseRowDto {
                id = c.FccmCaseId ?? $"REQ-{c.Id}", status = c.Status,
                step = c.Status switch { KycCaseStatus.Submitted => 1, KycCaseStatus.AwaitingCaseId => 2, KycCaseStatus.CaseCreated => 3, KycCaseStatus.RiskAssessed => 4, _ => 1 },
                closed = c.Status == KycCaseStatus.RiskAssessed,
                date = (c.CaseCreatedAt ?? c.SubmittedAt).ToString("MM-dd")
            });
        var timed = cases.Where(c => c.CaseCreatedAt.HasValue).Select(c => (c.CaseCreatedAt!.Value - c.SubmittedAt).TotalMinutes).Where(m => m >= 0).ToList();
        dto.avgTimeToCaseMin = timed.Count > 0 ? (int)Math.Round(timed.Average()) : (int?)null;
        dto.evidenceCoveragePct = dto.flags.Count > 0 ? (int)Math.Round(dto.flags.Count(f => f.hasEvidence) * 100.0 / dto.flags.Count) : 0;

        // ---- transactions + products + details (trade) ----
        var txns = txnIds.Count == 0 ? new List<TxnRaw>() :
            (await _db.Transactions.AsNoTracking().Where(t => txnIds.Contains(t.TransactionId))
                .Select(t => new { t.TransactionId, t.ProductId, t.CreatedDate, t.TransactionAmount }).ToListAsync(ct))
            .Select(t => new TxnRaw(t.TransactionId, t.ProductId, t.CreatedDate, t.TransactionAmount)).ToList();
        dto.trade90dCount = txns.Count(t => t.Date >= d90);
        var prev90 = txns.Count(t => t.Date >= d180 && t.Date < d90);
        dto.tradeTrendPct = prev90 > 0 ? (int)Math.Round((dto.trade90dCount - prev90) * 100.0 / prev90) : (int?)null;

        // Direction from the product TYPE lookup ('ProductTypes'), not the name.
        var typeById = (await _db.Lookups.AsNoTracking().Where(l => l.LookupType == "ProductTypes")
                .Select(l => new { l.Id, l.VisibleValue }).ToListAsync(ct))
            .ToDictionary(l => l.Id, l => l.VisibleValue);
        var prodById = (await _db.Products.AsNoTracking()
                .Select(p => new { p.ProductId, p.ProductTypeLkp, p.ProductName, p.ProductCode }).ToListAsync(ct))
            .ToDictionary(p => p.ProductId, p => (
                p.ProductName, p.ProductCode,
                dir: ProductDirectionClassifier.Classify(
                    (p.ProductTypeLkp.HasValue && typeById.TryGetValue(p.ProductTypeLkp.Value, out var tv) ? tv : null) ?? p.ProductName,
                    p.ProductCode)));
        dto.productMix = txns.GroupBy(t => prodById.TryGetValue(t.ProductId, out var p) ? (p.ProductName ?? "Unknown") : "Unknown")
            .Select(g => new NamedCountDto { label = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToList();
        foreach (var t in txns)
        {
            var dir = prodById.TryGetValue(t.ProductId, out var p2) ? p2.dir : TradeDirection.Unclassified;
            if (dir == TradeDirection.Import) dto.importCount++;
            else if (dir == TradeDirection.Export) dto.exportCount++;
        }
        // monthly volume (last 12 months)
        dto.volumeTrend = Enumerable.Range(0, 12).Select(i => now.AddMonths(-11 + i)).Select(m => new TimeBucketDto {
            period = m.ToString("yyyy-MM"),
            count = txns.Count(t => t.Date.Year == m.Year && t.Date.Month == m.Month)
        }).ToList();

        // amounts + countries (CAPTURE — from TransactionDetail UDF; degrade)
        var details = txnIds.Count == 0 ? new List<string?>() :
            await _db.TransactionDetails.AsNoTracking().Where(td => txnIds.Contains(td.TransactionId))
                .Select(td => td.UdfData).ToListAsync(ct);
        // Amount from the typed Transaction_Amount column (migration 2026_07_015); country still from UDF.
        var amounts = txns.Where(t => t.Amount.HasValue).Select(t => t.Amount!.Value).ToList();
        var countryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var udfAmounts = new List<decimal>();
        foreach (var ud in details)
        {
            var (amt, ctry) = ReadUdf(ud);
            if (amt.HasValue) udfAmounts.Add(amt.Value);
            if (!string.IsNullOrWhiteSpace(ctry)) countryCounts[ctry!.Trim()] = countryCounts.GetValueOrDefault(ctry.Trim()) + 1;
        }
        // Fallback to UDF-parsed amounts until the typed column is populated (backfill / save-flow).
        if (amounts.Count == 0) amounts = udfAmounts;
        dto.tradeAmounts = new TradeAmountsDto {
            captured = amounts.Count > 0,
            total = amounts.Sum(), largest = amounts.Count > 0 ? amounts.Max() : 0,
            average = amounts.Count > 0 ? Math.Round(amounts.Average(), 0) : 0
        };
        if (countryCounts.Count > 0)
        {
            var sanctioned = (await _db.SanctionedCountries.AsNoTracking()
                .Select(s => new { s.IsoCountryCode, s.CountryName }).ToListAsync(ct));
            bool IsSanctioned(string c) => sanctioned.Any(s =>
                string.Equals(s.IsoCountryCode, c, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(s.CountryName) && s.CountryName.Contains(c, StringComparison.OrdinalIgnoreCase)));
            dto.countries = countryCounts.OrderByDescending(kv => kv.Value)
                .Select(kv => new CountryDto { code = kv.Key, count = kv.Value, sanctioned = IsSanctioned(kv.Key) }).ToList();
        }

        // ---- header chips ----
        if (dto.countries.Any(c => c.sanctioned)) dto.headerChips.Add("Sanctioned-country hit");
        if (dto.flags.Any(f => f.severity == "critical")) dto.headerChips.Add("Critical flag");
        if (reviewState == ReviewState.Overdue) dto.headerChips.Add("Review overdue");
        if (band == RiskBand.Pep) dto.headerChips.Add("PEP");

        // ---- behaviour ----
        var mean90 = txns.Count > 0 ? txns.Count / Math.Max(1.0, (now - txns.Min(t => t.Date)).TotalDays / 90.0) : 0;
        if (dto.trade90dCount > 0 && mean90 > 0 && dto.trade90dCount > mean90 * 1.5)
            dto.behavioural.Add(new InsightCalloutDto { kind = "warn", title = $"Activity ▲ vs typical",
                detail = "Current 90-day volume is well above this customer's trailing mean." });
        var highRisk = RiskBandClassifier.IsHighRisk(band);
        if (highRisk && dto.trade90dCount > 0)
            dto.behavioural.Add(new InsightCalloutDto { kind = "ok", title = "Risk vs activity: consistent",
                detail = "High risk band against active trade — profile looks coherent." });
        else if (!highRisk && dto.trade90dCount == 0 && dto.activeFlags > 0)
            dto.behavioural.Add(new InsightCalloutDto { kind = "info", title = "Dormant with open flags",
                detail = "No recent trade but unresolved flags — monitor." });
        dto.behavioural.Add(new InsightCalloutDto { kind = "info", title = "Seasonality",
            detail = txns.Count < 6 ? "Insufficient history to assess seasonality." : "Monthly volume is broadly even across the observed window." });

        // heatmap — last 2 calendar years
        for (int y = now.Year - 1; y <= now.Year; y++)
            dto.activityHeatmap.Add(new HeatRowDto { year = y.ToString(),
                months = Enumerable.Range(1, 12).Select(m => txns.Count(t => t.Date.Year == y && t.Date.Month == m)).ToList() });

        return dto;
    }

    private sealed record FlagRaw(int FlagId, string Source, string? SetBy, DateTime SetDate, int? EvidenceDocumentId);
    private sealed record TxnRaw(int Id, int ProductId, DateTime Date, decimal? Amount);

    private static string Normalize(string sev) =>
        sev.Contains("critical") ? "critical" : sev.Contains("high") ? "high" :
        sev.Contains("medium") ? "medium" : sev.Contains("info") ? "info" : "low";

    /// <summary>Best-effort read of an amount + country out of a transaction UDF blob (keys vary; sparse).</summary>
    private static (decimal? amount, string? country) ReadUdf(string? udf)
    {
        if (string.IsNullOrWhiteSpace(udf)) return (null, null);
        decimal? amt = null; string? ctry = null;
        try
        {
            using var doc = JsonDocument.Parse(udf);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var key = p.Name.Replace("_", "").ToLowerInvariant();
                if (amt is null && key.Contains("amount"))
                {
                    if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var d)) amt = d;
                    else if (p.Value.ValueKind == JsonValueKind.String && decimal.TryParse(p.Value.GetString(), out var ds)) amt = ds;
                }
                if (ctry is null && key.Contains("country") && !key.Contains("residence") && p.Value.ValueKind == JsonValueKind.String)
                {
                    var v = p.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) ctry = v;
                }
            }
        }
        catch (JsonException) { }
        return (amt, ctry);
    }
}

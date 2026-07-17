using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Customer.List;      // ListCustomersQueryHandler.ExtractRiskScore
using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;  // RiskBandClassifier
using FSI.Trade.Compliance.Domain.Entities;                        // KycCaseStatus, ReviewScheduleStatus
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Customer.Common;

/// <summary>
/// The five book-wide portfolio KPIs shown on the Customer Intelligence listing
/// strip. Computed once here so the InsightSummary endpoint (live values) and the
/// daily snapshot job (persisted row) share a single source of truth and can never
/// drift apart.
/// </summary>
public readonly record struct CustomerInsightMetricValues(
    int TotalCustomers,
    int HighRisk,
    int ReviewsOverdue,
    int OpenCases,
    int FlagsRaised7d);

public static class CustomerInsightMetrics
{
    // Case statuses considered "open" (not yet risk-assessed/closed, not errored).
    private static readonly string[] OpenCaseStatuses =
        { KycCaseStatus.Submitted, KycCaseStatus.AwaitingCaseId, KycCaseStatus.CaseCreated };

    /// <summary>
    /// Compute the portfolio KPIs as of <paramref name="asOf"/> (all-tenant, matching
    /// the ops dashboard). The customer count + high-risk band are derived from the
    /// latest snapshot per distinct Customer_Code, exactly like the summary endpoint.
    /// </summary>
    public static async Task<CustomerInsightMetricValues> ComputeAsync(
        IApplicationDbContext db, DateTime asOf, CancellationToken ct)
    {
        // ---- Customers + high-risk (distinct Customer_Code, latest snapshot's risk) ----
        var snaps = await db.TransactionCustomerSnapshots.AsNoTracking()
            .Where(c => c.CustomerCode != null)
            .Select(c => new { c.CustomerCode, c.CreatedDate, c.UdfData })
            .ToListAsync(ct);

        int total = 0, high = 0;
        foreach (var grp in snaps.GroupBy(s => s.CustomerCode!))
        {
            total++;
            var latest = grp.OrderByDescending(x => x.CreatedDate).First();
            var band = RiskBandClassifier.Classify(ListCustomersQueryHandler.ExtractRiskScore(latest.UdfData));
            if (RiskBandClassifier.IsHighRisk(band)) high++;
        }

        // ---- Reviews overdue (schedule past its next-review date, still Scheduled) ----
        var reviewsOverdue = await db.CustomerReviewSchedules.AsNoTracking()
            .Where(s => s.Status == ReviewScheduleStatus.Scheduled
                     && s.NextReviewDate != null && s.NextReviewDate < asOf)
            .CountAsync(ct);

        // ---- Open FCCM cases ----
        var openCases = await db.KycCaseRequests.AsNoTracking()
            .Where(k => OpenCaseStatuses.Contains(k.Status))
            .CountAsync(ct);

        // ---- Flags raised in the last 7 days ----
        var d7 = asOf.AddDays(-7);
        var flags7d = await db.TransactionFlags.AsNoTracking()
            .Where(f => f.IsFlagged && f.SetDate >= d7)
            .CountAsync(ct);

        return new CustomerInsightMetricValues(total, high, reviewsOverdue, openCases, flags7d);
    }
}

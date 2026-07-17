namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Customer_Insight_Daily — one book-wide row PER DAY
/// capturing the portfolio KPIs that the Customer Intelligence listing strip shows
/// (total customers, high-risk, reviews-overdue, open cases, flags-7d).
///
/// A tiny daily <c>CustomerInsightSnapshotService</c> upserts today's row; the
/// InsightSummary endpoint then reads the row from ~7 days ago to compute REAL
/// "vs last week" deltas instead of omitting them (plan §6 opt-1). Additive and
/// isolated — no existing table is touched.
/// </summary>
public class CustomerInsightDaily
{
    public long     Id             { get; set; }
    /// <summary>The calendar day (UTC, date-only) this snapshot represents. Unique.</summary>
    public DateTime SnapshotDate   { get; set; }
    public int      TotalCustomers { get; set; }
    public int      HighRisk       { get; set; }   // High + PEP
    public int      ReviewsOverdue { get; set; }
    public int      OpenCases      { get; set; }
    public int      FlagsRaised7d  { get; set; }
    public string   CreatedBy      { get; set; } = "CUSTOMER_INSIGHT_SNAPSHOT";
    public DateTime CreatedDate    { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

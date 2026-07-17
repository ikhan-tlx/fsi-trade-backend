namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Periodic Customer Review tunables. Bound from the <c>PeriodicReview</c>
/// section in appsettings.json via <c>IOptionsMonitor</c> so frequency edits
/// hot-reload without a restart. See PERIODIC_CUSTOMER_REVIEW_PLAN.md.
/// </summary>
public class PeriodicReviewOptions
{
    public const string SectionName = "PeriodicReview";

    public bool     Enabled        { get; set; } = true;
    public TimeSpan ScanInterval   { get; set; } = TimeSpan.FromHours(1);
    public int      BatchSize      { get; set; } = 50;
    public string   ReviewerUserId { get; set; } = "PERIODIC_REVIEWER";
    public int      LeadTimeDays   { get; set; } = 0;

    /// <summary>Hours after which a schedule stuck in InReview (its re-KYC never reached a
    /// terminal decision to heal it) is auto-reconciled back to Scheduled and retried.</summary>
    public int      StaleInReviewHours { get; set; } = 72;

    public ReviewFrequency DefaultFrequency { get; set; } = new() { Value = 3, Unit = "Months" };

    /// <summary>Per-risk-band cadence, keyed by band label (PEP/High/Medium/Low).</summary>
    public Dictionary<string, ReviewFrequency> Frequencies { get; set; } = new();
}

/// <summary>A cadence: <c>Value</c> units of <c>Unit</c> (Days/Weeks/Months/Years).</summary>
public class ReviewFrequency
{
    public int    Value { get; set; }
    public string Unit  { get; set; } = "Months";
}

namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Customer Intelligence daily-snapshot tunables. Bound from the
/// <c>CustomerInsightSnapshot</c> section in appsettings.json via
/// <c>IOptionsMonitor</c> so edits hot-reload without a restart.
///
/// The service upserts today's book-wide KPI row on each tick (idempotent), so the
/// portfolio strip can show real "vs last week" deltas (plan §6 opt-1).
/// </summary>
public class CustomerInsightSnapshotOptions
{
    public const string SectionName = "CustomerInsightSnapshot";

    public bool Enabled { get; set; } = true;

    /// <summary>How often to re-compute + upsert today's row. Sub-daily by default so a
    /// row always exists shortly after startup and stays fresh through the day; the
    /// upsert keys on the UTC date, so extra ticks only refresh today's single row.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);

    public string WriterUserId { get; set; } = "CUSTOMER_INSIGHT_SNAPSHOT";
}

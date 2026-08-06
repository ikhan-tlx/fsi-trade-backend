namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// SLA / deadline NOTIFICATION scanner tunables. Bound from the <c>Sla</c>
/// section via <c>IOptionsMonitor</c> so edits hot-reload without a restart —
/// and because the DB→IConfiguration bridge layers the
/// <c>TmX_Configuration_Value</c> rows (Sla.Enabled / Sla.DefaultHours /
/// Sla.AtRiskPercent) over the same section, these values are tunable from
/// /admin/configuration and reload live.
///
/// The scan is a best-effort emitter over the existing notification infra
/// (no new table): a background pass finds open <c>WorkflowInbox</c> work
/// items whose SLA clock has crossed the at-risk or breach line and raises
/// <c>sla.atrisk</c> / <c>sla.breached</c> notifications.
/// </summary>
public class SlaOptions
{
    public const string SectionName = "Sla";

    /// <summary>Master on/off for the SLA scanner (hot-reload).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the scanner runs a pass.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The SLA window, in hours, from the clock start (entered-current-state
    /// time, falling back to the transaction's created date).</summary>
    public int DefaultHours { get; set; } = 24;

    /// <summary>Raise the at-risk notification once elapsed time reaches this
    /// percentage of the SLA window (e.g. 80 = at 80% of DefaultHours).</summary>
    public int AtRiskPercent { get; set; } = 80;

    /// <summary>Let the app settle before the first scan pass.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Staleness bound for breach notifications, in hours. A breach whose
    /// <c>dueAt</c> is more than this many hours in the past is treated as
    /// stale/abandoned (not actionable) and is skipped entirely — no
    /// <c>sla.breached</c> notification is raised. Guards against flooding on
    /// ancient never-completed work items. Default 168h (7 days).</summary>
    public int MaxBreachAgeHours { get; set; } = 168;

    /// <summary>When true, a breach also notifies the <c>Requests.Approve</c>
    /// escalation group (approvers/supervisors) in addition to the assignees.
    /// When false (default), a breach notifies only the assignees actually
    /// holding the item — no approver blast.</summary>
    public bool EscalateToApprovers { get; set; } = false;
}

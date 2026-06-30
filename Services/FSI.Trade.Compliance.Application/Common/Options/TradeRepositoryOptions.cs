namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// Trade Repository listing tuning. Bound from the <c>TradeRepository</c>
/// section in appsettings.json.
///
/// Today it carries only the SLA used to compute a row's "aging" badge on
/// the redesigned grid (Option A). There is no SLA-config UI yet — the
/// value is operator-tuned in appsettings and surfaced to the FE via the
/// stats endpoint so the client can colour the aging badge (amber as it
/// nears the limit, red once over). A natural home for future listing
/// toggles (default density, KPI card set, etc).
///
///   "TradeRepository": { "SlaDays": 5 }
/// </summary>
public class TradeRepositoryOptions
{
    public const string SectionName = "TradeRepository";

    /// <summary>
    /// Number of calendar days a transaction may sit (measured from its
    /// Transaction_Date) before it is considered overdue. Drives the grid's
    /// aging badge. Defaults to 5 when the section is absent.
    /// </summary>
    public int SlaDays { get; set; } = 5;
}

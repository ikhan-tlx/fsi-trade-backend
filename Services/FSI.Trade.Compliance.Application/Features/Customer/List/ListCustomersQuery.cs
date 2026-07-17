using FSI.Trade.Compliance.Application.Common.Models;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.List;

/// <summary>
/// Paged customer listing for the "Customer Insight" page
/// (<c>GET /api/v1/Customer?page=…&amp;pageSize=…&amp;sort=…&amp;filter=…</c>).
///
/// Source is <c>TmX_Customer_Master</c> (per-transaction snapshots), grouped
/// distinct by <c>Customer_Code</c> — the same source the dashboard uses for
/// its customer KPIs. Because risk lives in the snapshot UDF JSON, the group +
/// risk classification is done in memory (the book is a few hundred customers).
/// </summary>
public class ListCustomersQuery : PagedQuery, IRequest<PagedResult<CustomerListItemDto>>
{
    // ---- Customer-Intelligence facet filters (all optional; applied after enrichment) ----
    /// <summary>Risk band: High | Medium | Low | PEP (case-insensitive). Null = all.</summary>
    public string? RiskBand    { get; set; }
    /// <summary>Review urgency: Overdue | DueSoon | OK. Null = all.</summary>
    public string? ReviewState { get; set; }
    /// <summary>Activity: Active | Dormant. Null = all.</summary>
    public string? Activity    { get; set; }
    /// <summary>When true, only customers with ≥1 open FCCM case.</summary>
    public bool?   HasOpenCase { get; set; }
}

/// <summary>
/// One row of the Customer Intelligence listing. The first block is the original
/// (unchanged) contract; everything below it is additive enrichment for the
/// redesigned triage table (attention, KYC, trade, flags, health). Additive
/// fields are read structurally on the FE, so this stays backward-compatible.
/// See CUSTOMER_INSIGHT_BACKEND_CHANGE_PLAN §3.
/// </summary>
public class CustomerListItemDto
{
    // ---- original contract ----
    public string    customerCode      { get; set; } = "";
    public string?   customerName      { get; set; }
    public string?   riskScore         { get; set; }          // raw UDF value (band text / code)
    public string    riskBand          { get; set; } = "Unknown";
    public int       transactionCount  { get; set; }
    public DateTime? lastActivityDate  { get; set; }
    public DateTime  onboardedDate     { get; set; }

    // ---- risk ----
    /// <summary>Derived composite 0–99 (band + active-flag weight). Null = not assessed.</summary>
    public int?      riskScoreNum      { get; set; }
    /// <summary>Per-review score delta. Null until Risk_Score_At_Review is persisted (plan §6 opt-3).</summary>
    public int?      riskTrend         { get; set; }

    // ---- attention (cases + flags) ----
    public int       openCases         { get; set; }
    public int       activeFlags       { get; set; }
    public int       flagsManual       { get; set; }
    public int       flagsAuto         { get; set; }
    /// <summary>red | amber | green — cases → red, flags-only → amber, clear → green.</summary>
    public string    attention         { get; set; } = "green";

    // ---- KYC review ----
    public string?   reviewStatus      { get; set; }           // schedule Status (Scheduled/InReview/Blocked/...)
    public string    reviewState       { get; set; } = "None"; // Overdue | DueSoon | OK | None
    public DateTime? nextReviewDate    { get; set; }

    // ---- trade (90d) ----
    public int       trade90dCount     { get; set; }
    public List<int> tradeSpark        { get; set; } = new();  // weekly buckets (≈12)
    public int       tradeImport       { get; set; }
    public int       tradeExport       { get; set; }
    /// <summary>Import vs export lean: import-lean | export-lean | balanced | none.</summary>
    public string    tradeLean         { get; set; } = "none";
    /// <summary>% change vs prior 90d. Null = no comparable prior data ("no change").</summary>
    public int?      tradeTrendPct     { get; set; }

    // ---- activity / health ----
    public bool      dormant           { get; set; }
    /// <summary>red | amber | green | grey composite triage health.</summary>
    public string    health            { get; set; } = "grey";
}

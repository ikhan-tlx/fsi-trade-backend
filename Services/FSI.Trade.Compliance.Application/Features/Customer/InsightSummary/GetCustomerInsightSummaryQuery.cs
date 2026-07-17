using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.InsightSummary;

/// <summary>
/// Portfolio-level "Customer Intelligence" summary — the 5-card KPI strip on the
/// listing page (Customers · High-risk · Reviews overdue · Open cases · Flags 7d).
/// Book-wide aggregate (all customers), short-cached like the ops dashboard.
///
/// <c>GET /api/v1/Customer/InsightSummary</c>
/// </summary>
public class GetCustomerInsightSummaryQuery : IRequest<CustomerInsightSummaryDto>
{
}

public class CustomerInsightSummaryDto
{
    public int totalCustomers  { get; set; }
    public int highRisk        { get; set; }   // High + PEP
    public int reviewsOverdue  { get; set; }
    public int openCases       { get; set; }
    public int flagsRaised7d   { get; set; }

    /// <summary>
    /// Flags-7d has a real prior-window delta (this 7d vs previous 7d).
    /// The other four deltas need a daily metrics snapshot that does not exist
    /// yet (see CUSTOMER_INSIGHT_BACKEND_CHANGE_PLAN §6 opt-1) — they stay null
    /// so the UI omits the "vs last week" line rather than fabricating it.
    /// </summary>
    public int? flagsRaised7dDelta   { get; set; }
    public int? totalCustomersDelta  { get; set; }
    public int? highRiskDelta        { get; set; }
    public int? reviewsOverdueDelta  { get; set; }
    public int? openCasesDelta       { get; set; }

    public DateTime asOf { get; set; }
}

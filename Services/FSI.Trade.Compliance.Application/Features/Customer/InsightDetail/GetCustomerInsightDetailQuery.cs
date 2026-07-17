using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.InsightDetail;

/// <summary>
/// Full 360° detail aggregate for one customer — backs the redesigned Customer
/// Intelligence detail page (header gauge, 6 KPIs, and the Risk&amp;KYC / Trade /
/// Alerts&amp;Flags / Behaviour tabs). Everything is read-side aggregation over
/// existing tables; sparse fields (trade amounts, counterparty country) degrade
/// gracefully. See CUSTOMER_INSIGHT_BACKEND_CHANGE_PLAN §4.
///
/// <c>GET /api/v1/Customer/{customerId}/InsightDetail</c>
/// </summary>
public class GetCustomerInsightDetailQuery : IRequest<CustomerInsightDetailDto>
{
    public string CustomerId { get; init; } = "";
    public GetCustomerInsightDetailQuery(string customerId) => CustomerId = customerId;
}

public class NamedCountDto { public string label { get; set; } = ""; public int count { get; set; } }
public class WeightedDto  { public string label { get; set; } = ""; public double weight { get; set; } }
public class TimeBucketDto { public string period { get; set; } = ""; public int count { get; set; } }

public class TimelineItemDto
{
    public string  date    { get; set; } = "";
    public string  title   { get; set; } = "";
    public string? remark  { get; set; }
    public string  kind    { get; set; } = "ok";   // ok | warn | bad | hollow
    public int?    scoreDelta { get; set; }         // null until per-review score persisted
}

public class FlagRowDto
{
    public string   name       { get; set; } = "";
    public string?  code       { get; set; }
    public string   severity   { get; set; } = "low";   // critical|high|medium|low|info
    public string   source     { get; set; } = "Manual"; // Manual|Auto
    public string?  setBy      { get; set; }
    public string?  setDate    { get; set; }
    public bool     hasEvidence{ get; set; }
}

public class CaseRowDto
{
    public string   id        { get; set; } = "";
    public string   status    { get; set; } = "";
    public int      step      { get; set; }     // 0..3 lifecycle position
    public bool     closed    { get; set; }
    public string?  date      { get; set; }
}

public class CountryDto { public string code { get; set; } = ""; public int count { get; set; } public bool sanctioned { get; set; } }

public class InsightCalloutDto
{
    public string kind   { get; set; } = "info";  // warn | ok | info | danger
    public string title  { get; set; } = "";
    public string detail { get; set; } = "";
}

public class HeatRowDto { public string year { get; set; } = ""; public List<int> months { get; set; } = new(); }

public class TradeAmountsDto
{
    public decimal total   { get; set; }
    public decimal largest { get; set; }
    public decimal average { get; set; }
    public bool    captured { get; set; }   // false when no amounts were entered (degrade UI)
}

public class CustomerInsightDetailDto
{
    // identity / header
    public string   customerCode   { get; set; } = "";
    public string?  customerName   { get; set; }
    public string?  customerType   { get; set; }
    public string?  branchName     { get; set; }
    public string?  status         { get; set; }
    public DateTime? onboardedDate { get; set; }
    public DateTime? lastActivityDate { get; set; }

    public string   riskBand       { get; set; } = "Unknown";
    public int?     riskScoreNum   { get; set; }
    public int?     riskTrend      { get; set; }   // null (per-review score not persisted)
    public List<string> headerChips { get; set; } = new();
    public int      profileCompleteness { get; set; }

    // KPI row
    public DateTime? nextReviewDate { get; set; }
    public string    reviewState    { get; set; } = "None";
    public string?   reviewStatus   { get; set; }
    public int openCases { get; set; }
    public int closedCases { get; set; }
    public int activeFlags { get; set; }
    public int flagsManual { get; set; }
    public int flagsAuto { get; set; }
    public int trade90dCount { get; set; }
    public int? tradeTrendPct { get; set; }

    // Risk & KYC
    public List<TimelineItemDto> kycTimeline { get; set; } = new();
    public List<TimeBucketDto>   riskTrajectory { get; set; } = new();   // single current point (NEW data caveat)
    public List<WeightedDto>     contributingFactors { get; set; } = new();

    // Trade
    public List<TimeBucketDto> volumeTrend { get; set; } = new();
    public TradeAmountsDto     tradeAmounts { get; set; } = new();
    public List<NamedCountDto> productMix { get; set; } = new();
    public int importCount { get; set; }
    public int exportCount { get; set; }
    public List<CountryDto> countries { get; set; } = new();

    // Alerts & Flags
    public List<FlagRowDto> flags { get; set; } = new();
    public List<NamedCountDto> severityMix { get; set; } = new();
    public List<TimeBucketDto> flagsOverTime { get; set; } = new();
    public List<CaseRowDto> cases { get; set; } = new();
    public int caseOpen { get; set; }
    public int caseClosed { get; set; }
    public int? avgTimeToCaseMin { get; set; }
    public int evidenceCoveragePct { get; set; }

    // Behaviour
    public List<InsightCalloutDto> behavioural { get; set; } = new();
    public List<HeatRowDto> activityHeatmap { get; set; } = new();
}

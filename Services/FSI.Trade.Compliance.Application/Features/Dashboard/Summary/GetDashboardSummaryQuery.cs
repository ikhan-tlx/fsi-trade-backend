using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Dashboard.Summary;

/// <summary>
/// Operations-dashboard aggregate (BE_revamp_Changes.md 2026-06-03).
/// <c>GET /api/v1/Dashboard/Summary?range=7d|30d|QTD|YTD</c>. Tenant-scoped.
///
/// PHASE 1 (this build) returns only what the current model supports from real
/// data: customer KPIs, risk-mix (from the customer-snapshot UDF RiskScore),
/// and instruments-by-type. Monetary exposure (tradeExposureUsd / exposureTrend
/// / countryExposure), EDD and FATF are returned null/empty — those need
/// dedicated data modeling (an exposure amount + FX source, an EDD flag, a FATF
/// list). Deltas that require point-in-time history are null. The FE renders
/// empty states for missing pieces.
/// </summary>
public record GetDashboardSummaryQuery(string? Range) : IRequest<DashboardSummaryDto>;

public class DashboardSummaryDto
{
    public string                     range              { get; set; } = "30d";
    public DateTime                   asOf               { get; set; }
    public DashboardKpisDto           kpis               { get; set; } = new();
    public ExposureTrendDto?          exposureTrend      { get; set; }       // Phase 2
    public RiskMixDto                 riskMix            { get; set; } = new();
    public List<InstrumentTypeDto>    instrumentsByType  { get; set; } = new();
    public List<CountryExposureDto>   countryExposure    { get; set; } = new();   // Phase 2 → empty
}

public class DashboardKpisDto
{
    public KpiActiveCustomersDto   activeCustomers   { get; set; } = new();
    public KpiActiveInstrumentsDto activeInstruments { get; set; } = new();
    public KpiTradeExposureDto     tradeExposureUsd  { get; set; } = new();   // Phase 2 → null value
    public KpiHighRiskDto          highRiskCustomers { get; set; } = new();
}

public class KpiActiveCustomersDto
{
    public int  value     { get; set; }
    public int? onboarded { get; set; }   // distinct customers first seen within range
}

public class KpiActiveInstrumentsDto
{
    public int     value    { get; set; }
    public double? deltaPct { get; set; }   // null in Phase 1 (needs point-in-time history)
    public string? mix      { get; set; }   // null in Phase 1
}

public class KpiTradeExposureDto
{
    public long? value    { get; set; }   // raw USD; null in Phase 1
    public long? deltaUsd { get; set; }   // null in Phase 1
}

public class KpiHighRiskDto
{
    public int     value      { get; set; }   // High + PEP customers
    public int?    delta      { get; set; }   // null in Phase 1
    public double? pctOfBook  { get; set; }   // high ÷ total customers, 0–100
    public int?    inEdd      { get; set; }   // null in Phase 1 (no EDD flag modeled)
}

public class ExposureTrendDto
{
    public string                unit   { get; set; } = "USD";
    public List<TrendPointDto>   points { get; set; } = new();
}

public class TrendPointDto
{
    public string label { get; set; } = "";
    public long   value { get; set; }
}

public class RiskMixDto
{
    public int  total  { get; set; }   // distinct customers (book)
    public int  low    { get; set; }
    public int  medium { get; set; }
    public int  high   { get; set; }   // High + PEP
    public int? inEdd  { get; set; }   // null in Phase 1
}

public class InstrumentTypeDto
{
    public string type        { get; set; } = "";
    public int    count       { get; set; }
    public long?  exposureUsd { get; set; }   // null in Phase 1
}

public class CountryExposureDto
{
    public string  country     { get; set; } = "";
    public long?   exposureUsd { get; set; }
    public string? fatfTier    { get; set; }
}

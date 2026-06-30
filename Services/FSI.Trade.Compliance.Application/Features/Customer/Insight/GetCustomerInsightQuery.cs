using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Customer.Insight;

/// <summary>
/// Per-customer insight aggregate for the Customer Insight detail view
/// (<c>GET /api/v1/Customer/{customerId}/Insight</c>): risk band + headline
/// KPIs + the chart series (transactions over time, product mix, status mix),
/// computed from this customer's transactions (TmX_Transaction, resolved via
/// the customer snapshots that link Customer_Code → Transaction_Id).
/// </summary>
public record GetCustomerInsightQuery(string CustomerId) : IRequest<CustomerInsightDto>;

public class CustomerInsightDto
{
    public string    customerCode      { get; set; } = "";
    public string?   customerName      { get; set; }
    public string?   riskScore         { get; set; }
    public string    riskBand          { get; set; } = "Unknown";
    public int       totalTransactions { get; set; }
    public DateTime? onboardedDate     { get; set; }
    public DateTime? lastActivityDate  { get; set; }

    public List<TimeBucketDto> transactionsOverTime { get; set; } = new();
    public List<NamedCountDto> productMix           { get; set; } = new();
    public List<NamedCountDto> statusMix            { get; set; } = new();
}

public class TimeBucketDto
{
    public string period { get; set; } = "";   // "yyyy-MM"
    public int    count  { get; set; }
}

public class NamedCountDto
{
    public string label { get; set; } = "";
    public int    count { get; set; }
}

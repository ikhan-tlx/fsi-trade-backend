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
}

public class CustomerListItemDto
{
    public string    customerCode      { get; set; } = "";
    public string?   customerName      { get; set; }
    public string?   riskScore         { get; set; }
    public string    riskBand          { get; set; } = "Unknown";
    public int       transactionCount  { get; set; }
    public DateTime? lastActivityDate  { get; set; }
    public DateTime  onboardedDate     { get; set; }
}

using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Customer.Get;
using FSI.Trade.Compliance.Application.Features.Customer.GetKyc;
using FSI.Trade.Compliance.Application.Features.Customer.Insight;
using FSI.Trade.Compliance.Application.Features.Customer.InsightDetail;
using FSI.Trade.Compliance.Application.Features.Customer.InsightSummary;
using FSI.Trade.Compliance.Application.Features.Customer.List;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Customer reads — full master record (TBAML) and KYC screening (KYC products).
/// Slice 4 surface:
///
///   GET /api/v1/Customer/{customerId}        (Bearer; full master record)
///   GET /api/v1/Customer/{customerId}/Kyc    (Bearer; risk score + name from KYC screening)
///
/// Vendor names (BRAINS, customer-master upstream) live in the
/// Infrastructure adapters injected via <c>IKycScreeningService</c> and
/// <c>ICustomerMasterService</c>. This controller speaks pure domain.
///
/// No <c>[RequiresPrivilege]</c> in Slice 4 — the Transaction add flow
/// needs both reads, and at the time of FE call the user already has at
/// least <c>Transactions.Create</c>-class authority. If finer gating is
/// needed later, layer privileges on without changing the URL.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class CustomerController : ControllerBase
{
    private readonly IMediator _mediator;
    public CustomerController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Paged customer listing for the Customer Insight page
    /// (<c>GET /api/v1/Customer?page=&amp;pageSize=&amp;sort=&amp;filter=</c>).
    /// Distinct customers from TmX_Customer_Master with risk band + activity.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListCustomersQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<CustomerListItemDto>>.Ok(paged));
    }

    /// <summary>
    /// Book-wide portfolio KPI strip for the Customer Intelligence listing
    /// (<c>GET /api/v1/Customer/InsightSummary</c>): total customers, high-risk,
    /// reviews overdue, open cases, flags raised (7d). Cached ~2 min.
    /// Literal route segment ⇒ matched ahead of <c>{customerId}</c>.
    /// </summary>
    [HttpGet("InsightSummary")]
    public async Task<IActionResult> InsightSummary(CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCustomerInsightSummaryQuery(), ct);
        return Ok(ResponseViewModel<CustomerInsightSummaryDto>.Ok(dto));
    }

    [HttpGet("{customerId}")]
    [FSI.Trade.Compliance.API.Filters.Audited("READ", "Customer", "customerId")]
    public async Task<IActionResult> Get(string customerId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCustomerQuery(customerId), ct);
        return Ok(ResponseViewModel<CustomerMasterDto>.Ok(dto));
    }

    /// <summary>
    /// Per-customer insight aggregate (risk band, KPIs, transactions-over-time,
    /// product mix, status mix) backing the Customer Insight detail charts.
    /// </summary>
    [HttpGet("{customerId}/Insight")]
    [FSI.Trade.Compliance.API.Filters.Audited("READ", "Customer Insight", "customerId")]
    public async Task<IActionResult> Insight(string customerId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCustomerInsightQuery(customerId), ct);
        return Ok(ResponseViewModel<CustomerInsightDto>.Ok(dto));
    }

    /// <summary>
    /// Full 360° detail aggregate for the redesigned detail page
    /// (<c>GET /api/v1/Customer/{customerId}/InsightDetail</c>): header/gauge,
    /// KPIs, KYC timeline, flags, FCCM cases, trade + behaviour.
    /// </summary>
    [HttpGet("{customerId}/InsightDetail")]
    [FSI.Trade.Compliance.API.Filters.Audited("READ", "Customer Insight Detail", "customerId")]
    public async Task<IActionResult> InsightDetail(string customerId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCustomerInsightDetailQuery(customerId), ct);
        return Ok(ResponseViewModel<CustomerInsightDetailDto>.Ok(dto));
    }

    [HttpGet("{customerId}/Kyc")]
    [FSI.Trade.Compliance.API.Filters.Audited("READ", "Customer KYC", "customerId")]
    public async Task<IActionResult> GetKyc(string customerId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCustomerKycQuery(customerId), ct);
        return Ok(ResponseViewModel<CustomerKycDto>.Ok(dto));
    }
}

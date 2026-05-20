using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Customer.Get;
using FSI.Trade.Compliance.Application.Features.Customer.GetKyc;
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

    [HttpGet("{customerId}")]
    public async Task<IActionResult> Get(string customerId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCustomerQuery(customerId), ct);
        return Ok(ResponseViewModel<CustomerMasterDto>.Ok(dto));
    }

    [HttpGet("{customerId}/Kyc")]
    public async Task<IActionResult> GetKyc(string customerId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCustomerKycQuery(customerId), ct);
        return Ok(ResponseViewModel<CustomerKycDto>.Ok(dto));
    }
}

using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Kyc.RiskAssessment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Read model for the KYC transaction detail page's "Risk Assessment" tab.
///
///   GET /api/v1/Kyc/RiskAssessment/{transactionId}   (Bearer, Transactions.View)
///
/// Returns the consolidated BRAINS + FCCM assessment for the transaction's
/// customer plus the full per-customer execution history. The tab is read-only;
/// the privileged operational actions live on <c>KycCaseController</c>
/// ({requestId}/retry|repoll|refresh, gated by <c>Kyc.Operate</c>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Kyc/RiskAssessment")]
[Authorize]
public class KycRiskAssessmentController : ControllerBase
{
    private readonly IMediator _mediator;
    public KycRiskAssessmentController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{transactionId:long}")]
    [RequiresPrivilege("Transactions.View")]
    public async Task<IActionResult> Get(long transactionId, CancellationToken ct)
        => Ok(ResponseViewModel<KycRiskAssessmentDto>.Ok(
               await _mediator.Send(new GetKycRiskAssessmentQuery(transactionId), ct)));
}

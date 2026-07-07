using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Features.Kyc.GetCaseStatus;
using FSI.Trade.Compliance.Application.Features.Kyc.HandleCallback;
using FSI.Trade.Compliance.Application.Features.Kyc.Operations;
using FSI.Trade.Compliance.Application.Features.Kyc.SubmitCase;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// KYC-case lifecycle endpoints. Slice 4 surface:
///
///   POST /api/v1/Kyc/Case                (Bearer)        submit a new case
///   GET  /api/v1/Kyc/Case/{requestId}    (Bearer)        poll status
///   POST /api/v1/Kyc/Case/Callback       (Anonymous)     inbound webhook from FCCM
///
/// The Callback endpoint is anonymous because FCCM (the upstream) does NOT
/// have our JWT. The legacy <c>updateTransactionStatusAPIController</c>
/// also accepted plain POSTs with no header signing, so the new endpoint
/// mirrors that contract for legacy parity (FSI direction 2026-06-XX:
/// "if this is not there then we should not add this"). FCCM is on a
/// private network — the security model is network isolation, not
/// payload signing. If/when FCCM ops want to enable HMAC signing later,
/// add it back behind a feature flag without changing the wire shape.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Kyc/Case")]
public class KycCaseController : ControllerBase
{
    private readonly IMediator _mediator;
    public KycCaseController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Submit([FromBody] SubmitKycCaseCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<KycCaseSubmissionResult>.Ok(result));
    }

    [HttpGet("{requestId:long}")]
    [Authorize]
    public async Task<IActionResult> GetStatus(long requestId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetKycCaseStatusQuery(requestId), ct);
        return Ok(ResponseViewModel<KycCaseStatusDto>.Ok(dto));
    }

    // -------------------------------------------------------------------
    // Risk Assessment tab — privileged operational actions (Kyc.Operate).
    // These complement the background FccmCaseIdPoller; each acts on one case.
    // -------------------------------------------------------------------

    /// <summary>Retry the FCCM sync for a Failed/Timeout execution (opens a new attempt).</summary>
    [HttpPost("{requestId:long}/retry")]
    [Authorize]
    [RequiresPrivilege("Kyc.Operate")]
    public async Task<IActionResult> Retry(long requestId, CancellationToken ct)
        => Ok(ResponseViewModel<KycCaseSubmissionResult>.Ok(await _mediator.Send(new RetryKycCaseCommand(requestId), ct)));

    /// <summary>Force an immediate one-shot poll of an in-flight execution.</summary>
    [HttpPost("{requestId:long}/repoll")]
    [Authorize]
    [RequiresPrivilege("Kyc.Operate")]
    public async Task<IActionResult> Repoll(long requestId, CancellationToken ct)
        => Ok(ResponseViewModel<KycCaseStatusDto>.Ok(await _mediator.Send(new RepollKycCaseCommand(requestId), ct)));

    /// <summary>Re-read the FCCM risk category + re-run BRAINS↔FCCM reconciliation.</summary>
    [HttpPost("{requestId:long}/refresh")]
    [Authorize]
    [RequiresPrivilege("Kyc.Operate")]
    public async Task<IActionResult> Refresh(long requestId, CancellationToken ct)
        => Ok(ResponseViewModel<KycCaseStatusDto>.Ok(await _mediator.Send(new RefreshKycCaseCommand(requestId), ct)));

    /// <summary>
    /// FCCM verdict callback. Anonymous, no signature header — mirrors the
    /// legacy <c>updateTransactionStatusAPIController.Post</c> contract
    /// exactly (no auth at all; FCCM is on a private network).
    ///
    /// Body shape matches the legacy <c>updateTrnRequest</c>:
    /// <c>caseID</c>, <c>actionCode</c>, <c>actionReason</c>.
    ///
    /// <para>
    /// Routed under THREE URLs that hit the same handler — so FCCM ops can
    /// keep their webhook config pointed at whichever shape they have on
    /// file:
    /// <list type="bullet">
    ///   <item><c>POST /api/v1/Kyc/Case/Callback</c> — canonical (new BE).</item>
    ///   <item><c>POST /api/v1/updateTransactionStatusAPI</c> — versioned
    ///         legacy URL.</item>
    ///   <item><c>POST /api/updateTransactionStatusAPI</c> — un-versioned
    ///         legacy URL, exact match to the .NET Framework v1 controller
    ///         path.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// All three URLs are whitelisted in <c>Auth:WhitelistedPaths</c> +
    /// <c>Auth:DeviceCheckExemptPaths</c> so the bearer-token middleware
    /// lets them through.
    /// </para>
    /// </summary>
    [HttpPost("Callback")]
    [HttpPost("/api/v{version:apiVersion}/updateTransactionStatusAPI")]
    [HttpPost("/api/updateTransactionStatusAPI")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromBody] HandleKycCaseCallbackCommand cmd,
        CancellationToken ct)
    {
        cmd ??= new HandleKycCaseCallbackCommand();
        await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { received = true }));
    }
}

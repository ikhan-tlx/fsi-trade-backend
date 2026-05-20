using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Integrations;
using FSI.Trade.Compliance.Application.Features.Kyc.GetCaseStatus;
using FSI.Trade.Compliance.Application.Features.Kyc.HandleCallback;
using FSI.Trade.Compliance.Application.Features.Kyc.SubmitCase;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// KYC-case lifecycle endpoints. Slice 4 surface:
///
///   POST /api/v1/Kyc/Case                (Bearer)        submit a new case
///   GET  /api/v1/Kyc/Case/{requestId}    (Bearer)        poll status
///   POST /api/v1/Kyc/Case/Callback       (Anonymous +    inbound webhook from FCCM
///                                         HMAC header)
///
/// The Callback endpoint is anonymous because FCCM (the upstream) does NOT
/// have our JWT. Instead, it signs the payload body with a shared secret
/// (HMAC-SHA256) and includes the signature in the
/// <c>X-FCCM-Signature</c> header. We verify before accepting.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Kyc/Case")]
public class KycCaseController : ControllerBase
{
    private const string SignatureHeader = "X-FCCM-Signature";

    private readonly IMediator                    _mediator;
    private readonly IOptions<IntegrationOptions> _opt;

    public KycCaseController(IMediator mediator, IOptions<IntegrationOptions> opt)
    {
        _mediator = mediator;
        _opt      = opt;
    }

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

    [HttpPost("Callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(CancellationToken ct)
    {
        // Read the raw body so we can both verify the HMAC AND deserialize.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var      raw     = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var secret = _opt.Value.FccmCallbackSharedSecret;
        if (string.IsNullOrWhiteSpace(secret))
            return Unauthorized(BuildEnvelope(401, "callback_secret_unset",
                "FCCM callback secret is not configured. Refusing all callbacks."));

        if (!Request.Headers.TryGetValue(SignatureHeader, out var sig) || string.IsNullOrWhiteSpace(sig))
            return Unauthorized(BuildEnvelope(401, "missing_signature",
                $"Inbound FCCM callback missing {SignatureHeader} header."));

        var expected = ComputeSignature(secret, raw);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(sig.ToString())))
        {
            return Unauthorized(BuildEnvelope(401, "invalid_signature",
                "FCCM callback signature did not match expected HMAC."));
        }

        // Signature valid — deserialize as a normal MediatR command.
        var cmd = System.Text.Json.JsonSerializer.Deserialize<HandleKycCaseCallbackCommand>(
                      raw,
                      new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? new HandleKycCaseCallbackCommand();

        await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { received = true }));
    }

    private static string ComputeSignature(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash);
    }

    private static ResponseViewModel<object> BuildEnvelope(int code, string codeStr, string description) =>
        new()
        {
            status = ResponseStatus.Error(code, codeStr, description),
            data   = new { Success = 0, Code = codeStr }
        };
}

using System.Text.Json;
using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Infrastructure.Integrations.Fccm.Mapping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Development-only diagnostics. NOT exposed outside the Development environment
/// (every action 404s otherwise). Used to validate the onboarding mapping
/// framework without hitting FCCM.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Diagnostics")]
[AllowAnonymous]
public class DiagnosticsController : ControllerBase
{
    private readonly IApplicationDbContext        _db;
    private readonly IOnboardingMappingProvider   _mappingProvider;
    private readonly OnboardingPayloadMapper       _mapper;
    private readonly FccmOnboardingOptions        _onboarding;
    private readonly IHostEnvironment             _env;

    public DiagnosticsController(
        IApplicationDbContext         db,
        IOnboardingMappingProvider    mappingProvider,
        OnboardingPayloadMapper       mapper,
        IOptions<IntegrationOptions>  integration,
        IHostEnvironment              env)
    {
        _db              = db;
        _mappingProvider = mappingProvider;
        _mapper          = mapper;
        _onboarding      = integration.Value.Onboarding;
        _env             = env;
    }

    /// <summary>
    /// DRY-RUN the onboarding payload for a transaction against a mapping profile —
    /// builds exactly what would be POSTed to FCCM, but does NOT submit. Lets you
    /// validate 'v1' vs 'v2' (or any profile) shape from real KYC data.
    ///
    /// GET /api/v1/Diagnostics/OnboardingPayload?transactionId=7740&amp;profile=legacy
    /// </summary>
    [HttpGet("OnboardingPayload")]
    public async Task<IActionResult> OnboardingPayload(
        [FromQuery] int transactionId,
        [FromQuery] string profile,
        CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();
        if (string.IsNullOrWhiteSpace(profile))
            return BadRequest(new { error = "profile query param is required (e.g. 'v1' or 'v2')." });

        var loaded = _mappingProvider.Get(profile);

        var snap = await _db.TransactionCustomerSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TransactionId == transactionId, ct);
        var detailUdf = await _db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .Select(d => d.UdfData)
            .FirstOrDefaultAsync(ct);

        var applicantId = snap?.CustomerCode ?? "";
        var appIdFmt = string.IsNullOrWhiteSpace(_onboarding.ApplicationIdFormat) ? "{0}" : _onboarding.ApplicationIdFormat;

        var ctx = new OnboardingMappingContext
        {
            Snapshot    = snap,
            DetailUdf   = detailUdf,
            CustomerUdf = snap?.UdfData,
            Context = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"]         = "dryrun",
                ["applicationId"] = string.Format(appIdFmt, applicantId),
                ["requestId"]     = "0",
                ["transactionId"] = transactionId.ToString(),
                ["customerId"]    = applicantId,
            }
        };

        var node = await _mapper.MapAsync(loaded, ctx, ct);

        var envelope = new
        {
            profile       = loaded.Metadata.Name,
            targetContract= loaded.Metadata.TargetContract,
            version       = loaded.Metadata.Version,
            transactionId,
            applicantId,
            snapshotFound = snap is not null,
            detailUdfFound= detailUdf is not null,
            payload       = System.Text.Json.Nodes.JsonNode.Parse(node.ToJsonString())
        };

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
        return Content(json, "application/json");
    }
}

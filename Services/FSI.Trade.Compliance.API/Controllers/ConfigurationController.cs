using System.Text.Json;
using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Configuration;
using FSI.Trade.Compliance.Application.Features.Configuration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Configuration Management (Phase 1). DB-driven application settings + the
/// verification/diagnostics surface for the integrations they configure.
/// Reads gated by Configuration.View; every write/verify by Configuration.Manage.
/// Business logic is unchanged — this manages the configuration SOURCE only.
///   GET    /api/v1/Configuration/Branding        (AllowAnonymous — login/boot)
///   GET    /api/v1/Configuration
///   PUT    /api/v1/Configuration/{key}          { value }
///   PUT    /api/v1/Configuration                { changes:[{key,value}] }
///   POST   /api/v1/Configuration/{key}/Reset
///   GET    /api/v1/Configuration/{key}/History
///   POST   /api/v1/Configuration/{key}/Rollback { historyId }
///   POST   /api/v1/Configuration/{key}/Reveal
///   POST   /api/v1/Configuration/Verify/FccmConnection
///   POST   /api/v1/Configuration/Verify/Url            { url }
///   POST   /api/v1/Configuration/Verify/OnboardingCall { customerNo }
///   POST   /api/v1/Configuration/ValidateFile          (multipart: file, schemaKey)
///   GET/PUT /api/v1/Configuration/Logging              { masterDebug }
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ConfigurationController : ControllerBase
{
    private readonly IMediator _mediator;
    public ConfigurationController(IMediator mediator) => _mediator = mediator;

    // ----- request bodies -----
    public class SaveValueRequest { public JsonElement value { get; set; } }
    public class ChangeItem { public string key { get; set; } = default!; public JsonElement value { get; set; } }
    public class SaveManyRequest { public List<ChangeItem> changes { get; set; } = new(); }
    public class RollbackRequest { public long historyId { get; set; } }
    public class UrlRequest { public string url { get; set; } = default!; }
    public class OnboardingCallRequest { public string customerNo { get; set; } = default!; }
    public class ProviderTestRequest { public Dictionary<string, string?> context { get; set; } = new(); }
    public class LoggingRequest { public bool masterDebug { get; set; } }

    // ----- branding (public) -----
    // Pre-auth read for the login page / app boot: the login screen shows the app
    // title + logo colour before a token exists, so this single endpoint is
    // [AllowAnonymous]. It returns ONLY the four Branding.* values (never the full
    // catalogue), so no privileged config leaks. Everything else here stays gated.
    // NOTE: [AllowAnonymous] alone is NOT sufficient — CustomAuthorizationMiddleware
    // gates by an explicit path prefix allow-list, so this exact path is also listed
    // in Auth:WhitelistedPaths + Auth:DeviceCheckExemptPaths (appsettings.json). The
    // prefix "/api/v1/Configuration/Branding" does NOT match "/api/v1/Configuration",
    // so the privileged catalogue GET stays protected.
    [HttpGet("Branding")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBranding(CancellationToken ct)
        => Ok(await _mediator.Send(new GetBrandingQuery(), ct));

    // ----- catalogue -----
    [HttpGet]
    [RequiresPrivilege("Configuration.View")]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ResponseViewModel<ConfigCatalogDto>.Ok(await _mediator.Send(new GetConfigCatalogQuery(), ct)));

    [HttpGet("{key}/History")]
    [RequiresPrivilege("Configuration.View")]
    public async Task<IActionResult> History(string key, CancellationToken ct)
        => Ok(ResponseViewModel<List<ConfigHistoryDto>>.Ok(await _mediator.Send(new GetConfigHistoryQuery(key), ct)));

    // ----- writes -----
    [HttpPut("{key}")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> SaveOne(string key, [FromBody] SaveValueRequest body, CancellationToken ct)
        => Ok(ResponseViewModel<ConfigSettingDto>.Ok(await _mediator.Send(new SaveConfigValueCommand(key, body.value), ct)));

    [HttpPut]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> SaveMany([FromBody] SaveManyRequest body, CancellationToken ct)
    {
        var changes = body.changes.Select(c => (c.key, c.value)).ToList();
        return Ok(ResponseViewModel<SaveManyResultDto>.Ok(await _mediator.Send(new SaveManyConfigCommand(changes), ct)));
    }

    [HttpPost("{key}/Reset")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> Reset(string key, CancellationToken ct)
        => Ok(ResponseViewModel<ConfigSettingDto>.Ok(await _mediator.Send(new ResetConfigCommand(key), ct)));

    [HttpPost("{key}/Rollback")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> Rollback(string key, [FromBody] RollbackRequest body, CancellationToken ct)
        => Ok(ResponseViewModel<ConfigSettingDto>.Ok(await _mediator.Send(new RollbackConfigCommand(key, body.historyId), ct)));

    [HttpPost("{key}/Reveal")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> Reveal(string key, CancellationToken ct)
        => Ok(ResponseViewModel<RevealResultDto>.Ok(await _mediator.Send(new RevealSecretCommand(key), ct)));

    // ----- verification / diagnostics -----
    [HttpPost("Verify/FccmConnection")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> VerifyFccm(CancellationToken ct)
        => Ok(ResponseViewModel<ProbeResult>.Ok(await _mediator.Send(new VerifyFccmConnectionCommand(), ct)));

    [HttpPost("Verify/Url")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> VerifyUrl([FromBody] UrlRequest body, CancellationToken ct)
        => Ok(ResponseViewModel<UrlProbeResult>.Ok(await _mediator.Send(new VerifyUrlCommand(body.url), ct)));

    [HttpPost("Verify/OnboardingCall")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> VerifyOnboarding([FromBody] OnboardingCallRequest body, CancellationToken ct)
        => Ok(ResponseViewModel<OnboardingCallResult>.Ok(await _mediator.Send(new VerifyOnboardingCallCommand(body.customerNo), ct)));

    // Rich provider test: executes the configured Provider Profile with a sample
    // context and returns the actual request, response (status/headers/body/time),
    // success (per criteria) and mapped result.
    [HttpPost("Verify/ProviderTest")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> VerifyProviderTest([FromBody] ProviderTestRequest body, CancellationToken ct)
        => Ok(ResponseViewModel<ProviderExecResult>.Ok(await _mediator.Send(new VerifyProviderTestCommand(body.context ?? new()), ct)));

    [HttpPost("ValidateFile")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> ValidateFile([FromForm] IFormFile? file, [FromForm] string schemaKey, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ResponseViewModel<FileValidationResult>.Fail(400, "file_required", "No file uploaded."));
        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(ResponseViewModel<FileValidationResult>.Fail(400, "file_too_large", "File exceeds 2 MB."));
        if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ResponseViewModel<FileValidationResult>.Fail(400, "file_type", "Only .json accepted."));

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(ct);
        var result = await _mediator.Send(new ValidateConfigFileCommand(schemaKey ?? "", content), ct);
        return Ok(ResponseViewModel<FileValidationResult>.Ok(result));
    }

    // Effective onboarding config for the KYC wizard (provider title + enable flag).
    // Deliberately NOT privilege-gated beyond [Authorize] — every user who can create
    // a KYC transaction needs the provider title + whether verification is required.
    [HttpGet("Onboarding/Effective")]
    public async Task<IActionResult> OnboardingEffective(CancellationToken ct)
        => Ok(ResponseViewModel<OnboardingEffectiveDto>.Ok(await _mediator.Send(new GetOnboardingEffectiveConfigQuery(), ct)));

    [HttpGet("Logging")]
    [RequiresPrivilege("Configuration.View")]
    public async Task<IActionResult> GetLogging(CancellationToken ct)
        => Ok(ResponseViewModel<LoggingStateDto>.Ok(await _mediator.Send(new GetLoggingStateQuery(), ct)));

    [HttpPut("Logging")]
    [RequiresPrivilege("Configuration.Manage")]
    public async Task<IActionResult> SetLogging([FromBody] LoggingRequest body, CancellationToken ct)
        => Ok(ResponseViewModel<LoggingStateDto>.Ok(await _mediator.Send(new SetLoggingStateCommand(body.masterDebug), ct)));
}

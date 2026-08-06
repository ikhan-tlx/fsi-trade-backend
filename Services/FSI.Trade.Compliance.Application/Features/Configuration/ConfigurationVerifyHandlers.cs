using FSI.Trade.Compliance.Application.Contracts.Configuration;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace FSI.Trade.Compliance.Application.Features.Configuration;

// Verification / diagnostics handlers. The probes themselves live in
// Infrastructure (IConfigurationProbe); these handlers are thin pass-throughs
// plus the reveal + logging-master-debug endpoints.

public record VerifyFccmConnectionCommand() : IRequest<ProbeResult>;
public record VerifyUrlCommand(string Url) : IRequest<UrlProbeResult>;
public record VerifyOnboardingCallCommand(string CustomerNo) : IRequest<OnboardingCallResult>;
// Rich provider test — executes the configured Provider Profile with a sample
// context and returns the full request/response/mapping/timing envelope.
public record VerifyProviderTestCommand(Dictionary<string, string?> Context) : IRequest<ProviderExecResult>;
public record ValidateConfigFileCommand(string SchemaKey, string Content) : IRequest<FileValidationResult>;
public record RevealSecretCommand(string Key) : IRequest<RevealResultDto>;
public record GetLoggingStateQuery() : IRequest<LoggingStateDto>;
public record SetLoggingStateCommand(bool MasterDebug) : IRequest<LoggingStateDto>;

// Effective onboarding config the KYC wizard needs (title + enable flag). Not
// privilege-gated — any authenticated user renders the wizard.
public record GetOnboardingEffectiveConfigQuery() : IRequest<OnboardingEffectiveDto>;
public class OnboardingEffectiveDto
{
    public string providerTitle { get; set; } = "the KYC verification provider";
    public bool   requireExternalVerification { get; set; }
    public string failureHandling { get; set; } = "Block";
}

public class GetOnboardingEffectiveConfigQueryHandler : IRequestHandler<GetOnboardingEffectiveConfigQuery, OnboardingEffectiveDto>
{
    private readonly IProviderTitleProvider _provider;
    public GetOnboardingEffectiveConfigQueryHandler(IProviderTitleProvider provider) => _provider = provider;

    public Task<OnboardingEffectiveDto> Handle(GetOnboardingEffectiveConfigQuery req, CancellationToken ct)
        => Task.FromResult(new OnboardingEffectiveDto
        {
            providerTitle = _provider.Title,
            requireExternalVerification = _provider.RequireExternalVerification,
            failureHandling = _provider.FailureHandling
        });
}

public class RevealResultDto { public string? value { get; set; } }
public class LoggingStateDto { public bool masterDebug { get; set; } public string? autoRevertAt { get; set; } }

public class ConfigurationVerifyHandlers :
    IRequestHandler<VerifyFccmConnectionCommand, ProbeResult>,
    IRequestHandler<VerifyUrlCommand, UrlProbeResult>,
    IRequestHandler<VerifyOnboardingCallCommand, OnboardingCallResult>,
    IRequestHandler<VerifyProviderTestCommand, ProviderExecResult>,
    IRequestHandler<ValidateConfigFileCommand, FileValidationResult>,
    IRequestHandler<RevealSecretCommand, RevealResultDto>,
    IRequestHandler<GetLoggingStateQuery, LoggingStateDto>,
    IRequestHandler<SetLoggingStateCommand, LoggingStateDto>
{
    private readonly IConfigurationProbe _probe;
    private readonly IConfiguration _config;

    // Session-local master-debug flag (Phase 1). TODO(Phase 2): drive Serilog
    // LoggingLevelSwitch per module + a real 2h auto-revert timer.
    private static bool _masterDebug;
    private static DateTime? _autoRevertAt;

    public ConfigurationVerifyHandlers(IConfigurationProbe probe, IConfiguration config)
    { _probe = probe; _config = config; }

    public Task<ProbeResult> Handle(VerifyFccmConnectionCommand r, CancellationToken ct)
        => _probe.TestFccmConnectionAsync(ct);

    public Task<UrlProbeResult> Handle(VerifyUrlCommand r, CancellationToken ct)
        => _probe.TestUrlAsync(r.Url, ct);

    public Task<OnboardingCallResult> Handle(VerifyOnboardingCallCommand r, CancellationToken ct)
        => _probe.TestOnboardingCallAsync(r.CustomerNo, ct);

    public Task<ProviderExecResult> Handle(VerifyProviderTestCommand r, CancellationToken ct)
        => _probe.TestProviderAsync(r.Context ?? new(), ct);

    public Task<FileValidationResult> Handle(ValidateConfigFileCommand r, CancellationToken ct)
        => _probe.ValidateFileAsync(r.SchemaKey, r.Content, ct);

    public Task<RevealResultDto> Handle(RevealSecretCommand r, CancellationToken ct)
    {
        // TODO(Phase 2): require step-up re-auth and audit the reveal explicitly.
        // Secrets are NOT bridged into the DB layer; read from the secret store
        // (appsettings/env) and return with the password masked so it never leaks.
        var raw = _config[r.Key.Replace('.', ':')];
        return Task.FromResult(new RevealResultDto { value = ConfigCoerce.Mask(raw) });
    }

    public Task<LoggingStateDto> Handle(GetLoggingStateQuery r, CancellationToken ct)
        => Task.FromResult(new LoggingStateDto { masterDebug = _masterDebug, autoRevertAt = _autoRevertAt?.ToString("o") });

    public Task<LoggingStateDto> Handle(SetLoggingStateCommand r, CancellationToken ct)
    {
        _masterDebug = r.MasterDebug;
        _autoRevertAt = r.MasterDebug ? DateTime.UtcNow.AddHours(2) : null;
        // TODO(Phase 2): flip every Application.* module's Serilog LoggingLevelSwitch
        //                to Debug/Verbose here and schedule the 2h auto-revert.
        return Task.FromResult(new LoggingStateDto { masterDebug = _masterDebug, autoRevertAt = _autoRevertAt?.ToString("o") });
    }
}

using FSI.Trade.Compliance.Application.Contracts.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

/// <summary>
/// Startup guard: confirms every integration setting resolves from the
/// Configuration module and logs an ERROR for any that is missing/empty, so a
/// mis-seeded environment is caught at boot rather than at the first FCCM call.
/// Non-sensitive keys are read from the DB→IConfiguration bridge; sensitive keys
/// are probed through <see cref="ISecretConfigReader"/> (presence only — the
/// value is never read into a log).
/// </summary>
public sealed class IntegrationConfigStartupValidator : IHostedService
{
    private readonly IConfiguration _config;
    private readonly ISecretConfigReader _secrets;
    private readonly ILogger<IntegrationConfigStartupValidator> _log;

    public IntegrationConfigStartupValidator(
        IConfiguration config, ISecretConfigReader secrets, ILogger<IntegrationConfigStartupValidator> log)
    {
        _config = config;
        _secrets = secrets;
        _log = log;
    }

    // Non-sensitive integration settings expected from the DB→config bridge.
    private static readonly string[] BridgedKeys =
    {
        "Integration:FccmOnboardingUrl",
        "Integration:Onboarding:Username",
    };

    // Sensitive integration settings moved into the ENCRYPTED store in the
    // Configuration module (read via ISecretConfigReader, not the bridge).
    private static readonly string[] SecretKeys =
    {
        "Integration.Onboarding.Password",
        "Integration.FccmOracleConnection",
    };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var missing = 0;

        foreach (var key in BridgedKeys)
        {
            if (string.IsNullOrWhiteSpace(_config[key]))
            {
                _log.LogError(
                    "Integration setting '{Key}' is not configured in the Configuration module (nor appsettings). The dependent integration may not work.",
                    key);
                missing++;
            }
        }

        foreach (var key in SecretKeys)
        {
            if (!_secrets.HasValue(key))
            {
                _log.LogError(
                    "Integration secret '{Key}' is not set in the Configuration module. Set it from Configuration → FCCM/Integration (stored encrypted).",
                    key);
                missing++;
            }
        }

        var total = BridgedKeys.Length + SecretKeys.Length;
        if (missing == 0)
            _log.LogInformation("Integration configuration validated: all {Count} integration settings resolved from the Configuration module.", total);
        else
            _log.LogWarning("Integration configuration validation completed with {Missing} of {Total} setting(s) missing. See the errors above.", missing, total);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

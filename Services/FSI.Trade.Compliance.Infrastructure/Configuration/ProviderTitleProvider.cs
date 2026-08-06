using FSI.Trade.Compliance.Application.Contracts.Configuration;
using Microsoft.Extensions.Configuration;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

/// <summary>Reads Onboarding.* from the DB-bridged IConfiguration.</summary>
public sealed class ProviderTitleProvider : IProviderTitleProvider
{
    private readonly IConfiguration _config;
    public ProviderTitleProvider(IConfiguration config) => _config = config;

    public string Title
    {
        get
        {
            var t = _config["Onboarding:ProviderTitle"];
            return string.IsNullOrWhiteSpace(t) ? "the KYC verification provider" : t.Trim();
        }
    }

    public bool RequireExternalVerification =>
        string.Equals(_config["Onboarding:RequireExternalVerification"], "true", StringComparison.OrdinalIgnoreCase);

    public string FailureHandling
    {
        get
        {
            var m = _config["Onboarding:FailureHandling"];
            return string.IsNullOrWhiteSpace(m) ? "Block" : m.Trim();
        }
    }
}

using FSI.Trade.Compliance.Application.Contracts.Persistence;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

/// <summary>Triggers a reload of every live <see cref="DbConfigurationProvider"/>.</summary>
public sealed class ConfigurationRefresher : IConfigurationRefresher
{
    public void Refresh() => DbConfigurationProvider.ReloadAll();
}

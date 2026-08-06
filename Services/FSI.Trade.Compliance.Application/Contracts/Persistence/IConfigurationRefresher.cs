namespace FSI.Trade.Compliance.Application.Contracts.Persistence;

/// <summary>
/// Reloads the DB-backed configuration layer after a settings write so the
/// IOptions bridge (DbConfigurationProvider) republishes the new values and
/// every IOptionsMonitor&lt;T&gt; consumer re-binds without a restart.
/// Called by the Configuration save/reset/rollback handlers.
/// </summary>
public interface IConfigurationRefresher
{
    void Refresh();
}

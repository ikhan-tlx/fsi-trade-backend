using FSI.Trade.Compliance.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// Oracle-side reads against FCCM's <c>FCC_OB_RA</c> table. The poller calls
/// these to discover when a previously-submitted FCCM request has been
/// provisioned with a CASE_ID and (later) populated with a RISK_CATEGORY_KEY.
///
/// STUB IMPLEMENTATION — Slice 4 ships the interface and a no-op reader so
/// the rest of the scaffold compiles. The real implementation requires:
///
///   1. Adding the <c>Oracle.ManagedDataAccess.Core</c> NuGet package to
///      FSI.Trade.Compliance.Infrastructure.csproj.
///   2. Wiring an <c>OracleConnection</c> using
///      <see cref="IntegrationOptions.FccmOracleConnection"/>.
///   3. Two SELECTs:
///        SELECT CASE_ID            FROM FCC_OB_RA WHERE REQUEST_ID = :requestId
///        SELECT RISK_CATEGORY_KEY  FROM FCC_OB_RA WHERE REQUEST_ID = :requestId
///   4. Returning null on no-row-found (the poller treats null as "not yet").
///
/// Until then, this stub returns null for every request and the poller
/// transitions cases to <c>Timeout</c> after the configured window.
/// </summary>
public class FccmOracleReader
{
    private readonly IntegrationOptions          _opt;
    private readonly ILogger<FccmOracleReader>   _log;
    private          bool                        _warnedAboutStub;

    public FccmOracleReader(IOptions<IntegrationOptions> opt, ILogger<FccmOracleReader> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public Task<string?> ReadCaseIdAsync(string fccmRequestId, CancellationToken ct = default)
    {
        WarnIfStub();
        return Task.FromResult<string?>(null);
    }

    public Task<string?> ReadRiskCategoryKeyAsync(string fccmRequestId, CancellationToken ct = default)
    {
        WarnIfStub();
        return Task.FromResult<string?>(null);
    }

    private void WarnIfStub()
    {
        if (_warnedAboutStub) return;
        if (string.IsNullOrWhiteSpace(_opt.FccmOracleConnection))
        {
            _log.LogWarning("FccmOracleReader is in STUB mode (Integration:FccmOracleConnection unset). " +
                            "All KYC cases will time out. Wire Oracle.ManagedDataAccess.Core in Slice 4 build phase.");
        }
        else
        {
            _log.LogWarning("FccmOracleReader is a STUB — Integration:FccmOracleConnection is set but the real " +
                            "reader is not implemented yet. See file header for wiring steps.");
        }
        _warnedAboutStub = true;
    }
}

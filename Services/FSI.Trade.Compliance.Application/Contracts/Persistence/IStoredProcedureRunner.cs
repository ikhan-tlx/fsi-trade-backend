using System.Data;

namespace FSI.Trade.Compliance.Application.Contracts.Persistence;

/// <summary>
/// Runs a stored procedure by name with named parameters and returns the
/// first result-set as a <see cref="DataTable"/>.
///
/// Slice 7 (Reports) uses this to execute the SP referenced by a report
/// template (TmX_Lookup REPORT_TYPE rows carry the SP name as their
/// LookupDescription/LookupName). The legacy AngularJS ReportService
/// posts the SP name + a free-form <c>Arguments</c> map, the backend runs
/// the SP, then either renders the result via Liquid (HTML/PDF) or
/// streams it as an Excel sheet.
///
/// Callers MUST validate that the SP is on the report allowlist before
/// passing it here — this primitive is intentionally generic and does
/// not enforce policy.
/// </summary>
public interface IStoredProcedureRunner
{
    /// <param name="storedProcedureName">
    /// Fully-resolved SP name (no schema prefix needed if it lives in dbo).
    /// </param>
    /// <param name="parameters">
    /// Named parameters; nulls are sent as DBNull. Keys may or may not be
    /// prefixed with '@' — the implementation normalises both forms.
    /// </param>
    /// <param name="commandTimeoutSeconds">
    /// Optional override. Reports occasionally hit large datasets, so a
    /// caller can lift the default 30s. Pass null to use connection default.
    /// </param>
    Task<DataTable> ExecuteAsync(
        string storedProcedureName,
        IReadOnlyDictionary<string, object?> parameters,
        int? commandTimeoutSeconds = null,
        CancellationToken ct = default);
}

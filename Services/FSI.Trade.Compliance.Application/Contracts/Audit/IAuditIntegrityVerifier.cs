namespace FSI.Trade.Compliance.Application.Contracts.Audit;

/// <summary>
/// Recomputes the tamper-evidence hash chain over <c>TmX_Audit_Log</c> and reports
/// whether it is intact. Each row's <c>Row_Hash</c> must equal
/// SHA-256(previous row's stored hash | this row's canonical content); a mismatch
/// means a row was altered, inserted, deleted or re-ordered after the fact.
/// Used by the audit export (integrity statement on the report) and available for
/// an on-demand integrity check.
/// </summary>
public interface IAuditIntegrityVerifier
{
    Task<AuditIntegrityResult> VerifyAsync(CancellationToken ct = default);
}

public sealed class AuditIntegrityResult
{
    /// <summary>True when every row's recomputed hash matches its stored hash.</summary>
    public bool  Intact              { get; set; }
    /// <summary>Number of rows checked.</summary>
    public long  RowsChecked         { get; set; }
    /// <summary>Audit_Id of the first row whose hash does not verify (null when intact).</summary>
    public long? FirstBrokenAuditId  { get; set; }
    /// <summary>Human-readable summary suitable for the export header.</summary>
    public string Summary =>
        RowsChecked == 0
            ? "No audit rows to verify."
            : Intact
                ? $"Integrity verified: hash chain intact across {RowsChecked:N0} row(s)."
                : $"INTEGRITY FAILURE: hash chain breaks at Audit_Id {FirstBrokenAuditId} (checked {RowsChecked:N0} row(s)).";
}

namespace FSI.Trade.Compliance.Application.Contracts.Audit;

/// <summary>
/// Explicit audit writer for events the EF SaveChanges interceptor cannot see:
/// READ / view of sensitive records, LOGIN, EXPORT, and FAILUREs (a rejected
/// mutation never commits, so the interceptor records nothing). Writes one
/// compliance-grade <c>TmX_Audit_Log</c> row, enriched with the same
/// who/where/how context and tamper-evidence hash chain as mutation rows.
/// Best-effort: failures to write audit must never break the request.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEvent evt, CancellationToken ct = default);
}

public sealed class AuditEvent
{
    /// <summary>READ / LOGIN / EXPORT / APPROVE / … (the operation).</summary>
    public string  Action        { get; set; } = "";
    public string  EntityType    { get; set; } = "";
    public string? EntityKey     { get; set; }
    public string? Description   { get; set; }
    public string  Outcome       { get; set; } = "SUCCESS";   // SUCCESS / FAILURE
    public int?    StatusCode    { get; set; }
    public string? FailureReason { get; set; }
    public string? ChangesJson   { get; set; }
}

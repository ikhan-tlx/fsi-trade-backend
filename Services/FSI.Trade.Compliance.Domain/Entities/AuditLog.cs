namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Generic audit-trail row. One per changed entity per SaveChanges, written by
/// the EF SaveChanges interceptor. Captures who (user + IP), what (entity + key
/// + field-level old->new), where (screen / request path + method) and when.
/// </summary>
public class AuditLog
{
    public long      AuditId      { get; set; }
    public int?      TenantId     { get; set; }
    public string?   UserId       { get; set; }
    public string?   UserName     { get; set; }
    public string?   IpAddress    { get; set; }
    public string    Action       { get; set; } = "";   // CREATE / READ / UPDATE / DELETE / LOGIN / EXPORT / APPROVE / REJECT
    public string?   UserRole     { get; set; }          // role/privilege at time of action
    public string?   OnBehalfOf   { get; set; }          // impersonation / delegation (reserved)
    public string    EntityType   { get; set; } = "";    // object / module affected
    public string?   EntityKey    { get; set; }          // primary key of the affected record
    public string?   ChangesJson  { get; set; }          // [{field,old,new}] — before/after
    public string?   Description  { get; set; }          // human-readable summary
    public string?   Screen       { get; set; }          // FE-supplied X-Screen (originating page)
    public string?   RequestPath  { get; set; }          // endpoint / URL
    public string?   HttpMethod   { get; set; }
    public int?      StatusCode   { get; set; }          // HTTP response status
    public string?   Outcome      { get; set; }          // SUCCESS / FAILURE
    public string?   FailureReason{ get; set; }          // error / denial detail
    public string?   Host         { get; set; }          // server / machine that processed it
    public string?   Application  { get; set; }          // app / service name
    public string?   SessionId    { get; set; }          // login session (JWT jti)
    public string?   CorrelationId{ get; set; }          // trace id across services
    public string?   UserAgent    { get; set; }          // browser / device / OS
    public string?   RowHash      { get; set; }          // SHA-256 of canonical content + prev hash
    public string?   PrevHash     { get; set; }          // hash of the immediately preceding row (chain)
    public DateTime  CreatedDate  { get; set; }          // UTC, millisecond precision
}

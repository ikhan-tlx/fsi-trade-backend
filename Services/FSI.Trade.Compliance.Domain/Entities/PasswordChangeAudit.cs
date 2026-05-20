namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.Password_Change_Audit_Trail (created by 2026_05_003).
/// One row written per successful ChangePassword call.
/// </summary>
public class PasswordChangeAudit
{
    public long      AuditTrailId         { get; set; }
    public string    UserId               { get; set; } = "";
    public string?   PasswordHash         { get; set; }
    public string?   CreatedBy            { get; set; }
    public DateTime  CreatedDate          { get; set; }
    public int?      SourceAuditTrailId   { get; set; }
    public string?   MigratedFromAspNetId { get; set; }
}

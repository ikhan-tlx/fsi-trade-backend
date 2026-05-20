namespace FSI.Trade.Compliance.Domain.Entities.Application;

/// <summary>
/// Generic checklist/attachment rows for a transaction. The transaction
/// edit page surfaces this on a "Documents" tab. Filtered on read by
/// <c>Module_Code = 'Transaction'</c> (constant) so loan / account-opening
/// checklist rows don't leak through.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 4781.
/// </summary>
public class ApplicationChecklist
{
    public int       ApplicationChecklistId   { get; set; }
    public int       TransactionId            { get; set; }
    public string    ModuleCode               { get; set; } = "";
    public int       TenantId                 { get; set; }
    public bool      ActiveFlag               { get; set; }

    public int?      ChecklistTypeLkp         { get; set; }
    public string?   AttachmentUrl            { get; set; }
    public string?   ImageData                { get; set; }       // base64-ish blob — typically too heavy to inline; check usage
    public bool?     VerificationRequired     { get; set; }
    public int?      VerificationOutcomeLkp   { get; set; }
    public int?      LocationId               { get; set; }
    public string?   UserId                   { get; set; }
    public Guid?     MobileId                 { get; set; }
    public int?      TabId                    { get; set; }

    public string    CreatedBy                { get; set; } = "";
    public DateTime  CreatedDate              { get; set; }
    public string?   LastUpdatedBy            { get; set; }
    public DateTime? LastUpdatedDate          { get; set; }
}

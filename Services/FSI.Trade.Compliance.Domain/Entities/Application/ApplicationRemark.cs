namespace FSI.Trade.Compliance.Domain.Entities.Application;

/// <summary>
/// Free-text remarks attached to a transaction (each scoped by Action_Type
/// and Remarks_Lkp). The transaction edit page surfaces these as a comments
/// history; the workflow-execute command also appends here when the user
/// supplies "comments" on a transition.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 4855.
/// </summary>
public class ApplicationRemark
{
    public int       ApplicationRemarkId   { get; set; }
    public int?      TransactionId         { get; set; }
    public string    ModuleCode            { get; set; } = "";
    public int       TenantId              { get; set; }

    public string?   ActionType            { get; set; }
    public int       RemarksLkp            { get; set; }
    public string?   Comments              { get; set; }
    public string    UserId                { get; set; } = "";
    public Guid?     MobileId              { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}

namespace FSI.Trade.Compliance.Domain.Entities.Application;

/// <summary>
/// Read-only projection over <c>TmX_Application_Deviation_VW</c>. The view
/// JOINs <c>TmX_Application_Deviation</c> with
/// <c>TmX_Application_Deviation_Approval</c> to flatten deviation +
/// approver-action info into one row.
///
/// Used by the transaction edit page to show the "deviations" tab. Filter
/// on read: <c>Module_Code = 'Transaction'</c>.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 3280.
/// </summary>
public class ApplicationDeviationView
{
    public string?  Creator           { get; set; }
    public string?  RuleName          { get; set; }
    public string?  RuleMessage       { get; set; }
    public int?     DeviationAction   { get; set; }   // Action_Type_Lkp_ID from approval
    public string?  UserId            { get; set; }   // Approval_User_ID — the approver
    public int      TransactionId     { get; set; }
    public string   ModuleCode        { get; set; } = "";
    public int?     ApprovalId        { get; set; }
    public int      DeviationId       { get; set; }
}

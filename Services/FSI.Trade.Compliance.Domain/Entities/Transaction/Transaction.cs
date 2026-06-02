namespace FSI.Trade.Compliance.Domain.Entities.Transaction;

/// <summary>
/// Read/write projection over <c>TmX_Transaction</c> — the actual row, not the
/// view. Used by the Slice 6 detail endpoint (joined with its UDF + customer
/// + stakeholder + beneficiary children) and by upcoming create / update /
/// cancel commands. The view <c>TmX_Transaction_VW</c> is for the grid; this
/// is for everything else.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 2054.
/// </summary>
public class Transaction
{
    public int       TransactionId           { get; set; }
    public int       TenantId                { get; set; }
    public int?      CompanyBranchId         { get; set; }
    public int       ProductId               { get; set; }
    public string?   ClientReferenceNumber   { get; set; }
    public string?   UserId                  { get; set; }   // creator's TmX_User.User_ID (nvarchar)
    public int?      CurrencyId              { get; set; }
    public int       TransactionStatusLkp    { get; set; }
    public string?   TransactionNumber       { get; set; }
    public Guid?     ProcessInstanceId       { get; set; }
    public Guid?     MobileId                { get; set; }
    public bool?     IsWorkflowAttached      { get; set; }
    public int?      TransactionTypeLkp      { get; set; }
    public DateTime? TransactionDate         { get; set; }

    /// <summary>
    /// Slice 9 — FCCM case reference populated by
    /// <c>IFccmTradeCaseService.SubmitAsync</c> after a successful
    /// post-workflow-finalise submit. NULL until then. Doubles as the
    /// idempotency anchor so the same finalised transaction isn't
    /// resubmitted twice. Cleared back to NULL only via the explicit
    /// admin resubmit endpoint.
    /// </summary>
    public string?   FccmCaseId              { get; set; }

    public string    CreatedBy               { get; set; } = "";
    public DateTime  CreatedDate             { get; set; }
    public string?   LastUpdatedBy           { get; set; }
    public DateTime? LastUpdatedDate         { get; set; }
}

namespace FSI.Trade.Compliance.Domain.Entities.Transaction;

/// <summary>
/// READ-ONLY projection over <c>TmX_Transaction_VW</c>. Backs the
/// GET /api/v1/Transaction grid used by the FE's Trade Repository page.
///
/// The view itself does the heavy lifting: joins to Customer Master, Product,
/// Company Branch, User (creator), WorkflowProcessInstance (current state),
/// and WorkflowInbox (current assignee). It also collapses multiple inbox
/// rows for the same transaction into a single grid row — when more than
/// one actor has it in their workflow inbox, <c>Inbox_User_ID</c> is NULL
/// and <c>Inbox_Name</c> reads "Multiple Users". So this entity is a
/// pure DTO shape; no navigation properties, no writes.
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 2085
/// (<c>CREATE VIEW [dbo].[TmX_Transaction_VW]</c>).
/// </summary>
public class TransactionListView
{
    public int       TransactionId           { get; set; }
    public int       TenantId                { get; set; }
    public int?      CompanyBranchId         { get; set; }
    public Guid?     ProcessInstanceId       { get; set; }
    public string?   ClientReferenceNumber   { get; set; }

    public string    CreatedBy               { get; set; } = "";
    public DateTime  CreatedDate             { get; set; }
    public string?   LastUpdatedBy           { get; set; }
    public DateTime? LastUpdatedDate         { get; set; }

    public bool?     IsWorkflowAttached      { get; set; }
    public int?      TransactionTypeLkp      { get; set; }
    public DateTime? TransactionDate         { get; set; }
    public string?   TransactionType         { get; set; }
    public string?   TransactionNumber       { get; set; }
    public int       TransactionStatusLkp    { get; set; }

    public string?   CustomerCode            { get; set; }
    public string?   NationalIdentifierValue { get; set; }
    public string?   CustomerName            { get; set; }

    public int       ProductId               { get; set; }
    public string    ProductName             { get; set; } = "";
    public string?   BranchName              { get; set; }

    public string?   Creator                 { get; set; }
    public string?   CreatorName             { get; set; }
    public string?   CreatorId               { get; set; }
    public int?      CreatorLocationId       { get; set; }

    public string?   CurrentState            { get; set; }
    public string?   InboxUserId             { get; set; }   // NULL when >1 actor — see view CTE
    public string?   InboxUser               { get; set; }
    public string?   InboxName               { get; set; }   // "Multiple Users" when >1 actor
    public string?   Status                  { get; set; }
}

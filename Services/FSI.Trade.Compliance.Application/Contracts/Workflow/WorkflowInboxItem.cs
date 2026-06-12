namespace FSI.Trade.Compliance.Application.Contracts.Workflow;

/// <summary>
/// Slice 13 — one row of the Inbox grid. Replaces the original 4-field
/// shape (ProcessId / SchemeCode / StateName / CreatedDate) which didn't
/// carry any of the columns the FE actually renders.
///
/// Field set matches the FE Inbox grid 1:1 (Application column + pill,
/// Customer, Product, Branch, Creator, Assigned To, App. Date). The
/// `transactionId` / `processInstanceId` fields are present for drill-
/// down (FE navigates to <c>GET /Transaction/{transactionId}</c> for
/// the full payload — that endpoint is the "step 2" of the listing
/// flow and already returns customers / beneficiaries / stakeholders /
/// checklist / remarks / flags / workflow snapshot / productSummary).
///
/// Skipped from the legacy <c>TmX_Application_VW</c> view:
/// <list type="bullet">
///   <item><c>Application_Type</c> — hardcoded to <c>TradeRepository</c> in
///         the legacy Transaction branch; new BE is Trade-only.</item>
///   <item><c>Full_Name</c> — same as <c>Customer_Name</c> for Trade rows.</item>
///   <item><c>Customer_Contact_Num</c> — legacy stores the raw UDF JSON
///         blob in this field, which is broken; skip.</item>
///   <item><c>Approved_Amount / Requested_Amount / Total_Tenure</c> — loan-only.</item>
///   <item><c>Comments</c> — always NULL for the Transaction branch in legacy.</item>
///   <item><c>Inbox_User / Inbox_Name</c> — replaced with
///         <see cref="assignedToName"/> / <see cref="assignedToEmail"/>
///         which carry display name + email, matching what the FE grid
///         actually renders.</item>
/// </list>
/// </summary>
public class WorkflowInboxItem
{
    // ---- identity / drill-down anchors ----
    public int       transactionId          { get; set; }
    public System.Guid? processInstanceId   { get; set; }
    public string?   transactionNumber      { get; set; }

    // ---- Application column status pill ----
    public int       transactionStatusLkp   { get; set; }
    public string?   status                 { get; set; }    // resolved Visible_Value (e.g. "Draft")

    // ---- Customer column ----
    public string?   customerName           { get; set; }

    // ---- Product column ----
    public string?   productName            { get; set; }

    // ---- Branch column ----
    public string?   branchName             { get; set; }

    // ---- Creator column (two-line: name + email) ----
    public string?   creatorName            { get; set; }
    public string?   creatorEmail           { get; set; }

    // ---- Assigned To column (two-line: name + email) ----
    public string?   assignedToId           { get; set; }
    public string?   assignedToName         { get; set; }
    public string?   assignedToEmail        { get; set; }

    // ---- App. Date column + sort keys ----
    public System.DateTime?  transactionDate { get; set; }
    public System.DateTime   createdDate     { get; set; }
    public System.DateTime?  transitionTime  { get; set; }    // default sort key (DESC)
}

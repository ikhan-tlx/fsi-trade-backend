using FSI.Trade.Compliance.Application.Contracts.Workflow;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Detail;

/// <summary>
/// One transaction's full edit-page payload — header, customer + accounts,
/// beneficiaries, stakeholders, checklist, deviations, remarks, plus the
/// workflow snapshot (current state + commands available for the caller).
///
/// Mirrors legacy GET /api/v1/Transaction/{id} including its "fold workflow
/// commands into the same response" shape.
/// </summary>
public record GetTransactionByIdQuery(int TransactionId) : IRequest<TransactionDetailDto>;

public class TransactionDetailDto
{
    public int       transactionId           { get; set; }
    public int       tenantId                { get; set; }
    public int?      companyBranchId         { get; set; }
    public int       productId               { get; set; }
    /// <summary>Display name from TmX_Product (for the detail summary card).</summary>
    public string?   productName             { get; set; }
    /// <summary>Display name of the selected sub-product tab (TmX_Tab), null when none.</summary>
    public string?   subProductName          { get; set; }
    public string?   transactionNumber       { get; set; }
    public string?   clientReferenceNumber   { get; set; }
    public string?   userId                  { get; set; }
    public int?      currencyId              { get; set; }
    public int       transactionStatusLkp    { get; set; }
    public int?      transactionTypeLkp      { get; set; }
    public DateTime? transactionDate         { get; set; }
    public Guid?     processInstanceId       { get; set; }
    public bool?     isWorkflowAttached      { get; set; }

    public DateTime  createdDate             { get; set; }
    public string    createdBy               { get; set; } = "";
    public DateTime? lastUpdatedDate         { get; set; }
    public string?   lastUpdatedBy           { get; set; }

    /// <summary>Parsed JSON object from TmX_Transaction_Detail.UDF_Data (server-side parse per decision A).</summary>
    public object?   udfData                 { get; set; }

    public List<CustomerDto>         customers     { get; set; } = new();
    public List<UdfChildDto>         beneficiaries { get; set; } = new();
    public List<UdfChildDto>         stakeholders  { get; set; } = new();
    public List<ChecklistDto>        checklist     { get; set; } = new();
    public List<RemarkDto>           remarks       { get; set; } = new();
    public List<DeviationDto>        deviations    { get; set; } = new();

    /// <summary>
    /// Slice 8 — every flag scoped to this transaction's product, with
    /// its current state. Flat list (FE preference). Each entry carries
    /// both scope + catalogue + transaction-state fields so the FE can
    /// render the panel without a second roundtrip.
    /// </summary>
    public List<TransactionFlagDto>  flags         { get; set; } = new();

    public WorkflowSnapshotDto       workflow      { get; set; } = new();

    /// <summary>
    /// Slice 10 §7.6 — flat summary card for the Trade Finance detail page.
    /// Server-side back-traced from TmX_Tenant_Field_Setup + UDF_Data so the
    /// FE doesn't have to walk the dynamic-form tree. Null when the product
    /// has no matching fields configured (e.g. KYC product, account types).
    /// </summary>
    public ProductSummaryDto?        productSummary { get; set; }

    /// <summary>
    /// 2026-06-04 — destination tab id of the sub-product the user picked
    /// when creating this transaction (Import LC Issuance, Import Contract
    /// Registration, Export Documentary Collection, etc.). Resolved
    /// server-side by matching ProductTab-typed <c>TenantFieldSetup</c>
    /// rows against the <c>tf{id} = true</c> keys in <see cref="udfData"/>,
    /// then reading the matched field's own <c>UDF_Data = {"TabId":N}</c>.
    ///
    /// <para>
    /// FE usage when re-opening a saved transaction:
    /// <c>GET /Product/{productId}/Tabs/{selectedSubProductTabId}/Fields</c>
    /// lazy-loads exactly the field set the user originally filled in.
    /// </para>
    ///
    /// <para>
    /// Null when:
    /// <list type="bullet">
    ///   <item>The product has no sub-product selector tab (KYC and any
    ///         other one-shot-load product — same condition as
    ///         <c>ProductTabsDto.selectorTabId == null</c>).</item>
    ///   <item>No sub-product checkbox is set to true yet (draft transaction
    ///         before the user picked a sub-product).</item>
    /// </list>
    /// </para>
    /// </summary>
    public int?                      selectedSubProductTabId { get; set; }
}

public class TransactionFlagDto
{
    // ---- scope ----
    public int      flagScopeId        { get; set; }
    public int?     productId          { get; set; }   // null = "all products" scope (Slice 8.1)
    public int?     tabId              { get; set; }
    public int      sortOrder          { get; set; }
    public string?  legacyFieldName    { get; set; }

    // ---- catalogue (denormalised — FE doesn't need a second query) ----
    public int      flagId             { get; set; }
    public string   flagCode           { get; set; } = "";
    public string   flagName           { get; set; } = "";
    public string   flagDescription    { get; set; } = "";
    public int      flagTypeLkpId      { get; set; }
    public int?     severityLkpId      { get; set; }
    public decimal  defaultWeight      { get; set; }
    public bool     requiresEvidence   { get; set; }

    // ---- transaction state (null if never set on this transaction) ----
    public int?      transactionFlagId   { get; set; }
    public bool      isFlagged           { get; set; }
    public int?      evidenceDocumentId  { get; set; }
    public string?   evidenceFileName    { get; set; }
    public string?   analystNotes        { get; set; }
    public string?   setBy               { get; set; }
    public DateTime? setDate             { get; set; }

    // ---- auto-flag match evidence (what triggered this flag) ----
    /// <summary>"Auto" (raised by the rule engine) or "Manual" (analyst-set).</summary>
    public string?   source              { get; set; }
    /// <summary>Raw match evidence as stored, e.g. "ILF_AppCountry=IR" for a
    /// per-match auto flag, or a " · "-joined summary for an aggregate flag.</summary>
    public string?   matchedValue        { get; set; }
    /// <summary>The field that triggered the flag — its human label where it can be
    /// resolved (e.g. "Applicant country"), else the field code. Null for aggregates
    /// / manual flags.</summary>
    public string?   matchedField        { get; set; }
    /// <summary>The matched value on that field (e.g. "IR"). Null for aggregates /
    /// manual flags.</summary>
    public string?   matchedFieldValue   { get; set; }
}

public class CustomerDto
{
    public int     customerMasterId          { get; set; }
    public string? customerCode              { get; set; }
    public string? customerName              { get; set; }
    public string? customerTitle             { get; set; }
    public string? nationalIdentifierValue   { get; set; }
    public int?    customerTypeLkp           { get; set; }
    public int?    customerStatusLkp         { get; set; }
    public int?    customerSegmentLkp        { get; set; }
    public int?    customerSubSegmentLkp     { get; set; }
    public int?    locationId                { get; set; }
    public DateTime? nextReviewDate          { get; set; }   // Periodic Customer Review (schedule-backed)
    public string? riskCategory              { get; set; }   // current risk band (schedule-backed)
    public object? udfData                   { get; set; }
    public List<BankingAccountDto> bankingDetails { get; set; } = new();
}

public class BankingAccountDto
{
    public int     customerBankingDetailId          { get; set; }
    public string? bankAccountNumber                { get; set; }
    public string? branchCode                       { get; set; }
    public string? bankCardNumber                   { get; set; }
    public string? cardMemberName                   { get; set; }
    public int?    addressTypeLkpId                 { get; set; }
    public int?    internetBanking                  { get; set; }
    public decimal? internetBankingTransactionAmount{ get; set; }
    public decimal? internetAtmTransactionAmount    { get; set; }
    public string? mailingCommunication             { get; set; }
    public string? chequeBookNumber                 { get; set; }
    public object? udfData                          { get; set; }
}

public class UdfChildDto
{
    public int     id        { get; set; }
    public object? udfData   { get; set; }
}

public class ChecklistDto
{
    public int     applicationChecklistId  { get; set; }
    public int?    tabId                   { get; set; }
    public int?    checklistTypeLkp        { get; set; }
    public string? attachmentUrl           { get; set; }
    public bool?   verificationRequired    { get; set; }
    public int?    verificationOutcomeLkp  { get; set; }
    public string? userId                  { get; set; }
}

public class RemarkDto
{
    public int       applicationRemarkId  { get; set; }
    public string?   actionType           { get; set; }
    public int       remarksLkp           { get; set; }
    public string?   comments             { get; set; }
    public string    userId               { get; set; } = "";
    public DateTime  createdDate          { get; set; }
    public string    createdBy            { get; set; } = "";
}

public class DeviationDto
{
    public int     deviationId    { get; set; }
    public int?    approvalId     { get; set; }
    public string? creator        { get; set; }
    public string? ruleName       { get; set; }
    public string? ruleMessage    { get; set; }
    public int?    deviationAction{ get; set; }
    public string? userId         { get; set; }
}

public class WorkflowSnapshotDto
{
    public Guid?                  processInstanceId { get; set; }
    public string?                currentState      { get; set; }
    public string?                activityName      { get; set; }
    public List<WorkflowCommand>  commands          { get; set; } = new();

    /// <summary>
    /// Slice 10 §7.2 — every user the workflow engine currently has the
    /// process assigned to (one row per WorkflowInbox entry). The FE's
    /// "People" sidebar groups these by role to surface "Analyst" vs
    /// "Authorizer" labels. Empty when no inbox rows exist (e.g.
    /// process terminal, never started, or detached).
    /// </summary>
    public List<AssignedActorDto> assignedActors    { get; set; } = new();
}

public class AssignedActorDto
{
    /// <summary>TmX_User.User_ID — string form, matches what the FE uses elsewhere.</summary>
    public string  userId    { get; set; } = "";

    /// <summary>Display name: "First Last", falling back to UserName.</summary>
    public string  userName  { get; set; } = "";

    /// <summary>
    /// Primary role for this user (first role in TmX_User_Role_Mapping ordered
    /// by RoleId). Null if no roles are mapped. The FE uses this to label
    /// "Analyst" vs "Authorizer" on the People sidebar.
    /// </summary>
    public string? roleName  { get; set; }
}

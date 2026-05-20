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
}

public class TransactionFlagDto
{
    // ---- scope ----
    public int      flagScopeId        { get; set; }
    public int      productId          { get; set; }
    public int?     tabId              { get; set; }
    public int      sortOrder          { get; set; }
    public string?  legacyFieldName    { get; set; }

    // ---- catalogue (denormalised — FE doesn't need a second query) ----
    public int      flagId             { get; set; }
    public string   flagCode           { get; set; } = "";
    public string   flagName           { get; set; } = "";
    public string   flagDescription    { get; set; } = "";
    public int      flagTypeLkpId      { get; set; }
    public int?     flagCategoryLkpId  { get; set; }
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
}

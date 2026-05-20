using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Update;

/// <summary>
/// Save current state of an existing transaction. The FE calls this every
/// time the user clicks Save AND also right before firing a workflow command
/// (save-then-execute pattern in <c>dynamicForm.tsx</c>). Workflow state is
/// NOT touched — that's the separate
/// <c>PUT /api/v1/Workflow/Process/{id}/Execute</c> endpoint.
///
/// Semantics:
/// <list type="bullet">
///   <item>Updates the transaction header (date, status, type, currency).</item>
///   <item>Replaces <c>TmX_Transaction_Detail.UDF_Data</c> with the
///         serialized <see cref="udfData"/> object.</item>
///   <item>Updates the customer snapshot in place (single row per
///         transaction; matched by <c>CustomerMasterId</c> if present).</item>
///   <item><b>Diff strategy</b> on child collections (beneficiaries,
///         stakeholders, banking details): items WITH an id are UPDATEd,
///         items WITHOUT an id are INSERTed, existing rows NOT in the
///         request are DELETEd. Matches legacy
///         <c>TransactionService.UpdateTransaction</c> semantics.</item>
/// </list>
/// </summary>
public class UpdateTransactionCommand : IRequest<TransactionDetailDto>
{
    // Path-bound — set by the controller from {id}; not part of the body.
    public int       transactionId          { get; set; }

    // Header
    public DateTime? transactionDate        { get; set; }
    public int?      currencyId             { get; set; }
    public int?      transactionTypeLkp     { get; set; }
    public int?      transactionStatusLkp   { get; set; }
    public string?   clientReferenceNumber  { get; set; }

    // Transaction Detail — parsed object on the wire, serialized to nvarchar(max) on write.
    public object?   udfData                { get; set; }

    // Customer snapshot + banking details
    public UpdateCustomerInput? customer    { get; set; }

    // Child collections
    public List<UdfChildInput>  beneficiaries { get; set; } = new();
    public List<UdfChildInput>  stakeholders  { get; set; } = new();

    /// <summary>
    /// Slice 8 — flag state changes saved alongside the transaction.
    /// Full-replace semantics: any flag NOT in this list is left
    /// untouched (NOT cleared) — the FE sends only the rows the analyst
    /// actually changed. This matches the form-save flow where flags
    /// without user interaction stay in whatever state they were last
    /// saved in. Each entry is matched by <c>flagId</c>; if a
    /// <c>TmX_Transaction_Flag</c> row exists it's UPDATEd, otherwise
    /// INSERTed. A history row is emitted for every actual state
    /// transition.
    /// </summary>
    public List<UpdateFlagInput>  flags         { get; set; } = new();
}

/// <summary>
/// One flag's new state. The handler diffs against the current row in
/// <c>TmX_Transaction_Flag</c> and emits history entries for any of:
/// isFlagged change (Set / Cleared), notes change (Notes_Updated),
/// evidence change (Evidence_Attached / Evidence_Removed).
/// </summary>
public class UpdateFlagInput
{
    /// <summary>FK to <c>TmX_Flag_Catalogue.Flag_ID</c>. Required.</summary>
    public int      flagId              { get; set; }

    public bool     isFlagged           { get; set; }
    public int?     evidenceDocumentId  { get; set; }
    public string?  analystNotes        { get; set; }
}

/// <summary>
/// Customer snapshot for update. If <see cref="customerMasterId"/> is set,
/// the existing snapshot row is UPDATEd. If null/0, a new snapshot row is
/// INSERTed (rare during edit — typically the customer was created at
/// transaction-create time).
/// </summary>
public class UpdateCustomerInput
{
    public int?     customerMasterId        { get; set; }
    public string?  customerCode            { get; set; }
    public string?  customerName            { get; set; }
    public string?  customerTitle           { get; set; }
    public string?  nationalIdentifierValue { get; set; }
    public int?     nationalIdTypeLkp       { get; set; }
    public int?     customerTypeLkp         { get; set; }
    public int?     customerSegmentLkp      { get; set; }
    public int?     customerSubSegmentLkp   { get; set; }
    public int?     locationId              { get; set; }

    /// <summary>Parsed JSON object — serialized to <c>udf_data</c> on write.</summary>
    public object?  udfData                 { get; set; }

    public List<UpdateBankingDetailInput> bankingDetails { get; set; } = new();
}

public class UpdateBankingDetailInput
{
    public int?     customerBankingDetailId           { get; set; }
    public string?  bankAccountNumber                 { get; set; }
    public string?  branchCode                        { get; set; }
    public string?  bankCardNumber                    { get; set; }
    public string?  cardMemberName                    { get; set; }
    public int?     addressTypeLkpId                  { get; set; }
    public int?     internetBanking                   { get; set; }
    public decimal? internetBankingTransactionAmount  { get; set; }
    public decimal? internetAtmTransactionAmount      { get; set; }
    public string?  mailingCommunication              { get; set; }
    public string?  chequeBookNumber                  { get; set; }
    public object?  udfData                           { get; set; }
}

/// <summary>
/// Generic UDF-carrier child shape used for beneficiaries and stakeholders.
/// <c>id</c> present → UPDATE existing row; absent → INSERT new row.
/// Any existing rows whose id isn't in the request are DELETEd.
/// </summary>
public class UdfChildInput
{
    public int?     id      { get; set; }
    public object?  udfData { get; set; }
}

namespace FSI.Trade.Compliance.Application.Contracts.Integrations;

/// <summary>
/// Submits a finalised trade transaction to FCCM. Replaces the legacy
/// <c>ICBC_Integration_Test.TradeController.TradeTransactionToFCCM</c>
/// pipeline — same downstream effects, cleanly assembled from the new
/// domain entities instead of the legacy 528-property
/// <c>RepoPayloadModel</c>.
///
/// What the implementation does, internally (matches legacy effect set):
///
///   1. Generate a MessageId + write a tracking row for correlation.
///
///   2. Write three SQL staging rows the FCCM consumer polls:
///        • BeneficiaryDetailStaging  — from TmX_Beneficiary_Detail
///        • LcTrnDetStaging           — from TmX_Transaction_Detail.UDF_Data
///        • CustMasterStaging         — from TmX_Customer_Master
///
///   3. Write a "Customer Details" event to EventStaging.
///
///   4. Write one red-flag event per ticked flag — InputName reflects the flag's
///      origin ("Auto Redflag" for auto-flags, "Manual Redflag" otherwise) — sourced
///      from <c>TmX_Transaction_Flag</c> JOINed to <c>TmX_Flag_Catalogue</c>.
///      The catalogue's <c>Flag_Description</c> is the canonical text —
///      NO hardcoded indicator strings inline. This is the structural
///      win over the legacy ~27-if-block × OR-chain-of-16-columns
///      pattern: one query, one loop, one source of truth.
///
///   5. Oracle INSERT into FCC_OB_RA (the FCCM case base record),
///      returning the FCCM case id.
///
///   6. HTTP PATCH/POST to FCCM to enrich the case with customer details.
///
///   7. Persist the returned <c>caseId</c> back to
///      <c>TmX_Transaction.Fccm_Case_Id</c> — doubles as the idempotency
///      anchor so re-fires of the same finalised workflow command are no-ops.
///
/// Failure semantics: any error in steps 2-6 throws; caller wraps the
/// invocation in try/catch and decides whether to fail the workflow
/// command or log-and-continue. Step 7 is best-effort — if persisting
/// the case id fails after FCCM accepted the case, the next read of the
/// transaction reconciles via the integration tracking row.
/// </summary>
public interface IFccmTradeCaseService
{
    /// <summary>
    /// Submit a finalised trade transaction. Idempotent — if the
    /// transaction already has a non-NULL <c>Fccm_Case_Id</c>,
    /// <see cref="FccmTradeSubmissionResult.WasSkipped"/> is true and
    /// no FCCM call is made.
    /// </summary>
    Task<FccmTradeSubmissionResult> SubmitAsync(int transactionId, CancellationToken ct = default);

    /// <summary>
    /// Explicit resubmit — bypasses the idempotency check. Clears
    /// <c>Fccm_Case_Id</c> before re-running the pipeline. Intended for
    /// the admin recovery endpoint when an earlier submit landed in a
    /// half-completed state.
    /// </summary>
    Task<FccmTradeSubmissionResult> ResubmitAsync(int transactionId, CancellationToken ct = default);
}

public class FccmTradeSubmissionResult
{
    public int      TransactionId  { get; set; }
    public string?  FccmCaseId     { get; set; }

    /// <summary>True when no FCCM call was made (already-submitted transaction).</summary>
    public bool     WasSkipped     { get; set; }

    public int      FlagEventCount { get; set; }
    public DateTime SubmittedAt    { get; set; }
}

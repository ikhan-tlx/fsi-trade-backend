using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.ResubmitFccm;

/// <summary>
/// Manual re-fire of the FCCM trade submission for a transaction. Use
/// when the workflow-post-execute hook failed (e.g. FCCM was offline)
/// and the analyst / admin wants to retry without re-running the
/// workflow command.
///
/// Maps to <c>POST /api/v1/Transaction/{id}/ResubmitToFccm</c>.
/// Gated by <c>Transactions.Resubmit</c> privilege.
///
/// Bypasses <see cref="Integrations.IFccmTradeCaseService.SubmitAsync"/>'s
/// idempotency check by calling
/// <see cref="Integrations.IFccmTradeCaseService.ResubmitAsync"/>
/// instead — clears the existing <c>Fccm_Case_Id</c> first, then
/// re-runs the pipeline. The previous FCCM case is left in place
/// downstream; the bank's compliance team handles any de-duping on
/// the FCCM side.
/// </summary>
public class ResubmitTransactionToFccmCommand : IRequest<ResubmitTransactionToFccmResult>
{
    // Path-bound — set by controller from {id}; not part of the body.
    public int transactionId { get; set; }
}

public class ResubmitTransactionToFccmResult
{
    public int      transactionId   { get; set; }
    public string?  fccmCaseId      { get; set; }
    public int      flagEventCount  { get; set; }
    public DateTime submittedAt     { get; set; }
}

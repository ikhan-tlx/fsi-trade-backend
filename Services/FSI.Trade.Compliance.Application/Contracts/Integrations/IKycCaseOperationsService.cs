namespace FSI.Trade.Compliance.Application.Contracts.Integrations;

/// <summary>
/// On-demand operational actions for a KYC case execution, exposed by the
/// Risk Assessment tab's privileged (<c>Kyc.Operate</c>) ⋮ menu. These are
/// troubleshooting tools that complement the always-on background poller
/// (<c>FccmCaseIdPoller</c>) — they act on a single <c>KycCaseRequest</c> row.
/// </summary>
public interface IKycCaseOperationsService
{
    /// <summary>
    /// Retry the FCCM synchronisation for a Failed/Timeout execution. Opens a
    /// NEW submission attempt (a new <c>KycCaseRequest</c> row → new history
    /// entry) for the same customer + transaction; the original row is left as
    /// the historical record. Throws when the source row isn't Failed/Timeout.
    /// </summary>
    Task<KycCaseSubmissionResult> RetryAsync(long requestId, CancellationToken ct = default);

    /// <summary>
    /// Force an immediate one-shot poll of an in-flight execution
    /// (Submitted / AwaitingCaseId / CaseCreated) instead of waiting for the
    /// next background tick — advances the row's state machine once. Throws
    /// when the row is already in a terminal state.
    /// </summary>
    Task<KycCaseStatusDto> RepollAsync(long requestId, CancellationToken ct = default);

    /// <summary>
    /// Re-read the FCCM risk category for a case and re-run the BRAINS ↔ FCCM
    /// reconciliation (refreshing Cstm_Brains + the customer snapshot band).
    /// Requires the case to have been submitted (an FCCM request id exists).
    /// </summary>
    Task<KycCaseStatusDto> RefreshAsync(long requestId, CancellationToken ct = default);
}

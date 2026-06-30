namespace FSI.Trade.Compliance.Application.Contracts.Integrations;

/// <summary>
/// Asynchronous KYC case lifecycle. Submission returns immediately; status
/// transitions are driven by a background poller against the upstream
/// (today: FCCM via Oracle <c>FCC_OB_RA</c>). The legacy 20-second blocking
/// poll inside <c>caseInsertionController.GetCaseIdByRequestId</c> is
/// replaced by this async surface.
/// </summary>
public interface IKycCaseService
{
    /// <summary>
    /// Persists a new KYC case request, fires the upstream submit, and
    /// returns the local request ID + initial status. Does NOT block waiting
    /// for the upstream's case ID — that's the poller's job.
    /// </summary>
    Task<KycCaseSubmissionResult> SubmitAsync(KycCaseSubmissionRequest req, CancellationToken ct = default);

    /// <summary>Reads the current state of a previously-submitted case. Returns null if request_id unknown.</summary>
    Task<KycCaseStatusDto?>      GetStatusAsync(long requestId, CancellationToken ct = default);

    /// <summary>
    /// Handles the inbound webhook from FCCM with a final action code
    /// (approve / reject). Updates local state. The endpoint that calls this
    /// is anonymous (FCCM has no JWT); authenticate the payload via
    /// <c>Auth:FccmCallbackSharedSecret</c> header.
    /// </summary>
    Task                         HandleCallbackAsync(KycCaseCallback payload, CancellationToken ct = default);
}

public class KycCaseSubmissionRequest
{
    public string  CustomerId    { get; set; } = "";
    public long?   TransactionId { get; set; }
}

public class KycCaseSubmissionResult
{
    public long    RequestId { get; set; }
    public string  Status    { get; set; } = "";    // KycCaseStatus.* constant
}

public class KycCaseStatusDto
{
    public long      RequestId       { get; set; }
    public string    Status          { get; set; } = "";
    public string?   FccmCaseId      { get; set; }
    public string?   RiskCategoryKey { get; set; }
    public DateTime  SubmittedAt     { get; set; }
    public DateTime? LastPolledAt    { get; set; }
    public string?   ErrorDetail     { get; set; }
}

public class KycCaseCallback
{
    /// <summary>
    /// Inbound FCCM case ID. Wire field is <c>caseID</c> per the legacy
    /// <c>updateTrnRequest</c> contract. For the KYC tracking row this
    /// matches <c>KycCaseRequest.FccmCaseId</c>; for Trade transactions
    /// this is matched against <c>TransactionCode</c> in
    /// <c>TmX_Transaction_Detail.UDF_Data</c>.
    /// </summary>
    public string?  CaseID       { get; set; }

    /// <summary>
    /// Legacy action code as a string ("30004", "30003", "983", "984", …).
    /// Kept as string to match the legacy <c>updateTrnRequest.actionCode</c>
    /// shape — FCCM sometimes wires these as quoted strings. The handler
    /// matches against <c>IntegrationOptions.FccmActionCodeStatusMap</c>
    /// (keyed by string) for the Trade verdict path.
    /// </summary>
    public string?  ActionCode   { get; set; }
    public string?  ActionReason { get; set; }
}

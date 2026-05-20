namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.KycCaseRequest. Tracks the lifecycle of an
/// asynchronous KYC case submission to FCCM (or whichever upstream is wired
/// behind <c>IKycCaseService</c>). The request thread INSERTs a row in
/// status <see cref="KycCaseStatus.Submitted"/> and returns immediately.
/// A <c>BackgroundService</c> drives the row through the rest of the state
/// machine without blocking any HTTP request.
/// </summary>
public class KycCaseRequest
{
    public long      Id              { get; set; }
    public int       TenantId        { get; set; } = 1;
    public string    CustomerId      { get; set; } = "";
    public long?     TransactionId   { get; set; }
    public string    SubmittedBy     { get; set; } = "";
    public DateTime  SubmittedAt     { get; set; }

    public string?   FccmRequestId   { get; set; }     // upstream's request handle
    public string?   FccmCaseId      { get; set; }     // appears once FCCM provisions the case
    public string?   RiskCategoryKey { get; set; }     // populated alongside CaseId (or in a 2nd pass)

    public string    Status          { get; set; } = KycCaseStatus.Submitted;
    public DateTime? LastPolledAt    { get; set; }
    public string?   ErrorDetail     { get; set; }

    public DateTime  LastUpdatedAt   { get; set; }
}

/// <summary>
/// Stable string codes for <see cref="KycCaseRequest.Status"/>.
/// </summary>
public static class KycCaseStatus
{
    /// <summary>Just inserted; HTTP submit to upstream not yet attempted.</summary>
    public const string Submitted      = "Submitted";

    /// <summary>HTTP submit succeeded; awaiting upstream to provision the case ID.</summary>
    public const string AwaitingCaseId = "AwaitingCaseId";

    /// <summary>Case ID retrieved; risk score not yet fetched.</summary>
    public const string CaseCreated    = "CaseCreated";

    /// <summary>Risk score captured; flow complete.</summary>
    public const string RiskAssessed   = "RiskAssessed";

    /// <summary>Upstream rejected, network error, or business rule failure.</summary>
    public const string Failed         = "Failed";

    /// <summary>Case ID never appeared within the configured timeout window.</summary>
    public const string Timeout        = "Timeout";
}

namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Customer_Review_Schedule. One row PER CUSTOMER —
/// the authoritative periodic-review schedule (current risk category + next
/// review date + status). This is a dedicated per-customer table because
/// TmX_Customer_Master is only a per-transaction snapshot, not a master-of-record.
/// The PeriodicReviewService scans this table; the FCCM callback upserts it on
/// any terminal KYC decision (approve or reject).
/// </summary>
public class CustomerReviewSchedule
{
    public int       Id                   { get; set; }
    public int       TenantId             { get; set; } = 1;
    public string    CustomerCode         { get; set; } = "";
    public string?   CurrentRiskCategory  { get; set; }   // "PEP"/"High"/"Medium"/"Low"
    public int?      CurrentRiskScore     { get; set; }   // numeric 0..3 (RISK_BAND)
    public DateTime? NextReviewDate       { get; set; }
    public string    Status               { get; set; } = ReviewScheduleStatus.Scheduled;
    public DateTime? LastReviewedDate     { get; set; }
    public int?      LastKycTransactionId { get; set; }
    public string?   LastRemark           { get; set; }
    public string    CreatedBy            { get; set; } = "";
    public DateTime  CreatedDate          { get; set; }
    public string?   LastUpdatedBy        { get; set; }
    public DateTime? LastUpdatedDate      { get; set; }
}

/// <summary>Stable status codes for <see cref="CustomerReviewSchedule.Status"/>.</summary>
public static class ReviewScheduleStatus
{
    /// <summary>Idle; awaiting its Next_Review_Date. The only state the scan selects.</summary>
    public const string Scheduled = "Scheduled";
    /// <summary>A review cycle is in flight (atomic claim).</summary>
    public const string InReview  = "InReview";
    /// <summary>A KYC was already in progress when the review became due.</summary>
    public const string Blocked   = "Blocked";
    /// <summary>The review attempt errored before a terminal decision.</summary>
    public const string Failed    = "Failed";
    /// <summary>Manual hold / opt-out.</summary>
    public const string Suspended = "Suspended";
}

namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// One unit of ASYNC auto-flag work. Maps to <c>dbo.TmX_AutoFlag_Job</c>
/// (migration 2026_07_002). Enqueued at transaction save when an aggregate
/// (cross-transaction) flag applies to the transaction's product/sub-product;
/// a background worker claims it, computes the aggregate at runtime and stages
/// <c>TmX_Transaction_Flag</c>. Non-aggregate flags are evaluated synchronously
/// at save and never enqueue.
/// </summary>
public class AutoFlagJob
{
    public int       JobId         { get; set; }
    public int       TransactionId { get; set; }
    /// <summary>Grouping key resolved at enqueue (TmX_Customer_Master.Customer_Code).</summary>
    public string?   CustomerCode  { get; set; }
    /// <summary>Save (Phase 1). Reserved for future producers (e.g. Scheduled).</summary>
    public string    TriggerType   { get; set; } = "Save";
    /// <summary>Pending | Processing | Done | Failed | DeadLetter.</summary>
    public string    Status        { get; set; } = "Pending";
    public int       Attempts      { get; set; }
    public string?   Error         { get; set; }
    public DateTime  EnqueuedDate  { get; set; }
    public DateTime? ClaimedDate   { get; set; }
    public DateTime? ProcessedDate { get; set; }
}

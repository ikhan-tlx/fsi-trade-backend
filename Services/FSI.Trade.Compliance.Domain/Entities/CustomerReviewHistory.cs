namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Customer_Review_History. Append-only audit of every
/// periodic-review attempt/outcome (Completed / Blocked / Failed / Skipped) with
/// its remark — backs the "failed with remarks" requirement and the ops view.
/// </summary>
public class CustomerReviewHistory
{
    public int       Id                  { get; set; }
    public int       TenantId            { get; set; } = 1;
    public string    CustomerCode        { get; set; } = "";
    public DateTime  DueDate             { get; set; }
    public string    Outcome             { get; set; } = "";
    public int?      SourceTransactionId { get; set; }
    public int?      NewTransactionId    { get; set; }
    public long?     KycRequestId        { get; set; }
    public string?   Remarks             { get; set; }
    public string    CreatedBy           { get; set; } = "";
    public DateTime  CreatedDate         { get; set; }
}

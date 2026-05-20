namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// Per-transaction flag value — the current state. Maps to
/// <c>dbo.TmX_Transaction_Flag</c>.
///
/// One row per (TransactionId, FlagId). Last write wins; the full
/// timeline lives in <see cref="TransactionFlagHistory"/>.
/// </summary>
public class TransactionFlag
{
    public int       TransactionFlagId     { get; set; }
    public int       TransactionId         { get; set; }
    public int       FlagId                { get; set; }
    public bool      IsFlagged             { get; set; }

    /// <summary>FK to TmX_Document — the generic file-attachment store. NULL when no evidence attached.</summary>
    public int?      EvidenceDocumentId    { get; set; }

    public string?   AnalystNotes          { get; set; }

    /// <summary>Attribution for the most recent set/clear.</summary>
    public string    SetBy                 { get; set; } = "";
    public DateTime  SetDate               { get; set; }

    public string    CreatedBy             { get; set; } = "";
    public DateTime  CreatedDate           { get; set; }
    public string?   LastUpdatedBy         { get; set; }
    public DateTime? LastUpdatedDate       { get; set; }
}

namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// Append-only audit trail for flag changes. Maps to
/// <c>dbo.TmX_Transaction_Flag_History</c>.
///
/// One row per state change. <see cref="TransactionId"/> and
/// <see cref="FlagId"/> are denormalised onto the history table so
/// audit queries don't need to JOIN back to <see cref="TransactionFlag"/>
/// (which may have been updated since the change).
///
/// Long-running tables — keep them lean. Soft-deletes on
/// TmX_Transaction_Flag don't propagate here; the history record
/// remains as evidence.
/// </summary>
public class TransactionFlagHistory
{
    public long      TransactionFlagHistoryId    { get; set; }
    public int       TransactionFlagId           { get; set; }

    /// <summary>Denormalised — speeds up per-transaction and per-flag audit queries.</summary>
    public int       TransactionId               { get; set; }
    public int       FlagId                      { get; set; }

    /// <summary>FK to TmX_Lookup row with Lookup_Type='FLAG_CHANGE_TYPE' (Set / Cleared / Evidence_Attached / Evidence_Removed / Notes_Updated).</summary>
    public int       ChangeTypeLkpId             { get; set; }

    public bool?     PreviousIsFlagged           { get; set; }
    public bool?     NewIsFlagged                { get; set; }
    public string?   PreviousNotes               { get; set; }
    public string?   NewNotes                    { get; set; }
    public int?      PreviousEvidenceDocumentId  { get; set; }
    public int?      NewEvidenceDocumentId       { get; set; }

    public string    ChangedBy                   { get; set; } = "";
    public DateTime  ChangedDate                 { get; set; }
}

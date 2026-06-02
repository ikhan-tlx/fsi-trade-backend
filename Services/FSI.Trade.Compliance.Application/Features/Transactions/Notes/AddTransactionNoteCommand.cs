using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.Notes;

/// <summary>
/// Slice 10 §7.5 — appends a note (free-text remark) to a transaction.
/// Writes one row to <c>TmX_Application_Remark</c> with
/// <c>Module_Code = "5"</c>, current user as Created_By + User_Id,
/// and the supplied <c>comments</c>.
///
/// Listing is already handled by <c>GET /Transaction/{id}.remarks</c> on
/// the detail endpoint — no new GET needed.
/// </summary>
public class AddTransactionNoteCommand : IRequest<AddTransactionNoteResult>
{
    /// <summary>Bound from the route — body shape carries no id.</summary>
    public int      transactionId { get; set; }

    /// <summary>The note body. Required, non-empty after trim.</summary>
    public string?  comments      { get; set; }

    /// <summary>
    /// Optional categorisation. Free-form string, e.g. "Submit", "Authorize",
    /// "Return". Defaults to "Note" when omitted. Matches the legacy
    /// ActionType field on TmX_Application_Remark.
    /// </summary>
    public string?  actionType    { get; set; }
}

public class AddTransactionNoteResult
{
    public int      applicationRemarkId { get; set; }
    public string?  comments            { get; set; }
    public string?  actionType          { get; set; }
    public string   userId              { get; set; } = "";
    public DateTime createdDate         { get; set; }
    public string   createdBy           { get; set; } = "";
}

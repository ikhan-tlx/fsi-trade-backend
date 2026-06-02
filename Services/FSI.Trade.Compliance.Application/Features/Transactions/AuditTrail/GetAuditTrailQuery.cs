using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.AuditTrail;

/// <summary>
/// Slice 10 §7.3 — chronological audit feed for one transaction.
///
/// Derived (no dedicated audit table) by merging three sources:
/// <list type="bullet">
///   <item><b>Transaction lifecycle stamps</b> — <c>CreatedDate</c> emits a
///         "Created" event; <c>LastUpdatedDate</c> (when distinct) emits an
///         "Updated" event.</item>
///   <item><b>Workflow transitions</b> — every row in
///         <c>WorkflowProcessTransitionHistory</c> (excluding programmatic
///         <c>SetState</c> resets) becomes a transition event with the
///         <c>TriggerName</c> as the event type and the executor as actor.</item>
///   <item><b>Remarks/notes</b> — every <c>TmX_Application_Remark</c> row
///         tied to the transaction becomes a "NoteAdded" event with the
///         remark text in the <c>remarks</c> field.</item>
/// </list>
///
/// Output is sorted chronologically (ASC). When two events fire at the
/// exact same DateTime, ties break by insertion source (lifecycle stamp
/// before workflow before note) so the feed reads predictably.
/// </summary>
public record GetAuditTrailQuery(int TransactionId) : IRequest<IReadOnlyList<AuditTrailEntryDto>>;

public class AuditTrailEntryDto
{
    /// <summary>
    /// Event categorisation. Stable set:
    /// <c>Created</c>, <c>Updated</c>, <c>Transition</c>, <c>NoteAdded</c>.
    /// For Transition events the <c>action</c> field carries the workflow
    /// trigger name (e.g. "Submit", "Pickup", "Return").
    /// </summary>
    public string    eventType  { get; set; } = "";

    /// <summary>
    /// For Transition events: the workflow command name. For NoteAdded:
    /// the remark's ActionType. Null for lifecycle stamps.
    /// </summary>
    public string?   action     { get; set; }

    /// <summary>TmX_User.User_ID of the actor. Null when the source is system-generated.</summary>
    public string?   actorId    { get; set; }

    /// <summary>Display name resolved against TmX_User. May fall back to the raw actorId when no matching user row exists.</summary>
    public string?   actorName  { get; set; }

    public DateTime  timestamp  { get; set; }

    /// <summary>Free-text remarks for NoteAdded events; null otherwise.</summary>
    public string?   remarks    { get; set; }

    /// <summary>
    /// For Transition events: "FromActivity → ToActivity" descriptor.
    /// Null otherwise. Lets the FE render the stage delta without
    /// a second lookup.
    /// </summary>
    public string?   stageChange { get; set; }
}

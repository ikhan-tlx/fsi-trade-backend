using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Transactions.WorkflowHistory;

/// <summary>
/// Slice 10 §7.1 — returns the chronological list of workflow activities
/// the transaction has passed through, with entry/exit timestamps and the
/// actor who executed each transition.
///
/// Backed by OptimaJet's <c>WorkflowProcessTransitionHistory</c> table.
/// Transitions whose <c>TriggerName = 'SetState'</c> are excluded — those
/// are programmatic state resets (e.g. cancel handler's fallback path),
/// not user-driven actions, and showing them confuses the audit-trail
/// narrative on the Transaction Detail Page.
/// </summary>
public record GetWorkflowHistoryQuery(int TransactionId) : IRequest<IReadOnlyList<WorkflowHistoryEntryDto>>;

public class WorkflowHistoryEntryDto
{
    /// <summary>1-based ordinal — first activity in chronological order is Step 1.</summary>
    public int       step          { get; set; }

    /// <summary>Activity name (e.g. "Draft", "Analyst", "Authorizer"). Maps to OptimaJet's <c>ToActivityName</c>.</summary>
    public string    stageName     { get; set; } = "";

    /// <summary>State name (e.g. "Draft", "AnalystReview"). Often matches stageName one-to-one.</summary>
    public string?   stateName     { get; set; }

    /// <summary>Display name of the actor who triggered the transition into this stage.</summary>
    public string?   actorName     { get; set; }

    /// <summary>FK-shaped actor id (TmX_User.User_ID). Useful for the FE to deduplicate or filter.</summary>
    public string?   actorId       { get; set; }

    /// <summary>Workflow command that produced this transition ("Submit", "Pickup", etc.). Null for auto transitions.</summary>
    public string?   triggerName   { get; set; }

    /// <summary>When the actor entered this stage.</summary>
    public DateTime  enteredAt     { get; set; }

    /// <summary>When the next transition fired. Null if this is the current stage.</summary>
    public DateTime? exitedAt      { get; set; }

    /// <summary>True if this is the current stage (latest row in the history).</summary>
    public bool      isCurrent     { get; set; }
}

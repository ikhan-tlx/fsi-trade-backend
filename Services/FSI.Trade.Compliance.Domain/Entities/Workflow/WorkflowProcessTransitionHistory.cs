namespace FSI.Trade.Compliance.Domain.Entities.Workflow;

/// <summary>
/// READ-ONLY projection over OptimaJet's <c>WorkflowProcessTransitionHistory</c>
/// table. One row per state transition the engine executes — set/start/commands
/// /SetState/auto-transition all land here.
///
/// OWNERSHIP: OptimaJet's runtime writes every row. Our backend only READS,
/// surfaced via Slice 10 §7.1 (workflow history) and §7.3 (audit feed).
///
/// Column shape verified against <c>ICBC_DEMO-Schema.sql</c> (live deployment):
/// <list type="bullet">
///   <item><c>ExecutorIdentityId</c> + <c>ActorIdentityId</c> are
///         <c>nvarchar(256)</c> here, not <c>uniqueidentifier</c> — they
///         match <c>TmX_User.User_ID</c> (also nvarchar) so display-name
///         resolution is a string JOIN, no Guid.Parse needed.</item>
///   <item><c>TransitionClassifier</c> categorises the transition:
///         <c>Direct</c>, <c>Reverse</c>, <c>Conditional</c>,
///         <c>NotSpecified</c>. The legacy SQL filters <c>TriggerName NOT IN
///         ('SetState')</c> to exclude programmatic resets from the
///         user-facing history — Slice 10 follows the same convention.</item>
/// </list>
/// </summary>
public class WorkflowProcessTransitionHistory
{
    public Guid     Id                   { get; set; }
    public Guid     ProcessId            { get; set; }

    /// <summary>Who triggered the transition (acts via command). String FK to TmX_User.User_ID.</summary>
    public string?  ExecutorIdentityId   { get; set; }

    /// <summary>Who the transition was performed on behalf of — usually the same as Executor. String FK to TmX_User.User_ID.</summary>
    public string?  ActorIdentityId      { get; set; }

    public string   FromActivityName     { get; set; } = "";
    public string   ToActivityName       { get; set; } = "";
    public string?  FromStateName        { get; set; }
    public string?  ToStateName          { get; set; }

    public DateTime TransitionTime       { get; set; }
    public string   TransitionClassifier { get; set; } = "";
    public bool     IsFinalised          { get; set; }

    /// <summary>Command name (e.g. "Submit", "Pickup"). Null for auto/SetState transitions.</summary>
    public string?  TriggerName          { get; set; }
}

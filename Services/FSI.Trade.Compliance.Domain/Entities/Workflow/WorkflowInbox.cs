namespace FSI.Trade.Compliance.Domain.Entities.Workflow;

/// <summary>
/// READ-ONLY projection over OptimaJet's <c>WorkflowInbox</c> table.
/// One row per (Process, Identity) tuple — the engine populates this on
/// state transitions to mark which user/role is responsible for the next
/// action on each process. The FE's Inbox page reads from this.
///
/// OWNERSHIP: OptimaJet's runtime writes here via scheme actions
/// (FillApprovers, ClearInboxByRole, etc.). Our backend only READS.
/// </summary>
public class WorkflowInbox
{
    public Guid     Id                 { get; set; }
    public Guid     ProcessId          { get; set; }

    /// <summary>
    /// v21 in this deployment: IdentityId is <c>uniqueidentifier</c>, NOT
    /// nvarchar. Caller's <c>ICurrentUserService.UserId</c> is a string;
    /// handlers must Guid.Parse it before filtering.
    /// </summary>
    public Guid     IdentityId         { get; set; }

    /// <summary>Optional in some v21 deployments — not mapped if absent. See EF config.</summary>
    public DateTime? AddingDate        { get; set; }

    public string?  AvailableCommands  { get; set; }
}

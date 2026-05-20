namespace FSI.Trade.Compliance.Domain.Entities.Workflow;

/// <summary>
/// READ-ONLY projection over OptimaJet's <c>WorkflowProcessInstance</c> table.
/// One row per running / paused / completed workflow process. State machine
/// position lives in <see cref="StateName"/> + <see cref="ActivityName"/>.
///
/// OWNERSHIP: OptimaJet's runtime writes here — every <c>CreateInstance</c>
/// / <c>ExecuteCommand</c> / <c>SetState</c> mutates this table. Our backend
/// only READS for display (Inbox columns, GetProcessState).
///
/// v21 NOTES: scheme association is via <see cref="SchemeId"/> (GUID FK to
/// <c>WorkflowProcessScheme.Id</c>). There is NO <c>SchemeCode</c> column on
/// this table — to get the scheme code, JOIN to WorkflowProcessScheme.
/// </summary>
public class WorkflowProcessInstance
{
    public Guid     Id                          { get; set; }
    public Guid     SchemeId                    { get; set; }
    public string?  StateName                   { get; set; }
    public string?  ActivityName                { get; set; }
    public string?  PreviousState               { get; set; }
    public string?  PreviousActivity            { get; set; }
    public string?  PreviousActivityForDirect   { get; set; }
    public string?  PreviousActivityForReverse  { get; set; }
    public string?  PreviousStateForDirect      { get; set; }
    public string?  PreviousStateForReverse     { get; set; }
    public Guid?    ParentProcessId             { get; set; }
    public Guid?    RootProcessId               { get; set; }
    public string?  SubprocessName              { get; set; }
    public string?  StartingTransition          { get; set; }
    public string?  CalendarName                { get; set; }
    public DateTime? LastTransitionDate         { get; set; }
    public string?  TenantId                    { get; set; }
}

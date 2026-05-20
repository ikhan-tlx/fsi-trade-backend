namespace FSI.Trade.Compliance.Application.Contracts.Workflow;

/// <summary>
/// Domain-named port over a workflow runtime. ONLY exposes vendor primitives —
/// operations that require the engine itself (transitions, command resolution,
/// scheme parsing, designer protocol). DB-only queries (Inbox feed, scheme
/// list, product mapping reads) live in MediatR handlers and talk directly to
/// <c>IApplicationDbContext</c>, NOT through this interface.
///
/// This split honours the layering rule (see API_GUIDELINES §14a):
/// orchestration in Application, vendor specifics in Infrastructure.
/// Today's implementation (<c>OptimaJetWorkflowEngine</c>) wraps OptimaJet
/// WorkflowEngine.NETCore; a future swap to Elsa or Workflow Core only
/// touches the impl, not handlers.
/// </summary>
public interface IWorkflowEngine
{
    // ---------- Process lifecycle (runtime-bound) ----------

    /// <summary>
    /// Instantiates a new workflow process for the given scheme.
    /// <paramref name="processId"/> is server-issued (typically the matching
    /// Transaction's ProcessInstanceId so subsequent advance calls hit the
    /// same instance).
    /// </summary>
    Task CreateInstanceAsync(string schemeCode, Guid processId, string identityId,
                             IDictionary<string, object?>? parameters = null,
                             CancellationToken ct = default);

    /// <summary>
    /// Advances the process by executing the named command. Vendor decides
    /// transition + actions per scheme XML. <paramref name="comments"/> is
    /// optional, surfaced in transition history.
    /// </summary>
    Task<WorkflowExecutionResult> ExecuteCommandAsync(Guid processId, string identityId, string command,
                                                     string? comments = null,
                                                     IDictionary<string, object?>? parameters = null,
                                                     CancellationToken ct = default);

    /// <summary>Forces a state transition (admin recovery). Bypasses scheme rules — use sparingly.</summary>
    Task SetStateAsync(Guid processId, string state, string identityId, string? reason = null,
                       CancellationToken ct = default);

    // ---------- Read primitives (runtime-bound) ----------

    /// <summary>List of commands the named identity can fire on this process right now.</summary>
    Task<IReadOnlyList<WorkflowCommand>> GetAvailableCommandsAsync(Guid processId, string identityId,
                                                                  CancellationToken ct = default);

    /// <summary>
    /// Returns the engine's current activity name for a process. Pure runtime
    /// call. Used in conjunction with a DB-level state read in any handler
    /// that needs "what's the process doing right now". Returns null if the
    /// engine can't resolve the process.
    /// </summary>
    Task<string?> GetCurrentActivityNameAsync(Guid processId, CancellationToken ct = default);

    // ---------- Designer proxy ----------

    /// <summary>
    /// Routes inbound query/form data to the underlying engine's designer
    /// API. Used by the Process Designer FE module to load/save scheme XML
    /// via the engine's built-in protocol. Body-bytes contain the engine's
    /// JSON response — pass through to the FE verbatim.
    /// </summary>
    Task<WorkflowDesignerResponse> InvokeDesignerAsync(IDictionary<string, string> formParams,
                                                      Stream? requestBody,
                                                      CancellationToken ct = default);
}

// ---------- DTOs ----------

public class WorkflowCommand
{
    public string  Name             { get; set; } = "";
    public string? VisibleName      { get; set; }
    public string? Classifier       { get; set; }      // "Direct" | "Reverse" | "NotSpecified"
    public string? ParameterName    { get; set; }
    public bool    IsDeviation      { get; set; }

    /// <summary>
    /// When the scheme defines a <c>ManualActors</c> parameter on this command,
    /// the FE must pop a "pick recipient role" dialog before firing. This is
    /// the parsed list of candidate actors from that parameter's default value.
    /// Legacy field name was <c>TmxWorkflowReceivingActor</c>.
    /// Null / empty when the command doesn't need manual actor selection.
    /// </summary>
    public List<WorkflowActorRef>? ReceivingActors { get; set; }
}

/// <summary>
/// FE-facing actor reference. Mirrors legacy <c>WorkflowActorViewModel</c>:
/// either role-based (<see cref="ActorRole"/> set) or user-type-based
/// (<see cref="ActorType"/> set, <see cref="ActorRole"/> null).
/// </summary>
public class WorkflowActorRef
{
    public string? ActorRole                       { get; set; }
    public string? ActorType                       { get; set; }
    public string? ActorId                         { get; set; }
    public bool    IsFillVerificationUserInChecklist { get; set; }
    public bool    IsActorRoleType => !string.IsNullOrWhiteSpace(ActorRole);
}

public class WorkflowProcessState
{
    public Guid    ProcessId    { get; set; }
    public string  SchemeCode   { get; set; } = "";
    public string  StateName    { get; set; } = "";
    public string? ActivityName { get; set; }
}

public class WorkflowScheme
{
    public string Code     { get; set; } = "";
    public string? Display { get; set; }
}

public class WorkflowExecutionResult
{
    public Guid    ProcessId   { get; set; }
    public bool    WasExecuted { get; set; }
    public string? PreviousState { get; set; }
    public string? CurrentState  { get; set; }
    public string? Comment       { get; set; }
}

public class WorkflowInboxItem
{
    public Guid     ProcessId       { get; set; }
    public string?  SchemeCode      { get; set; }
    public string?  StateName       { get; set; }
    public string?  ApplicationType { get; set; }
    public string?  CustomerName    { get; set; }
    public string?  Status          { get; set; }
    public DateTime CreatedDate     { get; set; }
}

public class WorkflowDesignerResponse
{
    public string ContentType { get; set; } = "application/json";
    public byte[] Body        { get; set; } = Array.Empty<byte>();
}

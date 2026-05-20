using System.Collections.Specialized;
using System.Text;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptimaJet.Workflow;            // v3.5: DesignerAPI lives as an extension method in this top-level namespace
using OptimaJet.Workflow.Core.Model;
using OptimaJet.Workflow.Core.Runtime;

// Disambiguate: OptimaJet.Workflow.Core.Runtime.WorkflowCommand vs our
// Application.Contracts.Workflow.WorkflowCommand DTO. Two aliases — one
// per side — keep every `WorkflowCommand` reference unambiguous regardless
// of which namespace's using directive is in scope.
using AppWorkflowCommand    = FSI.Trade.Compliance.Application.Contracts.Workflow.WorkflowCommand;
using VendorWorkflowCommand = OptimaJet.Workflow.Core.Runtime.WorkflowCommand;

namespace FSI.Trade.Compliance.Infrastructure.Workflow;

/// <summary>
/// OptimaJet-backed <see cref="IWorkflowEngine"/> implementation pinned to
/// OptimaJet 3.5.0 (Slice 5.6). ONLY exposes vendor primitives — operations
/// that genuinely need the WorkflowRuntime. DB-only LINQ (Inbox feed, scheme
/// list, product mapping) lives in MediatR handlers and talks directly to
/// <c>IApplicationDbContext</c>.
///
/// Vendor names (OptimaJet, WorkflowRuntime, ProcessInstance, etc.) appear
/// ONLY in this file plus the action/rule providers and the factory —
/// nowhere else in the codebase.
///
/// v3.5 API surface (verified against the working sample app):
///   • <c>WorkflowCommand.Parameters</c> is a list of <see cref="ParameterDefinitionWithValue"/>
///     with <c>.ParameterName</c> + <c>.DefaultValue</c>. Mutation goes
///     through <c>WorkflowCommand.SetParameter(name, value)</c>.
///   • <c>WorkflowCommand.Classifier</c> is a <see cref="TransitionClassifier"/> enum
///     (Direct / Reverse / NotSpecified) — must be stringified for the FE.
///   • <see cref="WorkflowRuntime.SetState"/> uses a positional signature
///     <c>(processId, actor, executor, stateName, parameters)</c> — there is
///     no <c>SetStateParams</c> object in v3.5.
///   • Sync <c>DesignerAPI(NameValueCollection, Stream, bool)</c> exists as
///     an extension method in the top-level <c>OptimaJet.Workflow</c>
///     namespace (hence the using directive above).
///   • Current state / activity name come from sync
///     <c>GetCurrentState(processId)?.Name</c> and <c>GetCurrentActivityName(processId)</c>.
/// </summary>
public class OptimaJetWorkflowEngine : IWorkflowEngine
{
    private readonly WorkflowRuntimeFactory             _factory;
    private readonly WorkflowOptions                    _opt;
    private readonly ILogger<OptimaJetWorkflowEngine>   _log;

    public OptimaJetWorkflowEngine(
        WorkflowRuntimeFactory             factory,
        IOptions<WorkflowOptions>          opt,
        ILogger<OptimaJetWorkflowEngine>   log)
    {
        _factory = factory;
        _opt     = opt.Value;
        _log     = log;
    }

    private WorkflowRuntime R => _factory.Runtime;

    // ---------- Process lifecycle ----------

    public async Task CreateInstanceAsync(string schemeCode, Guid processId, string identityId,
                                          IDictionary<string, object?>? parameters = null,
                                          CancellationToken ct = default)
    {
        var initial = new CreateInstanceParams(schemeCode, processId)
        {
            IdentityId             = identityId,
            ImpersonatedIdentityId = identityId
        };

        // v3.5 quirk: CreateInstanceParams.InitialProcessParameters is NOT
        // auto-initialised to an empty dictionary in this build. Writing to
        // it raises NRE on the first set. v21 fixed that. Defensive-init
        // before we attempt any writes — and only when we actually have
        // parameters to pass (avoids allocating an empty dict unnecessarily).
        if (parameters is { Count: > 0 })
        {
            initial.InitialProcessParameters ??= new Dictionary<string, object>();
            foreach (var kv in parameters)
            {
                if (kv.Value is null) continue;            // skip nulls; the engine doesn't store them anyway
                initial.InitialProcessParameters[kv.Key] = kv.Value;
            }
        }

        await R.CreateInstanceAsync(initial);
    }

    public async Task<WorkflowExecutionResult> ExecuteCommandAsync(
        Guid processId, string identityId, string command,
        string? comments = null, IDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var available = await R.GetAvailableCommandsAsync(processId, identityId);
        var match     = available.FirstOrDefault(c =>
                            string.Equals(c.CommandName, command, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"Command '{command}' is not available on process {processId} for identity {identityId}.");

        // v3.5: WorkflowCommand.Parameters supports named indexer access —
        // surface caller's `comments` into the "Comments" slot if the scheme
        // declared one, and overlay any additional caller-supplied params.
        if (!string.IsNullOrWhiteSpace(comments) &&
            match.Parameters.Any(p => string.Equals(p.ParameterName, "Comments", StringComparison.OrdinalIgnoreCase)))
        {
            match.SetParameter("Comments", comments);
        }

        if (parameters is { Count: > 0 })
        {
            foreach (var kv in parameters)
            {
                if (match.Parameters.Any(p => string.Equals(p.ParameterName, kv.Key, StringComparison.OrdinalIgnoreCase)))
                    match.SetParameter(kv.Key, kv.Value);
            }
        }

        // v3.5: sync GetCurrentState(processId) → WorkflowState (.Name is the state).
        // Matches the working sample app's pattern in InvoiceController/ClaimController.
        var prevState = R.GetCurrentState(processId)?.Name ?? "";
        var result    = await R.ExecuteCommandAsync(match, identityId, identityId);
        var newState  = R.GetCurrentState(processId)?.Name ?? prevState;

        return new WorkflowExecutionResult
        {
            ProcessId     = processId,
            WasExecuted   = result.WasExecuted,
            PreviousState = prevState,
            CurrentState  = newState,
            Comment       = comments
        };
    }

    public Task SetStateAsync(Guid processId, string state, string identityId, string? reason = null, CancellationToken ct = default)
    {
        // v3.5: positional API (no SetStateParams object). Signature is
        // (processId, actorIdentityId, executorIdentityId, stateName, parameters).
        // The reason text doesn't have a dedicated slot here — we surface it
        // as a parameter named "Comments" so it appears in transition history
        // when the scheme reads that parameter. Matches the legacy convention
        // in the sample app's RequestController.
        var parameters = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(reason))
            parameters["Comments"] = reason;
        else
            _log.LogInformation("SetStateAsync: no reason supplied for process {ProcessId} → state '{State}'.", processId, state);

        R.SetState(processId, identityId, identityId, state, parameters);
        return Task.CompletedTask;
    }

    // ---------- Read primitives ----------

    public async Task<IReadOnlyList<AppWorkflowCommand>> GetAvailableCommandsAsync(Guid processId, string identityId, CancellationToken ct = default)
    {
        var raw = await R.GetAvailableCommandsAsync(processId, identityId);

        // Scheme-bound parameter names — match legacy
        // TMX.Common.Constants.{ManualActorParameter, IsDeviationParameter}.
        const string manualActorsParam = "ManualActors";
        const string isDeviationParam  = "IsDeviation";

        return raw.Select(c => new AppWorkflowCommand
        {
            Name            = c.CommandName,
            VisibleName     = c.LocalizedName,
            Classifier      = c.Classifier.ToString(),                                       // v3.5: TransitionClassifier enum → string
            ParameterName   = c.Parameters.Select(p => p.ParameterName).FirstOrDefault(),    // legacy parity — first param name
            IsDeviation     = ParseIsDeviation(c, isDeviationParam),
            ReceivingActors = ParseManualActors(c, manualActorsParam)
        }).ToList();
    }

    /// <summary>
    /// Reads the boolean stored on the scheme command's <c>IsDeviation</c>
    /// parameter (legacy <c>Constants.IsDeviationParameter</c>). Treats null
    /// / unparseable as false. Matches legacy
    /// <c>Convert.ToBoolean(workflowCommand.GetParameterValueOrDefault("IsDeviation") ?? false)</c>.
    /// </summary>
    private static bool ParseIsDeviation(VendorWorkflowCommand c, string paramName)
    {
        var defaultValue = c.Parameters
            .FirstOrDefault(p => string.Equals(p.ParameterName?.Trim(), paramName, StringComparison.OrdinalIgnoreCase))
            ?.DefaultValue?.ToString();

        return !string.IsNullOrWhiteSpace(defaultValue)
            && bool.TryParse(defaultValue, out var b)
            && b;
    }

    /// <summary>
    /// If the scheme command declares a <c>ManualActors</c> parameter (legacy
    /// <c>Constants.ManualActorParameter</c>), deserialise its default value
    /// (a JSON array) into our <see cref="WorkflowActorRef"/> list. The FE
    /// uses this list to show a "pick a recipient role" dialog before firing
    /// the command. Returns null when the parameter is absent or unparseable.
    /// </summary>
    private static List<WorkflowActorRef>? ParseManualActors(VendorWorkflowCommand c, string paramName)
    {
        var raw = c.Parameters
            .FirstOrDefault(p => string.Equals(p.ParameterName?.Trim(), paramName, StringComparison.OrdinalIgnoreCase))
            ?.DefaultValue?.ToString();

        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            var actors = JsonSerializer.Deserialize<List<WorkflowActorRef>>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return actors is { Count: > 0 } ? actors : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task<string?> GetCurrentActivityNameAsync(Guid processId, CancellationToken ct = default)
    {
        try
        {
            // v3.5: sync — returns the activity name string directly.
            return Task.FromResult<string?>(R.GetCurrentActivityName(processId));
        }
        catch
        {
            // Engine may throw for unknown / not-loaded process. Return null
            // so the caller can decide how to surface it. Handler-level code
            // in GetTransactionByIdQueryHandler also catches so the page
            // continues to render with an empty workflow snapshot.
            return Task.FromResult<string?>(null);
        }
    }

    // ---------- Designer proxy ----------

    /// <summary>
    /// Slice 5.6 — wired against v3.5's sync DesignerAPI. Translates the
    /// HTTP-shaped inputs (form params + optional body stream) into the
    /// <see cref="NameValueCollection"/> the legacy designer endpoint expects.
    /// Returns the response body as bytes with <c>application/json</c>
    /// content-type — matches the FE designer module's expectation.
    /// </summary>
    public Task<WorkflowDesignerResponse> InvokeDesignerAsync(IDictionary<string, string> formParams, Stream? requestBody, CancellationToken ct = default)
    {
        var pars = new NameValueCollection();
        foreach (var kv in formParams)
            pars.Add(kv.Key, kv.Value);

        // v3.5 DesignerAPI signature: (NameValueCollection pars, Stream body, bool returnAsJson) → string
        var json = R.DesignerAPI(pars, requestBody, true);

        // The legacy designer sometimes returns null (e.g. for actions that don't
        // produce a payload); the FE expects an empty array in that case.
        if (json == null)
            json = "[]";

        return Task.FromResult(new WorkflowDesignerResponse
        {
            ContentType = "application/json",
            Body        = Encoding.UTF8.GetBytes(json)
        });
    }
}

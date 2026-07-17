using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Application.Common.Logging;

/// <summary>
/// Cross-cutting structured workflow logger. Gives any business workflow a
/// consistent, greppable execution trail:
///
///   [START]   Trade received
///   [STEP]    Customer loaded
///   [CALL]    PR_CREATE_EVENT_DEMO …            (before an external call)
///   [CALL]    PR_CREATE_EVENT_DEMO completed in 180 ms.
///   [SUCCESS] Case created
///   [SUMMARY] TxId=7767 Case=CA-123 Flags=8
///   [END]     FCCM Trade Case completed in 2103 ms (9 steps, 0 warnings).
///
/// or, on failure:
///
///   [FAILED]  Stored procedure execution — Reason: Duplicate Message Id
///   [END]     FCCM Trade Case FAILED in 240 ms.
///
/// Design notes:
/// <list type="bullet">
///   <item>Correlation fields (Workflow + supplied keys such as TransactionId)
///     are pushed as an <see cref="ILogger"/> scope, so every line emitted
///     anywhere inside the workflow automatically carries them (Serilog
///     promotes scope state to structured properties). The per-request
///     RequestId / CorrelationId / UserId come from the request-scope
///     middleware and stack on top.</item>
///   <item>Message templates are constant (<c>{Message}</c>, <c>{Name}</c>…)
///     so the logging analyzer (CA2254) stays quiet and every field is
///     structured/searchable; callers pass already-interpolated text.</item>
///   <item><see cref="Detail"/> is verbose-only (Debug), gated by
///     <see cref="EnableDetailedLogs"/> — bind it from
///     <c>Integration:Diagnostics:EnableDetailedLogs</c>. Use it for payloads,
///     variable values and decision paths that must not bloat production logs
///     or leak sensitive data.</item>
///   <item>The scope is <see cref="IDisposable"/>: wrap the workflow in
///     <c>using var wf = WorkflowLog.Begin(...)</c> and the trail always
///     terminates with an <c>[END]</c> even if an exception bubbles out
///     without an explicit <see cref="EndSuccess"/> / <see cref="EndFailure"/>.</item>
/// </list>
/// </summary>
public static class WorkflowLog
{
    /// <summary>
    /// Verbose master switch — bound once at start-up from
    /// <c>Integration:Diagnostics:EnableDetailedLogs</c>. When false,
    /// <see cref="WorkflowLogScope.Detail"/> is a no-op.
    /// </summary>
    public static bool EnableDetailedLogs { get; set; }

    /// <summary>Begin a workflow trail. Supply the stable identifiers as keys
    /// (e.g. <c>("TransactionId", txId)</c>) — they enrich every line.</summary>
    public static WorkflowLogScope Begin(ILogger logger, string workflow, params (string Key, object? Value)[] keys)
        => new(logger, workflow, keys);
}

/// <summary>One live workflow trail. Not thread-safe — one per logical flow.</summary>
public sealed class WorkflowLogScope : IDisposable
{
    private readonly ILogger _log;
    private readonly string _workflow;
    private readonly IDisposable? _scope;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private int _steps;
    private int _warnings;
    private bool _ended;

    internal WorkflowLogScope(ILogger log, string workflow, (string Key, object? Value)[] keys)
    {
        _log = log;
        _workflow = workflow;

        var state = new Dictionary<string, object> { ["Workflow"] = workflow };
        foreach (var (k, v) in keys)
            if (v is not null) state[k] = v;

        _scope = _log.BeginScope(state);
        _log.LogInformation("[START] {Workflow}", workflow);
    }

    /// <summary>A meaningful business step reached.</summary>
    public void Step(string message) => _log.LogInformation("[STEP] {Message}", message);

    /// <summary>
    /// High-volume trace detail — full stored-procedure parameter values,
    /// onboarding URL + request payload + response body, decision paths.
    /// Emitted at <b>Debug</b>: it surfaces ONLY when the module's log level is
    /// set to Debug in appsettings (<c>Integration:Diagnostics:Files:*:Level</c>).
    /// At the default Information level Serilog filters it out, so the milestone
    /// trail stays lean and these appear only when deep tracing is switched on.
    /// </summary>
    public void Detail(string message) => _log.LogDebug("[DETAIL] {Message}", message);

    /// <summary>An intermediate success milestone.</summary>
    public void Success(string message) => _log.LogInformation("[SUCCESS] {Message}", message);

    /// <summary>A recoverable problem — counted in the completion summary.</summary>
    public void Warn(string message)
    {
        _warnings++;
        _log.LogWarning("[WARN] {Message}", message);
    }

    /// <summary>A step failed. Logs at Error with the operation, reason and
    /// (optionally) the exception's full chain + stack trace.</summary>
    public void Failed(string step, string reason, Exception? ex = null)
        => _log.LogError(ex, "[FAILED] {Step} — Reason: {Reason}", step, reason);

    /// <summary>
    /// Wrap an external call (DB / stored procedure / HTTP API / queue / Oracle
    /// FCCM / file / cache). Logs "[CALL] {name} …" before, then either
    /// "[CALL] {name} completed in N ms." or, on throw, "[CALL] {name} FAILED
    /// after N ms: {error}" (Error level, full exception) before rethrowing.
    /// </summary>
    public async Task<T> ExternalAsync<T>(string name, Func<Task<T>> call)
    {
        _log.LogInformation("[CALL] {Name} …", name);
        var t = Stopwatch.StartNew();
        try
        {
            var result = await call();
            t.Stop();
            _log.LogInformation("[CALL] {Name} completed in {Elapsed} ms.", name, t.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            t.Stop();
            _log.LogError(ex, "[CALL] {Name} FAILED after {Elapsed} ms: {Error}", name, t.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <summary>Void overload of <see cref="ExternalAsync{T}"/>.</summary>
    public Task ExternalAsync(string name, Func<Task> call)
        => ExternalAsync(name, async () => { await call(); return true; });

    /// <summary>Close the trail successfully. Optional one-line summary is
    /// emitted first (already-interpolated). Idempotent.</summary>
    public void EndSuccess(string? summary = null)
    {
        if (_ended) return;
        _ended = true;
        _sw.Stop();
        if (!string.IsNullOrWhiteSpace(summary))
            _log.LogInformation("[SUMMARY] {Summary}", summary);
        _log.LogInformation("[END] {Workflow} completed in {Elapsed} ms ({Steps} steps, {Warnings} warnings).",
            _workflow, _sw.ElapsedMilliseconds, _steps, _warnings);
    }

    /// <summary>Close the trail as failed, naming the step that stopped it and
    /// the reason. Idempotent.</summary>
    public void EndFailure(string failedStep, string reason)
    {
        if (_ended) return;
        _ended = true;
        _sw.Stop();
        _log.LogError("[END] {Workflow} FAILED in {Elapsed} ms. Failed step: {Step} — Reason: {Reason}",
            _workflow, _sw.ElapsedMilliseconds, failedStep, reason);
    }

    public void Dispose()
    {
        if (!_ended)
        {
            _sw.Stop();
            // Neither EndSuccess nor EndFailure was called — the flow exited
            // abnormally (an exception bubbled up). Always terminate the trail.
            _log.LogWarning("[END] {Workflow} ended without explicit completion after {Elapsed} ms " +
                            "(likely an exception — see the error above).", _workflow, _sw.ElapsedMilliseconds);
        }
        _scope?.Dispose();
    }
}

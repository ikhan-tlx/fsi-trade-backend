using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Notifications;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Sla;

/// <summary>
/// SLA-breach NOTIFICATION scanner. Best-effort background service that finds
/// open work items (rows in <c>WorkflowInbox</c> — a row exists only while the
/// process is unfinished; finalising deletes them) whose SLA clock has crossed
/// the at-risk or breach line, and raises <c>sla.atrisk</c> / <c>sla.breached</c>
/// notifications via the existing emitter (<see cref="INotificationService"/>).
/// It never writes to business data and adds no table/migration (reuses
/// TmX_Notification). Modeled on <c>PasswordExpiryScanService</c>: scope-per-tick,
/// per-tick try/catch, config-driven interval, hot-reload via IOptionsMonitor.
///
/// <para>SAFETY: the scan is read-only on business data and never throws out of
/// its loop — a failed pass is logged and retried on the next interval.</para>
///
/// <para>Idempotent per stage: dedupes by GroupKey (which encodes the clock-start
/// stamp) so re-running a pass — or tightening the interval — never double-notifies
/// the same transaction for the same SLA state entry.</para>
///
/// The SLA clock start reuses the same "entered current state" time the Inbox
/// listing computes: <c>MAX(WorkflowProcessTransitionHistory.TransitionTime)</c>
/// per process EXCLUDING <c>TriggerName == "SetState"</c>, falling back to the
/// transaction's <c>CreatedDate</c> when a process has no such transition rows.
/// </summary>
internal sealed class SlaBreachScanService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<SlaOptions> _opt;
    private readonly ILogger<SlaBreachScanService> _log;

    public SlaBreachScanService(
        IServiceProvider sp,
        IOptionsMonitor<SlaOptions> opt,
        ILogger<SlaBreachScanService> log)
    { _sp = sp; _opt = opt; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var startup = _opt.CurrentValue.StartupDelay;
        _log.LogInformation("SlaBreachScanService starting. Interval = {Interval}, StartupDelay = {Delay}, Enabled = {Enabled}.",
            _opt.CurrentValue.ScanInterval, startup, _opt.CurrentValue.Enabled);

        // Let the app settle before the first scan pass.
        try { await Task.Delay(startup, stop); }
        catch (TaskCanceledException) { return; /* shutting down */ }

        while (!stop.IsCancellationRequested)
        {
            var o = _opt.CurrentValue;
            try
            {
                if (o.Enabled)
                {
                    using var scope = _sp.CreateScope();
                    await RunScanAsync(scope.ServiceProvider, o, stop);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "SlaBreachScanService tick failed; will retry after the interval.");
            }

            try { await Task.Delay(o.ScanInterval, stop); }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }

    /// <summary>One scan pass over all open inbox work items. Read-only on
    /// business data; only INSERTs notifications (via the emitter).</summary>
    private async Task RunScanAsync(IServiceProvider services, SlaOptions o, CancellationToken ct)
    {
        var db     = services.GetRequiredService<IApplicationDbContext>();
        var notify = services.GetRequiredService<INotificationService>();
        var priv   = services.GetRequiredService<IPrivilegeService>();

        // ---------- Load open work items ----------
        // Same join shape as the Inbox listing (WorkflowInbox → ProcessInstance
        // → Transaction via proc.RootProcessId == tx.ProcessInstanceId) but
        // WITHOUT the per-user filter — every open inbox row is a candidate.
        // A transaction may appear on multiple rows (several assignees / roles);
        // we group by transaction below.
        var openItems = await (
            from inbox in db.WorkflowInboxes.AsNoTracking()
            join proc in db.WorkflowProcessInstances.AsNoTracking()
                on inbox.ProcessId equals proc.Id
            join tx in db.Transactions.AsNoTracking()
                on proc.RootProcessId equals tx.ProcessInstanceId
            select new
            {
                ProcId = proc.Id,
                tx.TransactionId,
                tx.TransactionNumber,
                tx.ClientReferenceNumber,
                tx.CreatedDate,
                tx.TenantId,
                AssigneeId = inbox.IdentityId
            }).ToListAsync(ct);

        if (openItems.Count == 0)
            return;

        // Clock start per process = MAX transition time (excluding 'SetState').
        // Loaded as a second, EF-translatable grouped query keyed by ProcessId —
        // the main join above stays translatable and this stays a single round-trip.
        var processIds = openItems.Select(r => r.ProcId).Distinct().ToList();
        var maxTransition = await db.WorkflowProcessTransitionHistories.AsNoTracking()
            .Where(h => processIds.Contains(h.ProcessId) && h.TriggerName != "SetState")
            .GroupBy(h => h.ProcessId)
            .Select(g => new { ProcessId = g.Key, MaxTime = g.Max(h => (DateTime?)h.TransitionTime) })
            .ToListAsync(ct);
        var maxTransitionByProcess = maxTransition.ToDictionary(x => x.ProcessId, x => x.MaxTime);

        var now    = DateTime.UtcNow;
        var window = TimeSpan.FromHours(o.DefaultHours);

        // Escalation audience (breach only), fetched lazily and at most once per
        // pass — and ONLY when EscalateToApprovers is enabled. There is no
        // per-user supervisor field, so the "Requests.Approve" group — the
        // approvers/supervisors — is the best-available escalation target.
        List<string>? approvers = null;

        int breachedRaised = 0, atRiskRaised = 0, txnCount = 0;

        foreach (var g in openItems.GroupBy(r => r.TransactionId))
        {
            if (ct.IsCancellationRequested) break;
            txnCount++;

            var first = g.First();
            var txnId = g.Key;

            // clock start = latest "entered current state" time across the txn's
            // processes; fall back to the transaction's created date.
            DateTime? maxStart = null;
            foreach (var r in g)
            {
                if (maxTransitionByProcess.TryGetValue(r.ProcId, out var t) && t.HasValue
                    && (maxStart is null || t.Value > maxStart.Value))
                    maxStart = t.Value;
            }
            var clockStart = maxStart ?? first.CreatedDate;

            var dueAt      = clockStart + window;
            var atRiskAt   = clockStart + window * (o.AtRiskPercent / 100.0);
            var clockStamp = clockStart.ToString("yyyyMMddHHmm");

            // Assignees = distinct inbox IdentityIds for this transaction.
            var assignees = g.Select(r => r.AssigneeId.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (assignees.Count == 0) continue; // no one to notify

            // Display ref: TransactionNumber, else ClientReferenceNumber, else id.
            var refText = !string.IsNullOrWhiteSpace(first.TransactionNumber)     ? first.TransactionNumber!
                        : !string.IsNullOrWhiteSpace(first.ClientReferenceNumber) ? first.ClientReferenceNumber!
                        : txnId.ToString();
            int? tenantId = first.TenantId;
            var deepLink  = $"/transactions/trade/{txnId}";

            // Breached only within the recent window: a breach whose dueAt is
            // more than MaxBreachAgeHours in the past is stale/abandoned (typical
            // of ancient never-completed items) and is skipped entirely.
            if (now >= dueAt && dueAt >= now.AddHours(-o.MaxBreachAgeHours))
            {
                // Breached — notify assignees, plus (only when EscalateToApprovers
                // is enabled) the approver/supervisor escalation group.
                var key = $"sla.breached:{txnId}:{clockStamp}";
                if (!await db.Notifications.AsNoTracking().AnyAsync(n => n.GroupKey == key, ct))
                {
                    IEnumerable<string> recipientSource = assignees;
                    if (o.EscalateToApprovers)
                    {
                        approvers ??= await priv.GetUserIdsWithPrivilegeAsync("Requests.Approve", ct);
                        recipientSource = assignees.Concat(approvers);
                    }
                    var recipients = recipientSource
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    await notify.RaiseAsync(
                        recipients, "sla.breached", "critical",
                        $"SLA breached: transaction {refText}",
                        $"This item has been awaiting action for over {o.DefaultHours}h.",
                        "Transaction", refText, deepLink,
                        null, key, tenantId, ct);
                    breachedRaised++;
                }
            }
            else if (now >= atRiskAt)
            {
                // At risk (not yet breached) — assignees only.
                var key = $"sla.atrisk:{txnId}:{clockStamp}";
                if (!await db.Notifications.AsNoTracking().AnyAsync(n => n.GroupKey == key, ct))
                {
                    await notify.RaiseAsync(
                        assignees, "sla.atrisk", "warning",
                        $"SLA at risk: transaction {refText}",
                        $"Action needed soon — approaching the {o.DefaultHours}h deadline.",
                        "Transaction", refText, deepLink,
                        null, key, tenantId, ct);
                    atRiskRaised++;
                }
            }
        }

        if (breachedRaised > 0 || atRiskRaised > 0)
            _log.LogInformation(
                "SlaBreachScanService: raised {Breached} breached + {AtRisk} at-risk notification(s) across {Count} open transaction(s).",
                breachedRaised, atRiskRaised, txnCount);
    }
}

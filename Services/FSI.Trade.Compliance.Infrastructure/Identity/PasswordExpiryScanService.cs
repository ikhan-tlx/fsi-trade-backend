using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Notifications;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

/// <summary>
/// Phase 3 — daily password-expiry NOTIFICATION scanner. Best-effort background
/// service that raises <c>password.expiring</c> / <c>password.expired</c>
/// notifications via the existing emitter (<see cref="INotificationService"/>);
/// it never writes to the user rows and adds no table/migration (reuses
/// TmX_Notification). Modeled on <c>PeriodicReviewService</c>: scope-per-tick,
/// per-tick try/catch, config-driven interval. Gated on BOTH
/// <see cref="PasswordExpiryScanOptions.Enabled"/> and
/// <see cref="PasswordPolicyOptions.EnforceExpiry"/> (both hot-reload via
/// IOptionsMonitor). Idempotent per stage: dedupes by GroupKey so re-running a
/// day (or a tighter interval) never double-notifies the same threshold.
/// </summary>
internal sealed class PasswordExpiryScanService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<PasswordExpiryScanOptions> _scanOpt;
    private readonly IOptionsMonitor<PasswordPolicyOptions> _policy;
    private readonly ILogger<PasswordExpiryScanService> _log;

    public PasswordExpiryScanService(
        IServiceProvider sp,
        IOptionsMonitor<PasswordExpiryScanOptions> scanOpt,
        IOptionsMonitor<PasswordPolicyOptions> policy,
        ILogger<PasswordExpiryScanService> log)
    { _sp = sp; _scanOpt = scanOpt; _policy = policy; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var startup = _scanOpt.CurrentValue.StartupDelay;
        _log.LogInformation("PasswordExpiryScanService starting. Interval = {Interval}, StartupDelay = {Delay}, Enabled = {Enabled}.",
            _scanOpt.CurrentValue.ScanInterval, startup, _scanOpt.CurrentValue.Enabled);

        // Let the app settle before the first scan pass.
        try { await Task.Delay(startup, stop); }
        catch (TaskCanceledException) { return; /* shutting down */ }

        while (!stop.IsCancellationRequested)
        {
            var o = _scanOpt.CurrentValue;
            try
            {
                if (o.Enabled && _policy.CurrentValue.EnforceExpiry)
                {
                    using var scope = _sp.CreateScope();
                    await RunScanAsync(scope.ServiceProvider, o, stop);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PasswordExpiryScanService tick failed; will retry after the interval.");
            }

            try { await Task.Delay(o.ScanInterval, stop); }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }

    /// <summary>One scan pass over active users whose password is expired or within
    /// the widest warning window. Read-only on users; only INSERTs notifications.</summary>
    private async Task RunScanAsync(IServiceProvider services, PasswordExpiryScanOptions o, CancellationToken ct)
    {
        var thresholds = (o.WarningDays ?? new List<int>())
            .Where(d => d > 0).Distinct().OrderBy(d => d).ToList();
        if (thresholds.Count == 0)
        {
            _log.LogWarning("PasswordExpiryScanService: no positive WarningDays configured; skipping pass.");
            return;
        }

        var db     = services.GetRequiredService<IApplicationDbContext>();
        var notify = services.GetRequiredService<INotificationService>();
        var roles  = services.GetRequiredService<IRoleQueryService>();

        var now        = DateTime.UtcNow;
        var maxWarning = thresholds.Max();
        var horizon    = now.AddDays(maxWarning);

        // Candidates: active users already expired OR within the widest warning window.
        var candidates = await db.Users.AsNoTracking()
            .Where(u => u.ActiveFlag
                     && u.Status == "Active"
                     && u.PasswordExpiryDate != null
                     && u.PasswordExpiryDate <= horizon)
            .Select(u => new { u.Id, u.UserName, u.PasswordExpiryDate, u.TenantId })
            .ToListAsync(ct);

        var disabledRoles = new HashSet<string>(
            _policy.CurrentValue.DisableExpiryForRoles ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        int expiringRaised = 0, expiredRaised = 0;

        foreach (var user in candidates)
        {
            if (ct.IsCancellationRequested) break;
            if (user.PasswordExpiryDate is null) continue;

            var recipient = user.UserName ?? user.Id;
            if (string.IsNullOrWhiteSpace(recipient)) continue;

            // Exempt privileged roles from expiry notifications (case-insensitive).
            if (disabledRoles.Count > 0)
            {
                var userRoles = await roles.GetRoleNamesAsync(user.Id, ct);
                if (userRoles.Any(r => disabledRoles.Contains(r))) continue;
            }

            var expiry      = user.PasswordExpiryDate.Value;
            var expiryStamp = expiry.ToString("yyyyMMdd");

            if (expiry < now)
            {
                // Already expired.
                var key = $"password.expired:{user.Id}:{expiryStamp}";
                if (!await db.Notifications.AsNoTracking().AnyAsync(n => n.GroupKey == key, ct))
                {
                    await notify.RaiseAsync(
                        new[] { recipient }, "password.expired", "critical",
                        "Your password has expired",
                        "You must change your password to keep signing in. Update it from your profile.",
                        "User", user.Id, "/profile", null, key, user.TenantId, ct);
                    expiredRaised++;
                }
            }
            else
            {
                // Expiring — find the SMALLEST threshold the user has crossed into
                // (thresholds are sorted ascending, so the first match is the stage).
                var daysLeft = (int)Math.Ceiling((expiry - now).TotalDays);
                int? stage = null;
                foreach (var t in thresholds) { if (daysLeft <= t) { stage = t; break; } }

                if (stage is int T)
                {
                    var key = $"password.expiring:{T}:{user.Id}:{expiryStamp}";
                    if (!await db.Notifications.AsNoTracking().AnyAsync(n => n.GroupKey == key, ct))
                    {
                        var severity = T <= 1 ? "critical" : T <= 7 ? "warning" : "info";
                        var title    = $"Your password expires in {daysLeft} day(s)";
                        await notify.RaiseAsync(
                            new[] { recipient }, "password.expiring", severity, title,
                            "Change it from your profile to avoid being locked out.",
                            "User", user.Id, "/profile", null, key, user.TenantId, ct);
                        expiringRaised++;
                    }
                }
            }
        }

        if (expiringRaised > 0 || expiredRaised > 0)
            _log.LogInformation(
                "PasswordExpiryScanService: raised {Expiring} expiring + {Expired} expired notification(s) across {Count} candidate user(s).",
                expiringRaised, expiredRaised, candidates.Count);
    }
}

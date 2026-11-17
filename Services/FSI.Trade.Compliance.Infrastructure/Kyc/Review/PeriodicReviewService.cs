using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Notifications;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

/// <summary>
/// Background service that scans TmX_Customer_Review_Schedule for customers whose
/// Next_Review_Date is due and NOTIFIES the users who can act on them.
///
/// 2026-11 rework: this used to auto-run the review (clone + drive the whole
/// workflow) in the background. The client no longer wants the automated feature —
/// a review must be started manually by a person. So this service now only raises
/// a <c>kyc.review.due</c> Notification Center notification (deduped per customer +
/// due date) to the users who hold the <c>Kyc.Review</c> privilege, deep-linking to
/// the customer's Customer Insight page where they click "Start review". It never
/// writes business data and adds no table/migration (reuses TmX_Notification).
/// Scope-per-tick, per-tick try/catch, config-driven interval; disabled when
/// PeriodicReviewOptions.Enabled is false.
/// </summary>
public class PeriodicReviewService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<PeriodicReviewOptions> _opt;
    private readonly ILogger<PeriodicReviewService> _log;

    public PeriodicReviewService(IServiceProvider sp, IOptionsMonitor<PeriodicReviewOptions> opt, ILogger<PeriodicReviewService> log)
    { _sp = sp; _opt = opt; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("PeriodicReviewService (notify-only) starting. Interval = {Interval}, Enabled = {Enabled}.",
            _opt.CurrentValue.ScanInterval, _opt.CurrentValue.Enabled);

        while (!stop.IsCancellationRequested)
        {
            var o = _opt.CurrentValue;
            try
            {
                if (o.Enabled)
                {
                    using var scope = _sp.CreateScope();
                    await NotifyDueAsync(scope.ServiceProvider, o, stop);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PeriodicReviewService tick failed; will retry after the interval.");
            }

            try { await Task.Delay(o.ScanInterval, stop); }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }

    /// <summary>One pass: find due schedules and notify the Kyc.Review audience,
    /// deduped per (customer, due-date) so a customer isn't notified twice for the
    /// same cycle. Read-only on business data; only INSERTs notifications.</summary>
    private async Task NotifyDueAsync(IServiceProvider services, PeriodicReviewOptions o, CancellationToken ct)
    {
        var db     = services.GetRequiredService<IApplicationDbContext>();
        var notify = services.GetRequiredService<INotificationService>();
        var priv   = services.GetRequiredService<IPrivilegeService>();

        var dueBy = DateTime.UtcNow.AddDays(o.LeadTimeDays);
        var due = await db.CustomerReviewSchedules.AsNoTracking()
            .Where(s => s.Status == ReviewScheduleStatus.Scheduled
                     && s.NextReviewDate != null && s.NextReviewDate <= dueBy)
            .OrderBy(s => s.NextReviewDate)
            .Take(o.BatchSize <= 0 ? 200 : o.BatchSize)
            .Select(s => new { s.CustomerCode, s.NextReviewDate, s.TenantId })
            .ToListAsync(ct);

        if (due.Count == 0) return;

        // Recipients = users who can start a review (Kyc.Review). Resolved once.
        var recipients = (await priv.GetUserIdsWithPrivilegeAsync("Kyc.Review", ct))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (recipients.Count == 0)
        {
            _log.LogInformation("PeriodicReviewService: {N} review(s) due but no users hold Kyc.Review — nothing to notify.", due.Count);
            return;
        }

        var raised = 0;
        foreach (var s in due)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(s.CustomerCode)) continue;

            var dueStamp = (s.NextReviewDate ?? DateTime.UtcNow).ToString("yyyyMMdd");
            var key = $"kyc.review.due:{s.CustomerCode}:{dueStamp}";
            if (await db.Notifications.AsNoTracking().AnyAsync(n => n.GroupKey == key, ct))
                continue;

            await notify.RaiseAsync(
                recipients, "kyc.review.due", "warning",
                $"KYC review due: {s.CustomerCode}",
                "This customer's periodic KYC review is due. Open Customer Insight and click “Start review”.",
                "Customer", s.CustomerCode, $"/customers/{Uri.EscapeDataString(s.CustomerCode)}",
                null, key, s.TenantId, ct);
            raised++;
        }

        if (raised > 0)
            _log.LogInformation("PeriodicReviewService: raised {N} 'KYC review due' notification(s) to {R} recipient(s).", raised, recipients.Count);
    }
}

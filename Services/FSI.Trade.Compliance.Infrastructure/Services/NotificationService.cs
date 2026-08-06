using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Notifications;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using FSI.Trade.Compliance.Infrastructure.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Services;

/// <summary>
/// EF Core implementation of <see cref="INotificationService"/>. Inserts one
/// <c>TmX_Notification</c> row per distinct recipient in its OWN
/// SaveChangesAsync — deliberately separate from the business write so a
/// notification failure can never roll back the approval/assignment that
/// triggered it. The whole body is wrapped in try/catch: on any failure it logs
/// and returns, never throwing.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IApplicationDbContext          _db;
    private readonly ILogger<NotificationService>   _logger;
    private readonly IHubContext<NotificationHub>   _hub;
    private readonly IOptionsMonitor<EmailOptions>  _emailOptions;
    private readonly IEmailSender                    _emailSender;

    public NotificationService(IApplicationDbContext db, ILogger<NotificationService> logger,
                               IHubContext<NotificationHub> hub,
                               IOptionsMonitor<EmailOptions> emailOptions,
                               IEmailSender emailSender)
    {
        _db           = db;
        _logger       = logger;
        _hub          = hub;
        _emailOptions = emailOptions;
        _emailSender  = emailSender;
    }

    public async Task RaiseAsync(IEnumerable<string> recipients, string typeCode, string severity, string title,
                                 string? body, string? entityType, string? entityId, string? deepLink,
                                 string? actor, string? groupKey, int? tenantId, CancellationToken ct = default)
    {
        try
        {
            if (recipients is null) return;

            // Dedupe (case-insensitive), drop null/blank, and never notify the
            // actor about their own action.
            var targets = recipients
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Where(r => actor is null || !string.Equals(r, actor.Trim(), StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 0) return;

            // Respect per-user preferences (Phase 2): drop recipients who have
            // EXPLICITLY disabled in-app delivery for this type. Default is ON —
            // a user with no stored row keeps receiving. One extra read; the
            // insert below still commits in the single SaveChanges.
            var optedOut = await _db.NotificationPrefs
                .Where(p => targets.Contains(p.UserId)
                         && p.TypeCode == typeCode
                         && !p.InAppEnabled)
                .Select(p => p.UserId)
                .ToListAsync(ct);
            if (optedOut.Count > 0)
            {
                var optOutSet = new HashSet<string>(optedOut, StringComparer.OrdinalIgnoreCase);
                targets = targets.Where(t => !optOutSet.Contains(t)).ToList();
                if (targets.Count == 0) return;
            }

            var now = DateTime.UtcNow;
            foreach (var recipient in targets)
            {
                _db.Notifications.Add(new Notification
                {
                    TenantId      = tenantId,
                    RecipientUser = recipient,
                    TypeCode      = typeCode,
                    Severity      = string.IsNullOrWhiteSpace(severity) ? "info" : severity,
                    Title         = title,
                    Body          = body,
                    EntityType    = entityType,
                    EntityId      = entityId,
                    DeepLink      = deepLink,
                    IsRead        = false,
                    GroupKey      = groupKey,
                    CreatedBy     = actor,
                    CreatedDate   = now,
                });
            }

            await _db.SaveChangesAsync(ct);

            // --- Delivery channel 1: SignalR real-time push -----------------
            // The persisted row above is the source of truth. This is only a
            // lightweight trigger so a connected client refetches — keep the
            // payload tiny. Per-recipient failures are swallowed so one dead
            // connection can't stop the others, and the whole loop is still
            // inside the method try/catch so nothing can escape to the caller.
            foreach (var t in targets)
            {
                try
                {
                    await _hub.Clients.Group(t).SendAsync("notification", new { typeCode, severity, title }, ct);
                }
                catch
                {
                    /* swallow per-recipient push failure */
                }
            }

            // --- Delivery channel 2: SMTP email (off by default) ------------
            // Only for high-severity notifications AND only when email is
            // enabled. When Email:Enabled=false (default) this branch is skipped
            // entirely, so there is zero behaviour change until an operator
            // turns it on. Best-effort: SmtpEmailSender never throws, but we
            // also guard the recipient lookup so nothing can escape.
            if (severity == "critical" && _emailOptions.CurrentValue.Enabled)
            {
                var emails = await _db.Users
                    .Where(u => (targets.Contains(u.Id) || (u.UserName != null && targets.Contains(u.UserName)))
                             && u.Email != null && u.Email != "")
                    .Select(u => u.Email!)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var email in emails)
                    await _emailSender.SendAsync(email, title, body ?? title, ct);
            }
        }
        catch (Exception ex)
        {
            // A notification failure must never affect the business write that
            // triggered it — log and swallow.
            _logger.LogError(ex,
                "Failed to raise notification(s) type={TypeCode} title={Title} actor={Actor}.",
                typeCode, title, actor);
        }
    }
}

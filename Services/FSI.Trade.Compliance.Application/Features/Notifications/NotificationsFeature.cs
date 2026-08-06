using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Application.Features.Notifications;

// =====================================================================
// Notifications — CQRS feature. DTOs + MediatR requests + handlers for
// the per-user notification feed (bell icon). Reads TmX_Notification for
// the current user (Recipient_User matched against BOTH UserId and
// UserName). Mark-read is scoped to the caller's own rows. No special
// privilege — [Authorize] only; a user reads their own notifications.
// =====================================================================

// ---------- DTOs (camelCase — FE contract) ----------
public class NotificationDto
{
    public long     id { get; set; }
    public string   typeCode { get; set; } = default!;
    public string   severity { get; set; } = default!;
    public string   title { get; set; } = default!;
    public string?  body { get; set; }
    public string?  entityType { get; set; }
    public string?  entityId { get; set; }
    public string?  deepLink { get; set; }
    public bool     isRead { get; set; }
    public DateTime createdDate { get; set; }
}

public class NotificationListDto
{
    public List<NotificationDto> items  { get; set; } = new();
    public int                   total  { get; set; }
    public int                   unread { get; set; }
}

// ---------- Queries ----------
public record GetNotificationsQuery(bool UnreadOnly, int Page, int PageSize) : IRequest<NotificationListDto>;

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, NotificationListDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public GetNotificationsQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<NotificationListDto> Handle(GetNotificationsQuery req, CancellationToken ct)
    {
        var uid   = _current.UserId;
        var uname = _current.UserName;

        // Recipient match: Recipient_User holds a user id OR a username, so
        // match against both identifiers of the current user.
        var mine = _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUser == uid || n.RecipientUser == uname);

        var filtered = req.UnreadOnly ? mine.Where(n => !n.IsRead) : mine;

        var page     = req.Page     < 1 ? 1  : req.Page;
        var pageSize = req.PageSize  < 1 ? 20 : req.PageSize;

        var total  = await filtered.CountAsync(ct);
        var unread = await mine.CountAsync(n => !n.IsRead, ct);

        var items = await filtered
            .OrderByDescending(n => n.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto
            {
                id          = n.NotificationId,
                typeCode    = n.TypeCode,
                severity    = n.Severity,
                title       = n.Title,
                body        = n.Body,
                entityType  = n.EntityType,
                entityId    = n.EntityId,
                deepLink    = n.DeepLink,
                isRead      = n.IsRead,
                createdDate = n.CreatedDate
            })
            .ToListAsync(ct);

        return new NotificationListDto { items = items, total = total, unread = unread };
    }
}

public record GetNotificationCountQuery() : IRequest<int>;

public class GetNotificationCountQueryHandler : IRequestHandler<GetNotificationCountQuery, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public GetNotificationCountQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<int> Handle(GetNotificationCountQuery req, CancellationToken ct)
    {
        var uid   = _current.UserId;
        var uname = _current.UserName;
        return await _db.Notifications.AsNoTracking()
            .CountAsync(n => (n.RecipientUser == uid || n.RecipientUser == uname) && !n.IsRead, ct);
    }
}

// ---------- Commands ----------
public record MarkNotificationReadCommand(long Id) : IRequest<int>;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public MarkNotificationReadCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<int> Handle(MarkNotificationReadCommand req, CancellationToken ct)
    {
        var uid   = _current.UserId;
        var uname = _current.UserName;

        // Only the recipient may mark their own notification read.
        var row = await _db.Notifications.FirstOrDefaultAsync(
            n => n.NotificationId == req.Id
              && (n.RecipientUser == uid || n.RecipientUser == uname), ct);
        if (row is not null && !row.IsRead)
        {
            row.IsRead   = true;
            row.ReadDate = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return await _db.Notifications.AsNoTracking()
            .CountAsync(n => (n.RecipientUser == uid || n.RecipientUser == uname) && !n.IsRead, ct);
    }
}

public record MarkAllNotificationsReadCommand() : IRequest<int>;

public class MarkAllNotificationsReadCommandHandler : IRequestHandler<MarkAllNotificationsReadCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public MarkAllNotificationsReadCommandHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<int> Handle(MarkAllNotificationsReadCommand req, CancellationToken ct)
    {
        var uid   = _current.UserId;
        var uname = _current.UserName;

        var unread = await _db.Notifications
            .Where(n => (n.RecipientUser == uid || n.RecipientUser == uname) && !n.IsRead)
            .ToListAsync(ct);

        if (unread.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var n in unread) { n.IsRead = true; n.ReadDate = now; }
            await _db.SaveChangesAsync(ct);
        }

        return 0;
    }
}

// =====================================================================
// Notification preferences (Phase 2) — per-user, per-type in-app toggles
// over TmX_Notification_Pref. Personal: [Authorize] only; a user reads and
// updates their OWN preferences (User_Id matched against BOTH UserId and
// UserName, same convention as the feed). Default is ON — the GET always
// returns EVERY known type, merging the stored row (or the default true).
// The emitter (INotificationService) drops recipients who set a type OFF.
// =====================================================================

/// <summary>
/// The full set of notification type codes the preferences UI exposes — kept
/// in sync with the codes the emitter raises (request.pending, request.approved,
/// request.rejected, assigned, flag.raised). The GET always returns one entry
/// per code; UPDATE ignores any submitted code not in this set.
/// </summary>
public static class NotificationTypeCodes
{
    public static readonly string[] KnownTypeCodes =
    {
        "request.pending",
        "request.approved",
        "request.rejected",
        "assigned",
        "flag.raised",
        "password.expiring",
        "password.expired",
        "sla.atrisk",
        "sla.breached",
    };
}

// ---------- DTOs (camelCase — FE contract) ----------
public class NotificationPrefDto
{
    public string typeCode { get; set; } = default!;
    public bool   inApp    { get; set; }
}

public class NotificationPrefsDto
{
    public List<NotificationPrefDto> preferences { get; set; } = new();
}

// ---------- Query ----------
public record GetNotificationPreferencesQuery() : IRequest<NotificationPrefsDto>;

public class GetNotificationPreferencesQueryHandler
    : IRequestHandler<GetNotificationPreferencesQuery, NotificationPrefsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    public GetNotificationPreferencesQueryHandler(IApplicationDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public async Task<NotificationPrefsDto> Handle(GetNotificationPreferencesQuery req, CancellationToken ct)
    {
        var uid   = _current.UserId;
        var uname = _current.UserName;

        // User_Id holds a user id OR a username — match against both identifiers
        // of the current user, same as the feed's recipient match.
        var stored = await _db.NotificationPrefs.AsNoTracking()
            .Where(p => p.UserId == uid || p.UserId == uname)
            .ToListAsync(ct);

        // Return EVERY known type, merged: a stored row's InAppEnabled wins,
        // otherwise the default (ON).
        var byType = stored
            .GroupBy(p => p.TypeCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().InAppEnabled, StringComparer.OrdinalIgnoreCase);

        var prefs = NotificationTypeCodes.KnownTypeCodes
            .Select(code => new NotificationPrefDto
            {
                typeCode = code,
                inApp    = byType.TryGetValue(code, out var enabled) ? enabled : true,
            })
            .ToList();

        return new NotificationPrefsDto { preferences = prefs };
    }
}

// ---------- Command ----------
public record UpdateNotificationPreferencesCommand(List<NotificationPrefDto> Preferences)
    : IRequest<NotificationPrefsDto>;

public class UpdateNotificationPreferencesCommandHandler
    : IRequestHandler<UpdateNotificationPreferencesCommand, NotificationPrefsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _current;
    private readonly IMediator             _mediator;
    public UpdateNotificationPreferencesCommandHandler(
        IApplicationDbContext db, ICurrentUserService current, IMediator mediator)
    { _db = db; _current = current; _mediator = mediator; }

    public async Task<NotificationPrefsDto> Handle(UpdateNotificationPreferencesCommand req, CancellationToken ct)
    {
        // Store against a single stable identifier (prefer UserId, fall back to
        // UserName) — the GET matches both, so either resolves back to the user.
        var owner = _current.UserId ?? _current.UserName;
        if (string.IsNullOrWhiteSpace(owner))
            return await _mediator.Send(new GetNotificationPreferencesQuery(), ct);

        var known = new HashSet<string>(NotificationTypeCodes.KnownTypeCodes, StringComparer.OrdinalIgnoreCase);

        // Only accept known type codes; ignore unknown. Keep the LAST submitted
        // value per type (defensive against duplicate entries in one payload).
        var submitted = (req.Preferences ?? new List<NotificationPrefDto>())
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.typeCode) && known.Contains(p.typeCode.Trim()))
            .GroupBy(p => p.typeCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().inApp, StringComparer.OrdinalIgnoreCase);

        if (submitted.Count > 0)
        {
            var now = DateTime.UtcNow;

            // Existing rows for this owner (by the stored identifier) that we may
            // update in place — avoids a unique-index collision on (User_Id, Type_Code).
            var existing = await _db.NotificationPrefs
                .Where(p => p.UserId == owner)
                .ToListAsync(ct);
            var byType = existing.ToDictionary(p => p.TypeCode, StringComparer.OrdinalIgnoreCase);

            foreach (var kv in submitted)
            {
                if (byType.TryGetValue(kv.Key, out var row))
                {
                    row.InAppEnabled    = kv.Value;
                    row.LastUpdatedDate = now;
                }
                else
                {
                    _db.NotificationPrefs.Add(new NotificationPref
                    {
                        UserId          = owner,
                        TypeCode        = kv.Key,
                        InAppEnabled    = kv.Value,
                        CreatedDate     = now,
                        LastUpdatedDate = now,
                    });
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        // Return the refreshed, fully-merged view.
        return await _mediator.Send(new GetNotificationPreferencesQuery(), ct);
    }
}

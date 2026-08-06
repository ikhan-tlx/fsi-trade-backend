namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Per-user notification delivery preference over <c>TmX_Notification_Pref</c>.
/// One row per (user, notification type) the user has explicitly toggled. Read
/// by the app's emitter (INotificationService) to drop recipients who disabled
/// in-app delivery for a type, and read/written by the /Notifications
/// Preferences API. Default is ON — a user with no row keeps receiving.
///
/// <see cref="UserId"/> holds a user id OR a username, matching the
/// <c>Recipient_User</c> semantics on <see cref="Notification"/>
/// (ICurrentUserService.UserId / .UserName).
/// </summary>
public class NotificationPref
{
    public long      NotificationPrefId { get; set; }
    public string    UserId             { get; set; } = "";   // user id OR username
    public string    TypeCode           { get; set; } = "";
    public bool      InAppEnabled       { get; set; } = true;
    public DateTime  CreatedDate        { get; set; }
    public DateTime? LastUpdatedDate    { get; set; }
}

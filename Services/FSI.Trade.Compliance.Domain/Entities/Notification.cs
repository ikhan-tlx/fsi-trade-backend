namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Per-recipient notification row over <c>TmX_Notification</c>. Written by the
/// app's emitter (INotificationService) AFTER a business write commits —
/// maker/checker approvals, request decisions, workflow task assignments — and
/// read back by the /Notifications feed API.
///
/// <see cref="RecipientUser"/> holds a user id OR a username; reads match
/// against both (ICurrentUserService.UserId / .UserName) so the emitter can
/// raise against whichever identifier it happens to hold.
/// </summary>
public class Notification
{
    public long      NotificationId { get; set; }
    public int?      TenantId       { get; set; }
    public string    RecipientUser  { get; set; } = "";   // user id OR username
    public string    TypeCode       { get; set; } = "";
    public string    Severity       { get; set; } = "info";
    public string    Title          { get; set; } = "";
    public string?   Body           { get; set; }
    public string?   EntityType     { get; set; }
    public string?   EntityId       { get; set; }
    public string?   DeepLink       { get; set; }
    public bool      IsRead         { get; set; }
    public DateTime? ReadDate       { get; set; }
    public string?   GroupKey       { get; set; }
    public string?   CreatedBy      { get; set; }   // the actor who caused it
    public DateTime  CreatedDate    { get; set; }
}

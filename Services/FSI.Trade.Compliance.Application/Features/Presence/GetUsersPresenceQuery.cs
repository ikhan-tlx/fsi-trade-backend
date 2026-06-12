using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Presence;

/// <summary>
/// Resolves lightweight "is this user around right now" presence for a set of
/// user ids, derived from <c>UserDevices.LastSeenAt</c> (already maintained by
/// the device-tracking middleware on every authenticated request — no SignalR
/// / persistent connection required). A user is "available" when their most
/// recent non-revoked device was seen within
/// <see cref="GetUsersPresenceQueryHandler.FreshnessMinutes"/> minutes.
///
/// This is "recently active anywhere in the app", which is the right semantic
/// for "could pick up this workflow step" — not "currently viewing this page".
/// </summary>
public record GetUsersPresenceQuery(IReadOnlyList<string> UserIds)
    : IRequest<IReadOnlyList<UserPresenceDto>>;

public class UserPresenceDto
{
    public string    userId      { get; set; } = "";
    public bool      isAvailable { get; set; }
    public DateTime? lastSeenAt  { get; set; }
}

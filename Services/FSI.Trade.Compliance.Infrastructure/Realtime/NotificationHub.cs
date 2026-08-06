using Microsoft.AspNetCore.SignalR;

namespace FSI.Trade.Compliance.Infrastructure.Realtime;

/// <summary>
/// Real-time push hub for in-app notifications. Lives in Infrastructure (not API)
/// so the emitter (<see cref="Services.NotificationService"/>) can inject
/// <c>IHubContext&lt;NotificationHub&gt;</c> and push after a business write commits.
/// Server pushes only — there are no client-callable hub methods.
///
/// On connect each client is added to TWO groups: their user id claim AND their
/// username claim. Notification recipients are stored as either the user id OR the
/// username, so grouping by both lets the emitter target <c>Clients.Group(recipient)</c>
/// regardless of which form was persisted.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId   = Context.User?.FindFirst("userId")?.Value;
        var userName = Context.User?.FindFirst("userName")?.Value ?? Context.User?.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);

        if (!string.IsNullOrWhiteSpace(userName))
            await Groups.AddToGroupAsync(Context.ConnectionId, userName);

        await base.OnConnectedAsync();
    }
}

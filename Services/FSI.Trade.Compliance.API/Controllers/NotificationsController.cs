using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Per-user notification feed (bell icon). A user reads and clears their OWN
/// notifications — no special privilege, [Authorize] only.
///   GET  /api/v1/Notifications?unreadOnly=&page=&pageSize=
///   GET  /api/v1/Notifications/Count
///   POST /api/v1/Notifications/{id}/Read
///   POST /api/v1/Notifications/ReadAll
///   GET  /api/v1/Notifications/Preferences
///   PUT  /api/v1/Notifications/Preferences
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    public NotificationsController(IMediator mediator) => _mediator = mediator;

    // ----- feed -----
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(ResponseViewModel<NotificationListDto>.Ok(
            await _mediator.Send(new GetNotificationsQuery(unreadOnly, page, pageSize), ct)));

    [HttpGet("Count")]
    public async Task<IActionResult> Count(CancellationToken ct)
        => Ok(ResponseViewModel<int>.Ok(await _mediator.Send(new GetNotificationCountQuery(), ct)));

    // ----- mark read -----
    [HttpPost("{id}/Read")]
    public async Task<IActionResult> Read(long id, CancellationToken ct)
        => Ok(ResponseViewModel<int>.Ok(await _mediator.Send(new MarkNotificationReadCommand(id), ct)));

    [HttpPost("ReadAll")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
        => Ok(ResponseViewModel<int>.Ok(await _mediator.Send(new MarkAllNotificationsReadCommand(), ct)));

    // ----- preferences (personal — [Authorize] only) -----
    [HttpGet("Preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
        => Ok(ResponseViewModel<NotificationPrefsDto>.Ok(
            await _mediator.Send(new GetNotificationPreferencesQuery(), ct)));

    [HttpPut("Preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePrefsRequest body, CancellationToken ct)
        => Ok(ResponseViewModel<NotificationPrefsDto>.Ok(
            await _mediator.Send(new UpdateNotificationPreferencesCommand(body.preferences), ct)));

    /// <summary>Request body for PUT /Preferences — the caller's full toggle set.</summary>
    public class UpdatePrefsRequest
    {
        public List<NotificationPrefDto> preferences { get; set; } = new();
    }
}

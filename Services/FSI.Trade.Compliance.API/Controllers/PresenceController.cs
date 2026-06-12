using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Presence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Lightweight user-presence surface, backed by <c>UserDevices.LastSeenAt</c>
/// (no SignalR / persistent connections). Used by the transaction-detail People
/// card to show which pooled actors are "available now".
///
///   GET /api/v1/Presence/Users?userIds=a,b,c   (Bearer)   availability per user
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class PresenceController : ControllerBase
{
    private readonly IMediator _mediator;

    public PresenceController(IMediator mediator) => _mediator = mediator;

    [HttpGet("Users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? userIds, CancellationToken ct)
    {
        var ids = (userIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await _mediator.Send(new GetUsersPresenceQuery(ids), ct);
        return Ok(ResponseViewModel<IReadOnlyList<UserPresenceDto>>.Ok(result));
    }
}

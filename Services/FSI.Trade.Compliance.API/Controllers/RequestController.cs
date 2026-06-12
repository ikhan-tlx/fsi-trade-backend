using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Requests.GetPending;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Maker/checker request surface over <c>TmX_Request</c>. Slice 14 — added so
/// the FE's pending-request guard stops 404'ing.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class RequestController : ControllerBase
{
    private readonly IMediator _mediator;
    public RequestController(IMediator mediator) => _mediator = mediator;

    /// <summary>Pending requests for an entity (request-type code + entity id).</summary>
    [HttpGet("GetPendingRequests/{requestType}/{entityId}")]
    public async Task<IActionResult> GetPendingRequests(string requestType, string entityId, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetPendingRequestsQuery(requestType, entityId), ct);
        return Ok(ResponseViewModel<IReadOnlyList<PendingRequestDto>>.Ok(rows));
    }
}

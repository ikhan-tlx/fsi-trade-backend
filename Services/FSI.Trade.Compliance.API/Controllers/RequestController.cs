using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Requests.GetById;
using FSI.Trade.Compliance.Application.Features.Requests.GetPending;
using FSI.Trade.Compliance.Application.Features.Requests.List;
using FSI.Trade.Compliance.Application.Features.Requests.Submit;
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

    /// <summary>
    /// Paged request list backing the FE Requests page grid.
    ///   GET /api/v1/Request?page=1&amp;pageSize=10&amp;sort=CreatedDate-desc
    /// Returns ResponseViewModel&lt;PagedResult&lt;RequestListItemDto&gt;&gt;
    /// (data.items + data.total), consistent with the other paged grids.
    /// </summary>
    [HttpGet]
    [RequiresPrivilege("Requests.View")]
    public async Task<IActionResult> List([FromQuery] ListRequestsQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<RequestListItemDto>>.Ok(paged));
    }

    /// <summary>Pending requests for an entity (request-type code + entity id).</summary>
    [HttpGet("GetPendingRequests/{requestType}/{entityId}")]
    public async Task<IActionResult> GetPendingRequests(string requestType, string entityId, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetPendingRequestsQuery(requestType, entityId), ct);
        return Ok(ResponseViewModel<IReadOnlyList<PendingRequestDto>>.Ok(rows));
    }

    /// <summary>
    /// Single request for the detail/edit page. Returns the row + Entity_Json so
    /// the FE can render the maker/checker entity in view mode.
    /// (id:long so it never shadows the literal "GetPendingRequests" route.)
    /// </summary>
    [HttpGet("{id:long}")]
    [RequiresPrivilege("Requests.View")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetRequestByIdQuery(id), ct);
        return Ok(ResponseViewModel<RequestDetailDto>.Ok(dto));
    }

    /// <summary>
    /// Checker decision (approve/reject/etc.) on a request. Records the new
    /// status + the executing checker. POST /api/v1/Request/SubmitRequest.
    /// </summary>
    [HttpPost("SubmitRequest")]
    public async Task<IActionResult> SubmitRequest([FromBody] SubmitRequestCommand cmd, CancellationToken ct)
    {
        var id = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { requestId = id }, "Request submitted"));
    }
}

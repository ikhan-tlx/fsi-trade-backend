using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Locations.Create;
using FSI.Trade.Compliance.Application.Features.Locations.GetById;
using FSI.Trade.Compliance.Application.Features.Locations.List;
using FSI.Trade.Compliance.Application.Features.Locations.Lov;
using FSI.Trade.Compliance.Application.Features.Locations.SetActive;
using FSI.Trade.Compliance.Application.Features.Locations.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Location surface over <c>TmX_Location</c>. Slice 14 — introduced because
/// the new BE had no LocationController at all (the FE "Locations" setup page
/// 404'd). Read endpoints first:
///
///   GET /api/v1/Location           — paged admin grid (server sort + filter)
///   GET /api/v1/Location/lov       — active locations for dropdowns
///
/// Write endpoints (create / edit / activate / deactivate) are a follow-up:
/// they need the request/approval workflow the legacy LocationController ran
/// through, which isn't wired in the new BE yet.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class LocationController : ControllerBase
{
    private readonly IMediator _mediator;
    public LocationController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Paged / sorted / filtered list of locations for the management grid.
    /// </summary>
    [HttpGet]
    [RequiresPrivilege("Masterdata.View")]
    public async Task<IActionResult> List([FromQuery] ListLocationsQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<LocationListItemDto>>.Ok(paged));
    }

    /// <summary>
    /// Active locations in LOV shape (Location_ID / Location_Name) for the
    /// parent-location selector and location filters.
    /// </summary>
    [HttpGet("lov")]
    public async Task<IActionResult> Lov(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListLocationsLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<LocationLovItemDto>>.Ok(rows));
    }

    /// <summary>Single location by id (edit form). Slice 14.</summary>
    [HttpGet("{id:int}")]
    [RequiresPrivilege("Masterdata.View")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetLocationByIdQuery(id), ct);
        return Ok(ResponseViewModel<LocationListItemDto>.Ok(dto));
    }

    /// <summary>Create a location. Slice 14.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLocationCommand cmd, CancellationToken ct)
    {
        var id = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { locationId = id }, "Location created"));
    }

    /// <summary>Update a location. Slice 14.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLocationCommand body, CancellationToken ct)
    {
        body.LocationId = id;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { locationId = id }, "Location updated"));
    }

    /// <summary>Activate a location. Slice 14.</summary>
    [HttpPut("Activate/{id:int}")]
    public async Task<IActionResult> Activate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetLocationActiveCommand(id, true), ct);
        return Ok(ResponseViewModel<object>.Ok(new { locationId = id, isActive = true }, "Location activated"));
    }

    /// <summary>Deactivate a location. Slice 14.</summary>
    [HttpPut("Deactivate/{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetLocationActiveCommand(id, false), ct);
        return Ok(ResponseViewModel<object>.Ok(new { locationId = id, isActive = false }, "Location deactivated"));
    }
}

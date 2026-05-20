using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Privileges.ListEntities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Read-only catalog of privilege codes defined in the system. Drives the
/// FE role-edit matrix UI. Slice 2 surface:
///
///   GET /api/v1/Privileges/Entities    (Bearer + Privileges.View)
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class PrivilegesController : ControllerBase
{
    private readonly IMediator _mediator;
    public PrivilegesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("Entities")]
    [RequiresPrivilege("Privileges.View")]
    public async Task<IActionResult> Entities(CancellationToken ct)
    {
        var entities = await _mediator.Send(new ListPrivilegeEntitiesQuery(), ct);
        return Ok(ResponseViewModel<List<PrivilegeEntityDto>>.Ok(entities));
    }
}

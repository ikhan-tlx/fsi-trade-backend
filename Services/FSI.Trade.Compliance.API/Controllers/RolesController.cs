using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Roles.Lov;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// All-roles LOV for Reports and similar flows that need to pick any
/// role. Slice 7. Distinct from <see cref="RoleController"/> (singular)
/// which exposes the privilege-gated CRUD surface.
///
/// Plural URL — matches the FE businessReports api which calls
/// <c>GET /api/v1/Roles</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;
    public RolesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListAllRolesLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<RoleLovItemDto>>.Ok(rows));
    }
}

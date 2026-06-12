using System.Security.Claims;
using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
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
///   GET /api/v1/Privileges/Mine        (Bearer)   the caller's own grants
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

    /// <summary>
    /// The distinct privilege codes granted to the caller's roles. Lets the FE
    /// gate affordances (e.g. hide the edit form) by the same codes the
    /// <c>[RequiresPrivilege]</c> filter enforces server-side. No privilege
    /// required — a user may always read their own grants.
    /// </summary>
    [HttpGet("Mine")]
    public async Task<IActionResult> Mine(
        [FromServices] IPrivilegeService privileges,
        CancellationToken ct)
    {
        var roleNames = User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var codes = await privileges.GetPrivilegesForRolesAsync(roleNames, ct);
        return Ok(ResponseViewModel<IReadOnlyCollection<string>>.Ok(codes));
    }
}

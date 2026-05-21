using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Roles.Create;
using FSI.Trade.Compliance.Application.Features.Roles.Get;
using FSI.Trade.Compliance.Application.Features.Roles.List;
using FSI.Trade.Compliance.Application.Features.Roles.Lov;
using FSI.Trade.Compliance.Application.Features.Roles.Privileges;
using FSI.Trade.Compliance.Application.Features.Roles.SetActive;
using FSI.Trade.Compliance.Application.Features.Roles.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Role CRUD + role-privilege matrix. Slice 2 Step 3 surface:
///
///   GET    /api/v1/Role                      (Bearer + Roles.View)
///   GET    /api/v1/Role/{id}                 (Bearer + Roles.View)
///   POST   /api/v1/Role                      (Bearer + Roles.Manage)
///   PUT    /api/v1/Role/{id}                 (Bearer + Roles.Manage)
///   POST   /api/v1/Role/{id}/Activate        (Bearer + Roles.Manage)
///   POST   /api/v1/Role/{id}/Deactivate      (Bearer + Roles.Manage)
///   GET    /api/v1/Role/{id}/Privileges      (Bearer + Roles.View)
///   PUT    /api/v1/Role/{id}/Privileges      (Bearer + Roles.Manage)
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class RoleController : ControllerBase
{
    private readonly IMediator _mediator;
    public RoleController(IMediator mediator) => _mediator = mediator;

    // ---------- List ----------
    [HttpGet]
    [RequiresPrivilege("Roles.View")]
    public async Task<IActionResult> List([FromQuery] ListRolesQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<RoleListItemDto>>.Ok(paged));
    }

    // ---------- LOV (flat, all-active, no paging, no privilege gate) ----------
    /// <summary>
    /// Slice 7 — all-active roles for Reports filter dropdowns and any
    /// other consumer that needs a flat list without paging or the
    /// Roles.View privilege gate. Distinct from <see cref="List"/>
    /// which is the paged/filtered/privilege-gated CRUD surface.
    /// Replaces the plural-name <c>RolesController</c> (deleted during
    /// consolidation).
    /// </summary>
    [HttpGet("all")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListAllRolesLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<RoleLovItemDto>>.Ok(rows));
    }

    // ---------- Get one ----------
    [HttpGet("{id:int}")]
    [RequiresPrivilege("Roles.View")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetRoleQuery(id), ct);
        return Ok(ResponseViewModel<RoleDetailDto>.Ok(dto));
    }

    // ---------- Create ----------
    [HttpPost]
    [RequiresPrivilege("Roles.Manage")]
    public async Task<IActionResult> Create([FromBody] CreateRoleCommand cmd, CancellationToken ct)
    {
        var newId = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { roleId = newId }));
    }

    // ---------- Update ----------
    [HttpPut("{id:int}")]
    [RequiresPrivilege("Roles.Manage")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoleCommand body, CancellationToken ct)
    {
        body.roleId = id;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { roleId = id }));
    }

    // ---------- Activate / Deactivate ----------
    [HttpPost("{id:int}/Activate")]
    [RequiresPrivilege("Roles.Manage")]
    public async Task<IActionResult> Activate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetRoleActiveCommand(id, true), ct);
        return Ok(ResponseViewModel<object>.Ok(new { roleId = id, isActive = true }));
    }

    [HttpPost("{id:int}/Deactivate")]
    [RequiresPrivilege("Roles.Manage")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetRoleActiveCommand(id, false), ct);
        return Ok(ResponseViewModel<object>.Ok(new { roleId = id, isActive = false }));
    }

    // ---------- Privilege matrix ----------
    [HttpGet("{id:int}/Privileges")]
    [RequiresPrivilege("Roles.View")]
    public async Task<IActionResult> GetPrivileges(int id, CancellationToken ct)
    {
        var list = await _mediator.Send(new GetRolePrivilegesQuery(id), ct);
        return Ok(ResponseViewModel<List<RolePrivilegeDto>>.Ok(list));
    }

    [HttpPut("{id:int}/Privileges")]
    [RequiresPrivilege("Roles.Manage")]
    public async Task<IActionResult> UpdatePrivileges(int id, [FromBody] UpdateRolePrivilegesCommand body, CancellationToken ct)
    {
        body.roleId = id;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { roleId = id, privilegeCount = body.privilegeIds?.Count ?? 0 }));
    }
}

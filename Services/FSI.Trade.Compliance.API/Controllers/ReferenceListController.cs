using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.ReferenceLists;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Reference Lists — the normalized reference-data module (/admin/reference-lists).
/// Reference TYPES are the categories Auto Flag rules point at; reference VALUES
/// are the deduped members; a value belongs to many types (M:N). See
/// REFERENCE_LIST_MODULE_DESIGN_AND_PLAN.md.
///
///   GET    /api/v1/ReferenceList/Domains
///   GET    /api/v1/ReferenceList/Types                — paged/filtered/sorted
///   GET    /api/v1/ReferenceList/Types/{id}
///   POST   /api/v1/ReferenceList/Types
///   PUT    /api/v1/ReferenceList/Types/{id}
///   DELETE /api/v1/ReferenceList/Types/{id}
///   GET    /api/v1/ReferenceList/Types/{code}/Usage   — rules referencing a type
///   GET    /api/v1/ReferenceList/Values               — paged/filtered/sorted
///   GET    /api/v1/ReferenceList/Values/{id}
///   POST   /api/v1/ReferenceList/Values
///   PUT    /api/v1/ReferenceList/Values/{id}
///   DELETE /api/v1/ReferenceList/Values/{id}
///   GET    /api/v1/ReferenceList/LookupPicks?lookupType=&q=   — Code picker
///   GET    /api/v1/ReferenceList/FieldDomains          — rule-builder preview
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ReferenceListController : ControllerBase
{
    private readonly IMediator _mediator;
    public ReferenceListController(IMediator mediator) => _mediator = mediator;

    // ── domains ──
    [HttpGet("Domains")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> Domains(CancellationToken ct)
        => Ok(ResponseViewModel<List<DomainDto>>.Ok(await _mediator.Send(new ListReferenceDomainsQuery(), ct)));

    // ── types ──
    [HttpGet("Types")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> ListTypes([FromQuery] ListReferenceTypesQuery query, CancellationToken ct)
        => Ok(ResponseViewModel<PagedResult<ReferenceTypeSummaryDto>>.Ok(await _mediator.Send(query, ct)));

    [HttpGet("Types/{id:long}")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> GetType(long id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetReferenceTypeByIdQuery(id), ct);
        return dto is null
            ? NotFound(ResponseViewModel<ReferenceTypeDetailDto>.Fail(404, "reference_type_not_found", "Reference type not found."))
            : Ok(ResponseViewModel<ReferenceTypeDetailDto>.Ok(dto));
    }

    [HttpPost("Types")]
    [RequiresPrivilege("ReferenceLists.Manage")]
    public async Task<IActionResult> CreateType([FromBody] SaveReferenceTypeCommand cmd, CancellationToken ct)
    {
        cmd.id = null;
        return Ok(ResponseViewModel<object>.Ok(new { id = await _mediator.Send(cmd, ct) }));
    }

    [HttpPut("Types/{id:long}")]
    [RequiresPrivilege("ReferenceLists.Manage")]
    public async Task<IActionResult> UpdateType(long id, [FromBody] SaveReferenceTypeCommand cmd, CancellationToken ct)
    {
        cmd.id = id;
        return Ok(ResponseViewModel<object>.Ok(new { id = await _mediator.Send(cmd, ct) }));
    }

    [HttpDelete("Types/{id:long}")]
    [RequiresPrivilege("ReferenceLists.Manage")]
    public async Task<IActionResult> DeleteType(long id, CancellationToken ct)
        => Ok(ResponseViewModel<object>.Ok(new { ok = await _mediator.Send(new DeleteReferenceTypeCommand(id), ct) }));

    [HttpGet("Types/{code}/Usage")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> TypeUsage(string code, CancellationToken ct)
        => Ok(ResponseViewModel<List<ReferenceRuleUsageDto>>.Ok(await _mediator.Send(new GetReferenceTypeUsageQuery(code), ct)));

    // ── values ──
    [HttpGet("Values")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> ListValues([FromQuery] ListReferenceValuesQuery query, CancellationToken ct)
        => Ok(ResponseViewModel<PagedResult<ReferenceValueSummaryDto>>.Ok(await _mediator.Send(query, ct)));

    [HttpGet("Values/{id:long}")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> GetValue(long id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetReferenceValueByIdQuery(id), ct);
        return dto is null
            ? NotFound(ResponseViewModel<ReferenceValueDetailDto>.Fail(404, "reference_value_not_found", "Reference value not found."))
            : Ok(ResponseViewModel<ReferenceValueDetailDto>.Ok(dto));
    }

    [HttpPost("Values")]
    [RequiresPrivilege("ReferenceLists.Manage")]
    public async Task<IActionResult> CreateValue([FromBody] SaveReferenceValueCommand cmd, CancellationToken ct)
    {
        cmd.id = null;
        return Ok(ResponseViewModel<object>.Ok(new { id = await _mediator.Send(cmd, ct) }));
    }

    [HttpPut("Values/{id:long}")]
    [RequiresPrivilege("ReferenceLists.Manage")]
    public async Task<IActionResult> UpdateValue(long id, [FromBody] SaveReferenceValueCommand cmd, CancellationToken ct)
    {
        cmd.id = id;
        return Ok(ResponseViewModel<object>.Ok(new { id = await _mediator.Send(cmd, ct) }));
    }

    [HttpDelete("Values/{id:long}")]
    [RequiresPrivilege("ReferenceLists.Manage")]
    public async Task<IActionResult> DeleteValue(long id, CancellationToken ct)
        => Ok(ResponseViewModel<object>.Ok(new { ok = await _mediator.Send(new DeleteReferenceValueCommand(id), ct) }));

    // ── helpers ──
    [HttpGet("LookupPicks")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> LookupPicks([FromQuery] string lookupType, [FromQuery] string? q, CancellationToken ct)
        => Ok(ResponseViewModel<List<LookupPickDto>>.Ok(await _mediator.Send(new ListLookupPicksQuery(lookupType, q), ct)));

    [HttpGet("FieldDomains")]
    [RequiresPrivilege("ReferenceLists.View")]
    public async Task<IActionResult> FieldDomains(CancellationToken ct)
        => Ok(ResponseViewModel<List<FieldDomainDto>>.Ok(await _mediator.Send(new ListFieldDomainsQuery(), ct)));
}

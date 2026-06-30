using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.AutoFlags;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Auto Flags — configuration-driven automatic red-flag rules (/admin/auto-flags).
/// A dedicated, separately-permissioned surface over the same TmX_Flag_Catalogue
/// (Flag_Type=Automated) + TmX_Flag_Rule. P1 covers the reference-list / field /
/// static archetypes — the generalisation of the legacy country/HS screening.
///
///   GET  /api/v1/AutoFlag                       — list auto flags
///   GET  /api/v1/AutoFlag/Operands?productId=   — operand (field) catalogue for the builder
///   GET  /api/v1/AutoFlag/ReferenceLists        — reference-list options
///   GET  /api/v1/AutoFlag/{id}                  — detail (rules + scope)
///   POST /api/v1/AutoFlag                       — create (Draft)
///   PUT  /api/v1/AutoFlag/{id}                  — update metadata/scope/rules
///   POST /api/v1/AutoFlag/{id}/SubmitForApproval
///   POST /api/v1/AutoFlag/{id}/Approve          — activate (begins evaluating)
///   POST /api/v1/AutoFlag/{id}/Test             — dry-run against a transaction id
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class AutoFlagController : ControllerBase
{
    private readonly IMediator _mediator;
    public AutoFlagController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequiresPrivilege("AutoFlags.View")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ResponseViewModel<List<AutoFlagSummaryDto>>.Ok(await _mediator.Send(new ListAutoFlagsQuery(), ct)));

    [HttpGet("Operands")]
    [RequiresPrivilege("AutoFlags.View")]
    public async Task<IActionResult> Operands([FromQuery] int? productId, CancellationToken ct)
        => Ok(ResponseViewModel<List<OperandOptionDto>>.Ok(await _mediator.Send(new ListAutoFlagOperandsQuery(productId), ct)));

    [HttpGet("ReferenceLists")]
    [RequiresPrivilege("AutoFlags.View")]
    public async Task<IActionResult> ReferenceLists(CancellationToken ct)
        => Ok(ResponseViewModel<List<ReferenceListOptionDto>>.Ok(await _mediator.Send(new ListAutoFlagReferenceListsQuery(), ct)));

    [HttpGet("{id:int}")]
    [RequiresPrivilege("AutoFlags.View")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetAutoFlagByIdQuery(id), ct);
        if (dto is null) return NotFound(ResponseViewModel<AutoFlagDetailDto>.Fail(404, "auto_flag_not_found", "Auto flag not found."));
        return Ok(ResponseViewModel<AutoFlagDetailDto>.Ok(dto));
    }

    [HttpPost]
    [RequiresPrivilege("AutoFlags.Author")]
    public async Task<IActionResult> Create([FromBody] CreateAutoFlagCommand cmd, CancellationToken ct)
    {
        var id = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagId = id }));
    }

    [HttpPut("{id:int}")]
    [RequiresPrivilege("AutoFlags.Author")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAutoFlagCommand cmd, CancellationToken ct)
    {
        cmd.FlagId = id;
        await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagId = id }));
    }

    [HttpPost("{id:int}/SubmitForApproval")]
    [RequiresPrivilege("AutoFlags.Author")]
    public async Task<IActionResult> Submit(int id, CancellationToken ct)
        => Ok(ResponseViewModel<object>.Ok(new { ok = await _mediator.Send(new SubmitAutoFlagCommand(id), ct) }));

    [HttpPost("{id:int}/Approve")]
    [RequiresPrivilege("AutoFlags.Approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
        => Ok(ResponseViewModel<object>.Ok(new { ok = await _mediator.Send(new ApproveAutoFlagCommand(id), ct) }));

    [HttpPost("{id:int}/Retire")]
    [RequiresPrivilege("AutoFlags.Approve")]
    public async Task<IActionResult> Retire(int id, CancellationToken ct)
        => Ok(ResponseViewModel<object>.Ok(new { ok = await _mediator.Send(new RetireAutoFlagCommand(id), ct) }));

    [HttpDelete("{id:int}")]
    [RequiresPrivilege("AutoFlags.Author")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Ok(ResponseViewModel<object>.Ok(new { ok = await _mediator.Send(new DeleteAutoFlagCommand(id), ct) }));

    public sealed class TestBody { public string transactionId { get; set; } = ""; }

    [HttpPost("{id:int}/Test")]
    [RequiresPrivilege("AutoFlags.View")]
    public async Task<IActionResult> Test(int id, [FromBody] TestBody body, CancellationToken ct)
        => Ok(ResponseViewModel<AutoFlagTestResultDto>.Ok(
            await _mediator.Send(new TestAutoFlagCommand(id, body?.transactionId ?? ""), ct)));
}

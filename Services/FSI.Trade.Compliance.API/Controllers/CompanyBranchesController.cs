using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.AllLov;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// All-branches LOV for Reports and admin flows. Slice 7. Returns every
/// effective branch (Active_Flag + today inside effective window) — no
/// user-scoping. Pair with <see cref="CompanyBranchController"/> (singular)
/// which exposes the user-scoped LOV for transaction-creation flows.
///
/// Plural URL — matches the FE businessReports api which calls
/// <c>GET /api/v1/CompanyBranches</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class CompanyBranchesController : ControllerBase
{
    private readonly IMediator _mediator;
    public CompanyBranchesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListAllBranchesLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<AllBranchesLovItemDto>>.Ok(rows));
    }
}

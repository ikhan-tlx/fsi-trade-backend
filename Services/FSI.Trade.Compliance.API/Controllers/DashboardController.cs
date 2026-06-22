using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Operations dashboard (landing page). Tenant-scoped aggregate read.
///
///   GET /api/v1/Dashboard/Summary?range=7d|30d|QTD|YTD
///
/// No <c>[RequiresPrivilege]</c> — this is the landing page for every
/// authenticated user (same rationale as the lookup catalogue). Gate later
/// with a universal <c>Dashboard.View</c> privilege if admin-scoping is needed.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;
    public DashboardController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Aggregate for the operations dashboard. <c>range</c> defaults to 30d.
    /// Phase 1: KPIs / risk-mix / instruments populated; exposure, country,
    /// EDD and FATF come back null/empty until they're modeled.
    /// </summary>
    [HttpGet("Summary")]
    [RequiresPrivilege("Dashboard.View")]
    public async Task<IActionResult> Summary([FromQuery] string? range, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetDashboardSummaryQuery(range), ct);
        return Ok(ResponseViewModel<DashboardSummaryDto>.Ok(dto));
    }
}

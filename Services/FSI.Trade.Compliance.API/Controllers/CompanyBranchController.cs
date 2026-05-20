using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Lov;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Company-branch surface. Slice 6.5 first endpoint is the LOV used by FE
/// dropdowns + the wizard's branch-selection step. Future slices may add
/// branch CRUD if/when the bank wants admin-side branch management.
///
/// Legacy mapping:
/// - GET /api/v1/CompanyBranch/lov → /CompanyBranch/lov (same)
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class CompanyBranchController : ControllerBase
{
    private readonly IMediator _mediator;
    public CompanyBranchController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Returns the effective branches the AUTHENTICATED caller is mapped to.
    /// </summary>
    [HttpGet("lov")]
    public async Task<IActionResult> Lov(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListBranchLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<BranchLovItemDto>>.Ok(rows));
    }
}

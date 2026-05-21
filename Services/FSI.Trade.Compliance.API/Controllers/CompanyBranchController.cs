using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.AllLov;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Lov;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Company-branch surface. Two read endpoints with intentionally
/// different scoping semantics — picked deliberately rather than
/// folded together because the security model differs:
///
///   GET /api/v1/CompanyBranch/lov  — user-scoped (caller's mapped branches)
///   GET /api/v1/CompanyBranch/all  — unscoped (every active branch)
///
/// Use <c>/lov</c> for transaction-creation flows where the user can
/// only target their own branches. Use <c>/all</c> for Reports and
/// admin flows that legitimately need to see every branch.
///
/// Future slices may add branch CRUD if/when the bank wants
/// admin-side branch management.
///
/// History: in Slice 7 the all-branches endpoint briefly lived on a
/// separate <c>CompanyBranchesController</c> (plural) to match a FE
/// expectation, but that diverged from the codebase's singular-
/// controller convention. Consolidated here.
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

    /// <summary>
    /// Returns ALL effective branches (no user-scoping). For Reports
    /// filters and admin flows. Slice 7. Replaces the plural-name
    /// <c>CompanyBranchesController</c> (deleted during consolidation).
    /// </summary>
    [HttpGet("all")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListAllBranchesLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<AllBranchesLovItemDto>>.Ok(rows));
    }
}

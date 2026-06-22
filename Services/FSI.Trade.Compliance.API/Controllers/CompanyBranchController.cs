using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.AllLov;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.ByCompany;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.CheckCode;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Create;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.GetById;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.List;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Lov;
using FSI.Trade.Compliance.Application.Features.CompanyBranches.Update;
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
    /// Paged admin grid of branches (server-side sort + filter). Backs the
    /// FE "Company Branches" management page. Slice 14.
    ///
    ///   GET /api/v1/CompanyBranch?page=1&amp;pageSize=10&amp;sort=BranchName-asc&amp;filter=karachi
    /// </summary>
    [HttpGet]
    [RequiresPrivilege("Masterdata.View")]
    public async Task<IActionResult> List([FromQuery] ListCompanyBranchesQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<CompanyBranchListItemDto>>.Ok(paged));
    }

    /// <summary>Single branch by id (edit form). Slice 14.</summary>
    [HttpGet("{id:int}")]
    [RequiresPrivilege("Masterdata.View")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetCompanyBranchByIdQuery(id), ct);
        return Ok(ResponseViewModel<CompanyBranchListItemDto>.Ok(dto));
    }

    /// <summary>Create a branch. Slice 14.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompanyBranchCommand cmd, CancellationToken ct)
    {
        var id = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { companyBranchId = id }, "Branch created"));
    }

    /// <summary>Update a branch. Slice 14.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCompanyBranchCommand body, CancellationToken ct)
    {
        body.CompanyBranchId = id;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { companyBranchId = id }, "Branch updated"));
    }

    /// <summary>
    /// Branch-code availability check. Returns status.message containing
    /// "already exist" when taken (the FE keys off that substring).
    /// </summary>
    [HttpGet("CheckCompanyBranch/{code}")]
    public async Task<IActionResult> CheckCompanyBranch(string code, CancellationToken ct)
    {
        var exists = await _mediator.Send(new CheckCompanyBranchQuery(code), ct);
        var message = exists ? "Branch code already exists" : "Branch code is available";
        return Ok(ResponseViewModel<object>.Ok(new { exists }, message));
    }

    /// <summary>
    /// Branches for a given company — the legacy company→branch cascade
    /// (the new FE user form's "Company Branch" dropdown). Slice 14.
    /// </summary>
    [HttpGet("lov/{companyId:int}")]
    public async Task<IActionResult> ByCompany(int companyId, CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListBranchesByCompanyQuery(companyId), ct);
        return Ok(ResponseViewModel<IReadOnlyList<BranchByCompanyDto>>.Ok(rows));
    }

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

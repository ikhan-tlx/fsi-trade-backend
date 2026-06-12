using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Companies.Lov;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Company surface over <c>TmX_Company</c>. Slice 14 — introduced because the
/// new BE had no Company controller, so the user form's "Select Company"
/// dropdown (GET /api/v1/Company/lov) 404'd and stayed empty.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class CompanyController : ControllerBase
{
    private readonly IMediator _mediator;
    public CompanyController(IMediator mediator) => _mediator = mediator;

    /// <summary>Companies for dropdowns (Company_Id / "Code-Name").</summary>
    [HttpGet("lov")]
    public async Task<IActionResult> Lov(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListCompaniesLovQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<CompanyLovItemDto>>.Ok(rows));
    }
}

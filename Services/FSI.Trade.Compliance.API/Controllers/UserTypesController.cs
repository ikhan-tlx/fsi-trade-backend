using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.UserTypes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// User-type reference list. Slice 14 — the FE Users screen calls
/// <c>GET /api/v1/UserTypes</c> for the "User Type" dropdown; the new BE had
/// no such route (404). Backed by <c>TmX_Lookup</c> rows of type
/// <c>'USER'</c>, matching the legacy
/// <c>GET /api/v1/Lookup/GetByLookupType/USER</c> contract.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class UserTypesController : ControllerBase
{
    private readonly IMediator _mediator;
    public UserTypesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListUserTypesQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<UserTypeLookupDto>>.Ok(rows));
    }
}

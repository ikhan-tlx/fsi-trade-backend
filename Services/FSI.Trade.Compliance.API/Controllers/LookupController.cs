using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByType;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Reference-data catalog for FE app-init.
///
/// Two endpoints:
///   GET /api/v1/Lookup/{culture}                         — full catalog (Slice 3)
///   GET /api/v1/Lookup/GetByType?type=X&amp;culture=en   — single lookup type (Slice 6.5)
///
/// No <c>[RequiresPrivilege]</c> on either — every authenticated user needs
/// the catalog to operate the app.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class LookupController : ControllerBase
{
    private readonly IMediator _mediator;
    public LookupController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{culture}")]
    public async Task<IActionResult> GetByCulture(string culture, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetLookupsByCultureQuery(culture), ct);
        return Ok(ResponseViewModel<List<LookupItemDto>>.Ok(rows));
    }

    /// <summary>
    /// Slice 6.5 — single lookup type. Useful when the FE only needs one
    /// type's rows (e.g. ProductTypes, Currency) and doesn't want to pull
    /// the whole catalog. <c>culture</c> defaults to "en" if not supplied.
    /// </summary>
    [HttpGet("GetByType")]
    public async Task<IActionResult> GetByType([FromQuery] string type, [FromQuery] string? culture, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetLookupsByTypeQuery(type, culture), ct);
        return Ok(ResponseViewModel<IReadOnlyList<LookupItemDto>>.Ok(rows));
    }
}

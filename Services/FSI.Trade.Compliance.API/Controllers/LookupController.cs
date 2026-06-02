using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByType;
using FSI.Trade.Compliance.Application.Features.Lookups.GetGroupedByCulture;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Reference-data catalogue for FE app-init.
///
/// Endpoint inventory:
///   GET /api/v1/Lookup/{culture}                          — flat list (legacy parity)
///   GET /api/v1/Lookup/GetByType?type=X&amp;culture=en    — single type
///   GET /api/v2/Lookup/{culture}                          — grouped dictionary (NEW)
///
/// No <c>[RequiresPrivilege]</c> — every authenticated user needs the
/// catalogue to operate the app. Cross-tenant cascade applies on all
/// three actions (caller's tenant &gt; Tenant 1 &gt; lowest with data).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class LookupController : ControllerBase
{
    private readonly IMediator _mediator;
    public LookupController(IMediator mediator) => _mediator = mediator;

    /// <summary>v1 — full catalogue as a flat list. FE groups client-side.</summary>
    [HttpGet("{culture}")]
    [MapToApiVersion("1.0")]
    public async Task<IActionResult> GetByCulture(string culture, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetLookupsByCultureQuery(culture), ct);
        return Ok(ResponseViewModel<List<LookupItemDto>>.Ok(rows));
    }

    /// <summary>
    /// Slice 6.5 — single lookup type. Useful when the FE only needs one
    /// type's rows (e.g. ProductTypes, Currency) and doesn't want to pull
    /// the whole catalogue. <c>culture</c> defaults to "en" if not supplied.
    /// </summary>
    [HttpGet("GetByType")]
    [MapToApiVersion("1.0")]
    public async Task<IActionResult> GetByType([FromQuery] string type, [FromQuery] string? culture, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetLookupsByTypeQuery(type, culture), ct);
        return Ok(ResponseViewModel<IReadOnlyList<LookupItemDto>>.Ok(rows));
    }

    /// <summary>
    /// v2 — grouped catalogue. Response is a dictionary keyed by
    /// <c>lookupType</c>; per-row records carry only
    /// <c>id / code / label / parent</c>. Eliminates client-side grouping
    /// and shrinks the payload by ~60% relative to v1.
    ///
    /// Same cross-tenant cascade + culture resolution as v1; only the
    /// projection shape differs.
    /// </summary>
    [HttpGet("{culture}")]
    [MapToApiVersion("2.0")]
    public async Task<IActionResult> GetGroupedByCulture(string culture, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetLookupsGroupedByCultureQuery(culture), ct);
        return Ok(ResponseViewModel<LookupCatalogueDto>.Ok(dto));
    }
}

using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Audit.Export;
using FSI.Trade.Compliance.Application.Features.Audit.List;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>Audit trail reads. GET /api/v1/Audit — paged, filterable, newest first.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly IMediator _mediator;
    public AuditController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListAuditQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<AuditLogListItemDto>>.Ok(paged));
    }

    /// <summary>
    /// Export the (filtered) audit trail as a downloadable report — Excel (xlsx) or
    /// PDF. Uses the same filter/sort params as the list. Binary file response; the
    /// export is itself recorded as an EXPORT audit event.
    /// GET /api/v1/Audit/Export?format=xlsx|pdf&amp;filter=&amp;dateFrom=&amp;…
    /// </summary>
    [HttpGet("Export")]
    public async Task<IActionResult> Export([FromQuery] ExportAuditTrailQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return File(result.Content, result.ContentType, result.FileName);
    }
}

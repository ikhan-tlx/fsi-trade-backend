using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Flags.Stats;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Slice 8 — Flag catalogue + stats endpoints. The per-transaction
/// flag read sits on <see cref="TransactionController.ListFlags"/>;
/// per-transaction writes happen as part of
/// <c>PUT /Transaction/{id}</c>. This controller is for
/// catalogue-level and cross-transaction queries:
///
///   GET /api/v1/Flag/Stats/TopFlagged
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class FlagController : ControllerBase
{
    private readonly IMediator _mediator;
    public FlagController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Top-N flags ranked by number of transactions on which they're
    /// currently set. Supports optional date-window and product
    /// filters. Aggregation runs on-the-fly (no materialised views).
    /// </summary>
    [HttpGet("Stats/TopFlagged")]
    public async Task<IActionResult> TopFlagged(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int?      productId,
        [FromQuery] int       take,
        CancellationToken     ct)
    {
        if (take <= 0) take = 10;
        var rows = await _mediator.Send(new TopFlaggedQuery(from, to, productId, take), ct);
        return Ok(ResponseViewModel<IReadOnlyList<TopFlaggedRowDto>>.Ok(rows));
    }
}

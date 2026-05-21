using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Flags.Read;
using FSI.Trade.Compliance.Application.Features.Flags.Stats;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Slice 8 — Flag catalogue + scope reads + stats endpoints.
///
///   GET /api/v1/Flag/Product/{productId}                 — all flags scoped to a product
///   GET /api/v1/Flag/Product/{productId}/Tab/{tabId}     — flags scoped to a product+tab
///   GET /api/v1/Flag/Stats/TopFlagged?from=&amp;to=&amp;productId=&amp;take=
///
/// Per-transaction flag read sits on <see cref="TransactionController.ListFlags"/>;
/// per-transaction writes happen as part of
/// <c>PUT /Transaction/{id}</c>.
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
    /// Every flag applicable to a product, across all tabs. Used by
    /// the transaction-create flow where no Transaction_Id exists yet —
    /// the FE renders the flag panel from this list and ticks land in
    /// memory until the first <c>POST /Transaction</c> + follow-up
    /// <c>PUT /Transaction/{id}</c>.
    ///
    /// Response shape is identical to the per-transaction flag list
    /// (<see cref="TransactionController.ListFlags"/>) — transaction-
    /// state fields come back null / isFlagged=false so the FE renders
    /// the same component.
    /// </summary>
    [HttpGet("Product/{productId:int}")]
    public async Task<IActionResult> ListByProduct(int productId, CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListFlagsByScopeQuery(productId, null), ct);
        return Ok(ResponseViewModel<IReadOnlyList<TransactionFlagDto>>.Ok(rows));
    }

    /// <summary>
    /// Flags applicable to a product narrowed to a single tab. Same
    /// shape as <see cref="ListByProduct"/>; useful when the FE wants
    /// to lazy-load flags tab-by-tab on big-product forms.
    /// </summary>
    [HttpGet("Product/{productId:int}/Tab/{tabId:int}")]
    public async Task<IActionResult> ListByProductAndTab(
        int productId,
        int tabId,
        CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListFlagsByScopeQuery(productId, tabId), ct);
        return Ok(ResponseViewModel<IReadOnlyList<TransactionFlagDto>>.Ok(rows));
    }

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

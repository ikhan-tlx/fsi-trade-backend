using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Flags.Read;
using FSI.Trade.Compliance.Application.Features.Transactions.Cancel;
using FSI.Trade.Compliance.Application.Features.Transactions.Create;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using FSI.Trade.Compliance.Application.Features.Transactions.List;
using FSI.Trade.Compliance.Application.Features.Transactions.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Trade Repository surface — replaces the legacy
/// <c>GET /api/v1/Transaction</c> grid endpoint. Subsequent slices add:
///
///   POST   /api/v1/Transaction               create + kick off workflow
///   GET    /api/v1/Transaction/{id}          read with udf detail
///   PUT    /api/v1/Transaction/{id}          edit
///   POST   /api/v1/Transaction/{id}/Workflow advance the workflow
///   POST   /api/v1/Transaction/{id}/Cancel   cancel
///
/// Slice 6 Step 1 ships the list only.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class TransactionController : ControllerBase
{
    private readonly IMediator _mediator;
    public TransactionController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListTransactionsQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<TransactionListItemDto>>.Ok(paged));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var detail = await _mediator.Send(new GetTransactionByIdQuery(id), ct);
        return Ok(ResponseViewModel<TransactionDetailDto>.Ok(detail));
    }

    /// <summary>
    /// Creates a new transaction and kicks off its workflow synchronously
    /// (Slice 6 Step 3). Mirrors legacy <c>POST /Transaction/Create</c> —
    /// minimal payload, workflow attached when the product has a scheme code.
    /// Response shape matches GET /Transaction/{id}.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionCommand body, CancellationToken ct)
    {
        var detail = await _mediator.Send(body, ct);
        return CreatedAtAction(nameof(GetById),
            new { id = detail.transactionId, version = "1.0" },
            ResponseViewModel<TransactionDetailDto>.Ok(detail));
    }

    /// <summary>
    /// Saves the current state of an existing transaction (Slice 6 Step 4).
    /// Header + UDF + customer snapshot + beneficiaries + stakeholders all
    /// in one body. Workflow state untouched — that's the separate
    /// /Workflow/Process/{id}/Execute endpoint.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTransactionCommand body, CancellationToken ct)
    {
        // Bind the route id over whatever (if anything) the body included.
        // Treat the URL as the canonical identifier.
        body.transactionId = id;

        var detail = await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<TransactionDetailDto>.Ok(detail));
    }

    /// <summary>
    /// Cancels an existing transaction (Slice 6 Step 5). Forces the workflow
    /// to the "Application Cancelled" state if a workflow instance is
    /// attached, then writes the cancelled status lookup id to the
    /// transaction row. Optional reason body is logged through the workflow
    /// SetState audit channel.
    /// </summary>
    [HttpPost("{id:int}/Cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelTransactionCommand? body, CancellationToken ct)
    {
        body ??= new CancelTransactionCommand();
        body.transactionId = id;

        var detail = await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<TransactionDetailDto>.Ok(detail));
    }

    /// <summary>
    /// Slice 8 — flag panel for a transaction. Same projection embedded
    /// in <c>GET /Transaction/{id}.flags</c>; exposed standalone for
    /// stats / admin / integration callers that want only the flag list
    /// without the rest of the transaction payload. Flag writes happen
    /// as part of <c>PUT /Transaction/{id}</c> (no separate save button
    /// on the form).
    /// </summary>
    [HttpGet("{id:int}/Flags")]
    public async Task<IActionResult> ListFlags(int id, CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListTransactionFlagsQuery(id), ct);
        return Ok(ResponseViewModel<IReadOnlyList<TransactionFlagDto>>.Ok(rows));
    }
}

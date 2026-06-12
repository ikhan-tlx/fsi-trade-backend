using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Flags.Read;
using FSI.Trade.Compliance.Application.Features.Transactions.Cancel;
using FSI.Trade.Compliance.Application.Features.Transactions.Create;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using FSI.Trade.Compliance.Application.Features.Transactions.List;
using FSI.Trade.Compliance.Application.Features.Transactions.ResubmitFccm;
using FSI.Trade.Compliance.Application.Features.Transactions.Update;
using FSI.Trade.Compliance.Application.Features.Documents.Upload;
using FSI.Trade.Compliance.Application.Features.Transactions.AuditTrail;
using FSI.Trade.Compliance.Application.Features.Transactions.Documents;
using FSI.Trade.Compliance.Application.Features.Transactions.Notes;
using FSI.Trade.Compliance.Application.Features.Transactions.WorkflowHistory;
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
    [RequiresPrivilege("Transactions.Create")]
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
    [RequiresPrivilege("Transactions.Edit")]
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
    // Per-transaction flag rail — available to any authenticated caller, like
    // the unprivileged GET /Transaction/{id} whose `flags` it mirrors.
    // (Flags.View gates the flag-catalogue ADMIN screens, not this rail.)
    [HttpGet("{id:int}/Flags")]
    public async Task<IActionResult> ListFlags(int id, CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListTransactionFlagsQuery(id), ct);
        return Ok(ResponseViewModel<IReadOnlyList<TransactionFlagDto>>.Ok(rows));
    }

    /// <summary>
    /// Slice 9 — manually re-fire the FCCM trade submission for a
    /// transaction. Use when the workflow-post-execute hook failed
    /// (FCCM offline, timeout, etc.) and the analyst wants to retry
    /// without re-running the workflow command.
    ///
    /// Clears the existing <c>Fccm_Case_Id</c> first, then re-runs the
    /// full pipeline. The previous FCCM case (if any) is NOT deleted —
    /// compliance team handles downstream de-duping.
    ///
    /// Gated by <c>Transactions.Resubmit</c> privilege.
    /// </summary>
    /// <summary>
    /// Slice 10 §7.1 — chronological workflow history for the transaction.
    /// Each entry carries the activity name, executor display name, command
    /// triggered, and entered/exited timestamps. The entry with
    /// <c>exitedAt = null</c> is the current stage.
    ///
    /// Driven by OptimaJet's <c>WorkflowProcessTransitionHistory</c> table.
    /// Returns an empty list when no workflow has been attached or no
    /// transitions have fired yet.
    /// </summary>
    [HttpGet("{id:int}/WorkflowHistory")]
    public async Task<IActionResult> GetWorkflowHistory(int id, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetWorkflowHistoryQuery(id), ct);
        return Ok(ResponseViewModel<IReadOnlyList<WorkflowHistoryEntryDto>>.Ok(rows));
    }

    /// <summary>
    /// Slice 10 §7.3 — composed audit trail for the transaction. Merges
    /// lifecycle stamps (Created / Updated) + workflow transitions +
    /// remarks into a single chronological feed. No dedicated audit
    /// table per design — all sources are existing.
    /// </summary>
    [HttpGet("{id:int}/AuditTrail")]
    public async Task<IActionResult> GetAuditTrail(int id, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetAuditTrailQuery(id), ct);
        return Ok(ResponseViewModel<IReadOnlyList<AuditTrailEntryDto>>.Ok(rows));
    }

    /// <summary>
    /// Slice 10 §7.4 — lists documents directly attached to the transaction.
    /// Flag-evidence documents are NOT included; those live on the Flags
    /// panel via the per-flag evidenceDocumentId.
    /// </summary>
    [HttpGet("{id:int}/Documents")]
    public async Task<IActionResult> ListDocuments(int id, CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListTransactionDocumentsQuery(id), ct);
        return Ok(ResponseViewModel<IReadOnlyList<TransactionDocumentDto>>.Ok(rows));
    }

    /// <summary>
    /// Slice 10 §7.5 — appends a note to the transaction. The note appears
    /// in the existing <c>remarks</c> list on the detail endpoint
    /// (<c>GET /Transaction/{id}</c>), so no separate GET is needed.
    /// </summary>
    [HttpPost("{id:int}/Notes")]
    [RequiresPrivilege("Transactions.AddNote")]
    public async Task<IActionResult> AddNote(int id, [FromBody] AddTransactionNoteCommand body, CancellationToken ct)
    {
        body.transactionId = id;
        var result = await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<AddTransactionNoteResult>.Ok(result));
    }

    /// <summary>
    /// Slice 10 §7.4 — attaches a document directly to the transaction.
    /// Multipart form data (single file under field name <c>file</c>).
    /// Hidden from Swagger (same Swashbuckle 6.8.x quirk as
    /// <c>POST /Document</c>); test with curl.
    /// </summary>
    [HttpPost("{id:int}/Documents")]
    [RequiresPrivilege("Transactions.UploadDocument")]
    [RequestSizeLimit(50_000_000)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> UploadDocument(
        int                 id,
        IFormFile           file,
        [FromQuery] string? subfolderHint,
        CancellationToken   ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ResponseViewModel<object>.Fail(
                400, "file_required", "Upload requires a non-empty file field."));

        await using var stream = file.OpenReadStream();

        var result = await _mediator.Send(new UploadDocumentCommand
        {
            Content           = stream,
            OriginalFileName  = file.FileName,
            MimeType          = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType,
            SubfolderHint     = string.IsNullOrWhiteSpace(subfolderHint)    ? "transaction-docs" : subfolderHint!,
            TransactionId     = id
        }, ct);

        return Ok(ResponseViewModel<UploadDocumentResult>.Ok(result));
    }

    [HttpPost("{id:int}/ResubmitToFccm")]
    [RequiresPrivilege("Transactions.Resubmit")]
    public async Task<IActionResult> ResubmitToFccm(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ResubmitTransactionToFccmCommand
        {
            transactionId = id
        }, ct);
        return Ok(ResponseViewModel<ResubmitTransactionToFccmResult>.Ok(result));
    }
}

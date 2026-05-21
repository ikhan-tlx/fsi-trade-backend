using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Create;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Get;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.List;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.SetActive;
using FSI.Trade.Compliance.Application.Features.Flags.Admin.Update;
using FSI.Trade.Compliance.Application.Features.Flags.Read;
using FSI.Trade.Compliance.Application.Features.Flags.Stats;
using FSI.Trade.Compliance.Application.Features.Transactions.Detail;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Slice 8 — Flag catalogue + scope reads + stats + admin CRUD.
///
/// Read surface (Flags.View):
///   GET    /api/v1/Flag                                  — paged catalogue list for admin grid
///   GET    /api/v1/Flag/{id}                             — single flag detail + scopes
///   GET    /api/v1/Flag/Product/{productId}              — all flags scoped to a product
///   GET    /api/v1/Flag/Product/{productId}/Tab/{tabId}  — narrowed to a tab
///   GET    /api/v1/Flag/Stats/TopFlagged                 — top-N flagged-transaction stats
///
/// Catalogue admin (Flags.Manage):
///   POST   /api/v1/Flag                                  — create new catalogue entry
///   PUT    /api/v1/Flag/{id}                             — update fields
///   POST   /api/v1/Flag/{id}/Activate                    — Active_Flag = true
///   POST   /api/v1/Flag/{id}/Deactivate                  — Active_Flag = false
///
/// Scope admin (Flags.Manage):
///   POST   /api/v1/Flag/{id}/Scopes                      — deploy flag to a (product, tab)
///   PUT    /api/v1/Flag/Scope/{scopeId}                  — toggle visibility / re-order
///   DELETE /api/v1/Flag/Scope/{scopeId}                  — permanently remove a scope
///
/// Per-transaction flag read sits on <see cref="TransactionController.ListFlags"/>;
/// per-transaction writes happen as part of <c>PUT /Transaction/{id}</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class FlagController : ControllerBase
{
    private readonly IMediator _mediator;
    public FlagController(IMediator mediator) => _mediator = mediator;

    // =====================================================================
    // Catalogue read (admin grid + detail)
    // =====================================================================

    /// <summary>Paged catalogue list with filter / sort for the admin UI.</summary>
    [HttpGet]
    [RequiresPrivilege("Flags.View")]
    public async Task<IActionResult> List([FromQuery] ListFlagCatalogueQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<FlagCatalogueListItemDto>>.Ok(paged));
    }

    /// <summary>Single flag detail + every scope it's deployed to.</summary>
    [HttpGet("{id:int}")]
    [RequiresPrivilege("Flags.View")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetFlagByIdQuery(id), ct);
        return Ok(ResponseViewModel<FlagCatalogueDetailDto>.Ok(dto));
    }

    // =====================================================================
    // Catalogue admin (create / edit / activate / deactivate)
    // =====================================================================

    /// <summary>
    /// Create a new catalogue flag. Flag_Code auto-generated when blank
    /// (hash of description + category prefix). Returns the new flagId.
    /// New flag starts unscoped — follow up with POST /Flag/{id}/Scopes.
    /// </summary>
    [HttpPost]
    [RequiresPrivilege("Flags.Manage")]
    public async Task<IActionResult> Create([FromBody] CreateFlagCommand cmd, CancellationToken ct)
    {
        var newId = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagId = newId }));
    }

    /// <summary>
    /// Update editable catalogue fields (name / description / category /
    /// severity / weight / requires-evidence / source). Flag_Code is NOT
    /// editable — see UpdateFlagCommand XML doc for the rationale.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequiresPrivilege("Flags.Manage")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFlagCommand body, CancellationToken ct)
    {
        body.flagId = id;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagId = id }));
    }

    /// <summary>Catalogue-level Active_Flag = true (every scope respects this).</summary>
    [HttpPost("{id:int}/Activate")]
    [RequiresPrivilege("Flags.Manage")]
    public async Task<IActionResult> Activate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetFlagActiveCommand(id, true), ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagId = id, activeFlag = true }));
    }

    /// <summary>Catalogue-level Active_Flag = false (retires the flag globally).</summary>
    [HttpPost("{id:int}/Deactivate")]
    [RequiresPrivilege("Flags.Manage")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        await _mediator.Send(new SetFlagActiveCommand(id, false), ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagId = id, activeFlag = false }));
    }

    // =====================================================================
    // Scope admin (deploy flag to product/tab; toggle visibility; remove)
    // =====================================================================

    /// <summary>
    /// Deploy a flag to a (product, tab) combination — creates a new
    /// TmX_Flag_Scope row. Returns the new flagScopeId.
    /// 409 if a scope for that (flag, product, tab) already exists.
    /// </summary>
    [HttpPost("{id:int}/Scopes")]
    [RequiresPrivilege("Flags.Manage")]
    public async Task<IActionResult> AddScope(int id, [FromBody] AddFlagScopeCommand body, CancellationToken ct)
    {
        body.flagId = id;
        var newScopeId = await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagScopeId = newScopeId }));
    }

    /// <summary>
    /// Toggle a scope's Active_Flag (the legacy "Visibility = '0'"
    /// use case) or re-order it. Other fields (flag / product / tab)
    /// are immutable on a scope — recreate to move.
    /// </summary>
    [HttpPut("Scope/{scopeId:int}")]
    [RequiresPrivilege("Flags.Manage")]
    public async Task<IActionResult> UpdateScope(int scopeId, [FromBody] UpdateFlagScopeCommand body, CancellationToken ct)
    {
        body.flagScopeId = scopeId;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagScopeId = scopeId }));
    }

    /// <summary>
    /// Permanently delete a scope row. Existing TmX_Transaction_Flag
    /// rows pointing at the parent flag are NOT affected — historical
    /// transaction state stays intact.
    /// </summary>
    [HttpDelete("Scope/{scopeId:int}")]
    [RequiresPrivilege("Flags.Manage")]
    public async Task<IActionResult> RemoveScope(int scopeId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveFlagScopeCommand(scopeId), ct);
        return Ok(ResponseViewModel<object>.Ok(new { flagScopeId = scopeId, removed = true }));
    }

    // =====================================================================
    // Per-product flag scope reads (used by the create-flow on the FE)
    // =====================================================================

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
    [RequiresPrivilege("Flags.View")]
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
    [RequiresPrivilege("Flags.View")]
    public async Task<IActionResult> ListByProductAndTab(
        int productId,
        int tabId,
        CancellationToken ct)
    {
        var rows = await _mediator.Send(new ListFlagsByScopeQuery(productId, tabId), ct);
        return Ok(ResponseViewModel<IReadOnlyList<TransactionFlagDto>>.Ok(rows));
    }

    // =====================================================================
    // Stats
    // =====================================================================

    /// <summary>
    /// Top-N flags ranked by number of transactions on which they're
    /// currently set. Supports optional date-window and product
    /// filters. Aggregation runs on-the-fly (no materialised views).
    /// </summary>
    [HttpGet("Stats/TopFlagged")]
    [RequiresPrivilege("Flags.View")]
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

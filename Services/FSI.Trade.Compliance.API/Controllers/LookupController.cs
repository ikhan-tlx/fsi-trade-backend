using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.LookupAdmin;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;
using FSI.Trade.Compliance.Application.Features.Lookups.GetByType;
using FSI.Trade.Compliance.Application.Features.Lookups.GetGroupedByCulture;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Lookup / reference-data controller. Two concerns live here on purpose (one
/// controller per resource, not one per job):
///
///   1. Read-only catalogue for FE app-init (no privilege — every authenticated
///      user needs it; cross-tenant cascade applies):
///        GET /api/v1/Lookup/{culture}                       — flat list (legacy parity)
///        GET /api/v1/Lookup/GetByType?type=X&amp;culture=en — single type
///        GET /api/v2/Lookup/{culture}                       — grouped dictionary
///
///   2. Lookup Management admin surface (/admin/lookups) — maintain the VALUES of
///      a curated, allow-listed set of TmX_Lookup types (types are NEVER created/
///      renamed/deleted here — only their values, and only for allow-listed,
///      field-bound types; server re-checks on every read/write → 403
///      lookup_type_not_manageable). Gated by LookupManagement.View/.Manage.
///      Writes are maker-checker: POST/PUT/PATCH/DELETE do NOT mutate TmX_Lookup
///      synchronously — each validates then creates a PENDING Request (TmX_Request,
///      request-type "0002") and returns { requestId, status: "pending_approval" };
///      the change is applied by the Requests approval handler, which re-runs the
///      same validation (incl. the not-in-use delete guard) before committing.
///        GET    /api/v1/Lookup/Types
///        GET    /api/v1/Lookup/Types/{lookupType}/Usage
///        GET    /api/v1/Lookup/Values?lookupType=&amp;q=&amp;active=&amp;parentId=&amp;page=&amp;pageSize=&amp;sort=
///        GET    /api/v1/Lookup/Values/{id}
///        GET    /api/v1/Lookup/Values/{id}/Usage
///        POST   /api/v1/Lookup/Values
///        PUT    /api/v1/Lookup/Values/{id}
///        PATCH  /api/v1/Lookup/Values/{id}/Active   { isActive }
///        DELETE /api/v1/Lookup/Values/{id}
///
/// Route note: the admin actions use literal first segments ("Types" / "Values")
/// which take routing precedence over the catalogue's {culture} parameter, so
/// GET /api/v1/Lookup/Types resolves to ListTypes while GET /api/v1/Lookup/en
/// resolves to GetByCulture — no ambiguity (same pattern already used by
/// GetByType). Every admin action is pinned to v1 via [MapToApiVersion("1.0")]
/// so it does not leak onto the v2 catalogue.
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

    // ═══════════════════════════════════════════════════════════════════════
    //  Lookup Management (admin) — /admin/lookups. See class summary above.
    //  All v1-only; gated by LookupManagement.View/.Manage.
    // ═══════════════════════════════════════════════════════════════════════

    // ── types ──
    [HttpGet("Types")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.View")]
    public async Task<IActionResult> ListTypes([FromQuery] ListManagedTypesQuery query, CancellationToken ct)
        => Ok(ResponseViewModel<PagedResult<ManagedLookupTypeDto>>.Ok(await _mediator.Send(query, ct)));

    [HttpGet("Types/{lookupType}/Usage")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.View")]
    public async Task<IActionResult> TypeUsage(string lookupType, CancellationToken ct)
        => Ok(ResponseViewModel<List<LookupTypeUsageDto>>.Ok(await _mediator.Send(new GetLookupTypeUsageQuery(lookupType), ct)));

    // ── values ──
    [HttpGet("Values")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.View")]
    public async Task<IActionResult> ListValues([FromQuery] ListLookupValuesQuery query, CancellationToken ct)
        => Ok(ResponseViewModel<PagedResult<LookupValueDto>>.Ok(await _mediator.Send(query, ct)));

    // Candidate parents for a hierarchical type (resolves the foreign parent type, e.g. LOB for HSCodeList).
    [HttpGet("ParentOptions")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.View")]
    public async Task<IActionResult> ParentOptions([FromQuery] string lookupType, CancellationToken ct)
        => Ok(ResponseViewModel<List<LookupParentOptionDto>>.Ok(await _mediator.Send(new ListLookupParentOptionsQuery(lookupType), ct)));

    [HttpGet("Values/{id:int}")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.View")]
    public async Task<IActionResult> GetValue(int id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetLookupValueByIdQuery(id), ct);
        return dto is null
            ? NotFound(ResponseViewModel<LookupValueDto>.Fail(404, "lookup_value_not_found", "Lookup value not found."))
            : Ok(ResponseViewModel<LookupValueDto>.Ok(dto));
    }

    [HttpGet("Values/{id:int}/Usage")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.View")]
    public async Task<IActionResult> ValueUsage(int id, CancellationToken ct)
        => Ok(ResponseViewModel<LookupValueUsageDto>.Ok(await _mediator.Send(new GetLookupValueUsageQuery(id), ct)));

    [HttpPost("Values")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.Manage")]
    public async Task<IActionResult> CreateValue([FromBody] SaveLookupValueCommand cmd, CancellationToken ct)
    {
        cmd.id = null;
        var requestId = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { requestId, status = "pending_approval" }, "Submitted for approval"));
    }

    [HttpPut("Values/{id:int}")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.Manage")]
    public async Task<IActionResult> UpdateValue(int id, [FromBody] SaveLookupValueCommand cmd, CancellationToken ct)
    {
        cmd.id = id;
        var requestId = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { requestId, status = "pending_approval" }, "Submitted for approval"));
    }

    [HttpPatch("Values/{id:int}/Active")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.Manage")]
    public async Task<IActionResult> SetActive(int id, [FromBody] SetActiveRequest body, CancellationToken ct)
    {
        var requestId = await _mediator.Send(new SetLookupValueActiveCommand { id = id, isActive = body.isActive }, ct);
        return Ok(ResponseViewModel<object>.Ok(new { requestId, status = "pending_approval" }, "Submitted for approval"));
    }

    [HttpDelete("Values/{id:int}")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.Manage")]
    public async Task<IActionResult> DeleteValue(int id, CancellationToken ct)
    {
        // Server-side guard runs inside the command (LookupValueValidator.ValidateDeleteAsync):
        // a value referenced by any active rule OR any transaction is rejected with
        // 409 lookup_value_in_use before a request is enqueued (and re-checked at apply time).
        var requestId = await _mediator.Send(new DeleteLookupValueCommand(id), ct);
        return Ok(ResponseViewModel<object>.Ok(new { requestId, status = "pending_approval" }, "Submitted for approval"));
    }

    /// <summary>
    /// Template-driven bulk value import for a managed type. Validates every row,
    /// parks the VALID rows as ONE pending Lookup Modification Request, and returns
    /// a per-row summary + the requestId. Applied on approval (maker-checker).
    /// POST /api/v1/Lookup/Values/Bulk.
    /// </summary>
    [HttpPost("Values/Bulk")]
    [MapToApiVersion("1.0")]
    [RequiresPrivilege("LookupManagement.Manage")]
    public async Task<IActionResult> BulkValues([FromBody] BulkSubmitLookupValuesCommand cmd, CancellationToken ct)
    {
        var res = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<LookupBulkResult>.Ok(res, "Submitted for approval"));
    }

    /// <summary>Body for PATCH /Values/{id}/Active.</summary>
    public class SetActiveRequest
    {
        public bool isActive { get; set; }
    }
}

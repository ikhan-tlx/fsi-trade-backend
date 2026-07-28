using System.Security.Claims;
using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Application.Features.Requests.Common;
using FSI.Trade.Compliance.Application.Features.Requests.GetById;
using FSI.Trade.Compliance.Application.Features.Requests.GetPending;
using FSI.Trade.Compliance.Application.Features.Requests.List;
using FSI.Trade.Compliance.Application.Features.Requests.Submit;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Maker/checker request surface over <c>TmX_Request</c>.
///
///   GET  /api/v1/Request                                   (Requests.View)
///   GET  /api/v1/Request/GetPendingRequests/{type}/{id}    (Bearer)
///   GET  /api/v1/Request/{id}                              (Requests.View)
///   POST /api/v1/Request/Submit                            (maker — the entity's Create/Update privilege)
///   POST /api/v1/Request/SubmitRequest                     (checker — Requests.Approve)
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class RequestController : ControllerBase
{
    private readonly IMediator               _mediator;
    private readonly IApprovalReplayRegistry _registry;

    public RequestController(IMediator mediator, IApprovalReplayRegistry registry)
    {
        _mediator = mediator;
        _registry = registry;
    }

    /// <summary>
    /// Paged request list backing the FE Requests page grid.
    ///   GET /api/v1/Request?page=1&amp;pageSize=10&amp;sort=CreatedDate-desc
    /// </summary>
    [HttpGet]
    [RequiresPrivilege("Requests.View")]
    public async Task<IActionResult> List([FromQuery] ListRequestsQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<RequestListItemDto>>.Ok(paged));
    }

    /// <summary>Pending requests for an entity (request-type code + entity id).</summary>
    [HttpGet("GetPendingRequests/{requestType}/{entityId}")]
    public async Task<IActionResult> GetPendingRequests(string requestType, string entityId, CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetPendingRequestsQuery(requestType, entityId), ct);
        return Ok(ResponseViewModel<IReadOnlyList<PendingRequestDto>>.Ok(rows));
    }

    /// <summary>Single request for the detail/edit page (row + Entity_Json).</summary>
    [HttpGet("{id:long}")]
    [RequiresPrivilege("Requests.View")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetRequestByIdQuery(id), ct);
        return Ok(ResponseViewModel<RequestDetailDto>.Ok(dto));
    }

    /// <summary>
    /// Maker side: park a proposed change as a PENDING request instead of committing
    /// it. The maker must hold the underlying entity's create/update privilege
    /// (resolved per request type), enforced here rather than via a single
    /// [RequiresPrivilege] attribute because one endpoint serves all entity types.
    /// POST /api/v1/Request/Submit.
    /// </summary>
    [HttpPost("Submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitApprovalRequestCommand cmd, CancellationToken ct)
    {
        var required = _registry.RequiredSubmitPrivilege(cmd.RequestTypeCode, cmd.EntityStatusCode);
        if (!await CheckPrivilegeAsync(required, ct)) return ForbidPrivilege(required);

        var id = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { requestId = id, status = "PendingApproval" }, "Submitted for approval"));
    }

    /// <summary>
    /// Checker decision (approve/reject) on a request. Requires Requests.Approve;
    /// approving/rejecting your OWN request additionally needs Requests.ApproveOwn
    /// (admin), evaluated server-side and passed into the command.
    /// POST /api/v1/Request/SubmitRequest.
    /// </summary>
    [HttpPost("SubmitRequest")]
    [RequiresPrivilege("Requests.Approve")]
    public async Task<IActionResult> SubmitRequest([FromBody] SubmitRequestCommand cmd, CancellationToken ct)
    {
        // Server-trusted: never accept CanApproveOwn from the body.
        cmd.CanApproveOwn = await CheckPrivilegeAsync("Requests.ApproveOwn", ct);
        var id = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { requestId = id }, "Request submitted"));
    }

    // ----------------------------------------------------------------
    // Manual privilege check — same cache shape (HttpContext.Items["__privset"])
    // and bootstrap-admin escape hatch as [RequiresPrivilege] / UserController.
    // ----------------------------------------------------------------
    private async Task<bool> CheckPrivilegeAsync(string code, CancellationToken ct)
    {
        var http = HttpContext;

        // Bootstrap escape hatch — a bootstrap admin passes every check.
        var auth = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (auth.BootstrapAdminRoles is { Count: > 0 })
        {
            foreach (var br in auth.BootstrapAdminRoles)
                foreach (var r in http.User.FindAll(ClaimTypes.Role).Select(c => c.Value))
                    if (string.Equals(r, br, StringComparison.OrdinalIgnoreCase))
                        return true;
        }

        if (http.Items["__privset"] is not HashSet<string> privSet)
        {
            var svc = http.RequestServices.GetRequiredService<IPrivilegeService>();
            var roleNames = http.User.FindAll(ClaimTypes.Role)
                .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
            var raw = await svc.GetPrivilegesForRolesAsync(roleNames, ct);
            privSet = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);
            http.Items["__privset"] = privSet;
        }

        return privSet.Contains(code);
    }

    private IActionResult ForbidPrivilege(string code)
    {
        var envelope = new ResponseViewModel<object>
        {
            status = ResponseStatus.Error(403, "forbidden_privilege",
                                          $"This action requires the '{code}' privilege."),
            data   = new { Success = 0, Code = "forbidden_privilege", Required = code }
        };
        return new ObjectResult(envelope) { StatusCode = 403 };
    }
}

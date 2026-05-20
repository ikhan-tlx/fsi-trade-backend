using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Workflow;
using FSI.Trade.Compliance.Application.Features.Workflow.Execute;
using FSI.Trade.Compliance.Application.Features.Workflow.GetCommands;
using FSI.Trade.Compliance.Application.Features.Workflow.Inbox;
using FSI.Trade.Compliance.Application.Features.Workflow.ProductMapping;
using FSI.Trade.Compliance.Application.Features.Workflow.Schemes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Cross-cutting workflow surface. Slice 5 surface — replaces the legacy
/// LoanApplication / AccountApplication / Transaction workflow endpoint
/// triplet with a single domain-named controller:
///
///   GET    /api/v1/Workflow/Inbox                          (Bearer)               caller's inbox
///   GET    /api/v1/Workflow/Process/{id}/Commands          (Bearer)               available commands for caller
///   PUT    /api/v1/Workflow/Process/{id}/Execute           (Bearer)               advance the workflow
///   GET    /api/v1/Workflow/Schemes                        (Workflow.View)        registered schemes
///   GET    /api/v1/Workflow/ProductMapping                 (Workflow.View)        product↔scheme mapping read
///   POST   /api/v1/Workflow/ProductMapping                 (Workflow.Manage)      bulk-replace mapping
///   GET    /api/v1/Workflow/Designer                       (Workflow.Manage)      designer GET pass-through
///   POST   /api/v1/Workflow/Designer                       (Workflow.Manage)      designer POST pass-through
///
/// Vendor names (OptimaJet) live ONLY in the Infrastructure adapter
/// behind <c>IWorkflowEngine</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class WorkflowController : ControllerBase
{
    private readonly IMediator        _mediator;
    private readonly IWorkflowEngine  _engine;

    public WorkflowController(IMediator mediator, IWorkflowEngine engine)
    {
        _mediator = mediator;
        _engine   = engine;
    }

    // ---------- Inbox ----------

    [HttpGet("Inbox")]
    public async Task<IActionResult> Inbox([FromQuery] ListInboxQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<WorkflowInboxItem>>.Ok(paged));
    }

    // ---------- Per-process: commands + execute ----------

    [HttpGet("Process/{processId:guid}/Commands")]
    public async Task<IActionResult> GetCommands(Guid processId, CancellationToken ct)
    {
        var cmds = await _mediator.Send(new GetWorkflowCommandsQuery(processId), ct);
        return Ok(ResponseViewModel<IReadOnlyList<WorkflowCommand>>.Ok(cmds));
    }

    [HttpPut("Process/{processId:guid}/Execute")]
    public async Task<IActionResult> Execute(Guid processId, [FromBody] ExecuteWorkflowCommand body, CancellationToken ct)
    {
        body.processId = processId;
        var result = await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<WorkflowExecutionResult>.Ok(result));
    }

    // ---------- Admin: schemes + product mapping ----------

    [HttpGet("Schemes")]
    [RequiresPrivilege("Workflow.View")]
    public async Task<IActionResult> Schemes(CancellationToken ct)
    {
        var schemes = await _mediator.Send(new ListWorkflowSchemesQuery(), ct);
        return Ok(ResponseViewModel<IReadOnlyList<WorkflowScheme>>.Ok(schemes));
    }

    [HttpGet("ProductMapping")]
    [RequiresPrivilege("Workflow.View")]
    public async Task<IActionResult> GetProductMapping([FromQuery] string schemeCode, CancellationToken ct)
    {
        var ids = await _mediator.Send(new GetProductMappingQuery(schemeCode), ct);
        return Ok(ResponseViewModel<IReadOnlyList<int>>.Ok(ids));
    }

    [HttpPost("ProductMapping")]
    [RequiresPrivilege("Workflow.Manage")]
    public async Task<IActionResult> SaveProductMapping([FromBody] UpdateProductMappingCommand body, CancellationToken ct)
    {
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { schemeCode = body.schemeCode, productCount = body.productIds?.Count ?? 0 }));
    }

    // ---------- Designer (admin pass-through) ----------

    [HttpGet("Designer")]
    [HttpPost("Designer")]
    [RequiresPrivilege("Workflow.Manage")]
    public async Task<IActionResult> Designer(CancellationToken ct)
    {
        // Collect all GET query-string + POST form params into a single
        // dict for the engine's designer protocol.
        var formParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in Request.Query)
            formParams[kv.Key] = kv.Value.ToString();

        if (Request.HasFormContentType)
        {
            foreach (var kv in Request.Form)
                formParams[kv.Key] = kv.Value.ToString();
        }

        var stream = Request.HasFormContentType ? null : Request.Body;
        var resp   = await _engine.InvokeDesignerAsync(formParams, stream, ct);

        // Designer responses are vendor-formatted JSON the FE designer
        // module consumes directly. Bypass our envelope; return raw.
        return new FileContentResult(resp.Body, resp.ContentType);
    }
}

using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.FieldSetup.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Slice 12 §A — admin surface for editing rows in
/// <c>TmX_Tenant_Field_Setup</c>. Drives the Field Configuration page
/// in the admin UI (BE_revamp_Changes.md §10.2).
///
/// Endpoint inventory:
///   PUT /api/v1/FieldSetup/{tenantFieldSetupId}   — edit one field's
///       label / placeholder / visible / required toggle
///
/// <para>
/// Reads are not duplicated here — the admin page hydrates the grid via
/// the existing per-product endpoints
/// (<c>GET /Product/{id}/Tabs</c> + <c>GET /Product/{id}/Tabs/{tabId}/Fields</c>
/// or <c>GET /Product/{id}/FormDefinition</c>), so the FE has the same
/// shape on the admin page as on the form-render path.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class FieldSetupController : ControllerBase
{
    private readonly IMediator _mediator;
    public FieldSetupController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Mutates one field. Body keys are nullable — null means "leave
    /// alone". Accepts any combination of <c>fieldLabel</c>,
    /// <c>placeholder</c>, <c>isActive</c>, <c>isMandatory</c>; at least
    /// one must be present.
    ///
    /// <para>
    /// 409 <c>field_has_conditional_rule</c> when <c>isMandatory</c> is
    /// supplied but the existing column holds a formula. Admin UI should
    /// surface the message verbatim and disable the toggle for that row.
    /// </para>
    /// </summary>
    [HttpPut("{tenantFieldSetupId:int}")]
    [RequiresPrivilege("FieldSetup.Manage")]
    public async Task<IActionResult> Update(
        int                        tenantFieldSetupId,
        [FromBody] UpdateFieldSetupCommand body,
        CancellationToken          ct)
    {
        body.tenantFieldSetupId = tenantFieldSetupId;
        var result = await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<UpdateFieldSetupResult>.Ok(result));
    }
}

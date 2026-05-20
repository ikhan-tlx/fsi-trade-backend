using Asp.Versioning;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Features.Configurations.GetForUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// Tenant-scoped feature flags / business limits / endpoint URLs the FE reads
/// at app-init. Slice 3 surface:
///
///   GET /api/v1/Configurations/GetUserCompanyConfigurations  (Bearer; no privilege gate)
///
/// URL kept verbose to match what the FE already calls. The endpoint is
/// effectively <c>GET /Configurations</c> — a tenant-scoped read for the
/// caller — but renaming would force a FE edit with zero benefit. No
/// <c>[RequiresPrivilege]</c>: every authenticated user needs configs to
/// render the app.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ConfigurationsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ConfigurationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("GetUserCompanyConfigurations")]
    public async Task<IActionResult> GetUserCompanyConfigurations(CancellationToken ct)
    {
        var rows = await _mediator.Send(new GetUserCompanyConfigurationsQuery(), ct);
        return Ok(ResponseViewModel<List<ConfigurationItemDto>>.Ok(rows));
    }
}

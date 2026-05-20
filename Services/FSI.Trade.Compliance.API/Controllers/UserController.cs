using Asp.Versioning;
using FSI.Trade.Compliance.API.Filters;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Features.Users.Create;
using FSI.Trade.Compliance.Application.Features.Users.Get;
using FSI.Trade.Compliance.Application.Features.Users.List;
using FSI.Trade.Compliance.Application.Features.Users.SetActive;
using FSI.Trade.Compliance.Application.Features.Users.Unlock;
using FSI.Trade.Compliance.Application.Features.Users.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSI.Trade.Compliance.API.Controllers;

/// <summary>
/// User CRUD + lifecycle. Slice 2 Step 4 surface:
///
///   GET    /api/v1/User                       (Bearer + Users.View)
///   GET    /api/v1/User/{id}                  (Bearer; self-bypass — see notes)
///   POST   /api/v1/User                       (Bearer + Users.Create)
///   PUT    /api/v1/User/{id}                  (Bearer + Users.Update)
///   POST   /api/v1/User/{id}/Activate         (Bearer + Users.Activate)
///   POST   /api/v1/User/{id}/Deactivate       (Bearer + Users.Activate)
///   POST   /api/v1/User/{id}/UnlockUser       (Bearer + Users.UnlockUser)
///
/// Self-bypass on GET /User/{id}: a caller may always read their own profile
/// without holding the Users.View privilege — drives the FE MyProfilePage
/// without admin grants. Any other id triggers the privilege check.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IMediator           _mediator;
    private readonly ICurrentUserService _current;

    public UserController(IMediator mediator, ICurrentUserService current)
    {
        _mediator = mediator;
        _current  = current;
    }

    // ---------- List ----------
    [HttpGet]
    [RequiresPrivilege("Users.View")]
    public async Task<IActionResult> List([FromQuery] ListUsersQuery query, CancellationToken ct)
    {
        var paged = await _mediator.Send(query, ct);
        return Ok(ResponseViewModel<PagedResult<UserListItemDto>>.Ok(paged));
    }

    // ---------- Get one (self-bypass on caller's own ID) ----------
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var isSelf = string.Equals(_current.UserId, id, StringComparison.OrdinalIgnoreCase);
        if (!isSelf)
        {
            // Run the privilege check manually since the attribute is conditional.
            // Reusing IPrivilegeService keeps the cache shape consistent with the
            // attribute path (HttpContext.Items["__privset"]).
            var allowed = await CheckPrivilegeAsync("Users.View", ct);
            if (!allowed) return ForbidPrivilege("Users.View");
        }

        var dto = await _mediator.Send(new GetUserQuery(id), ct);
        return Ok(ResponseViewModel<UserDetailDto>.Ok(dto));
    }

    // ---------- Create ----------
    [HttpPost]
    [RequiresPrivilege("Users.Create")]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand cmd, CancellationToken ct)
    {
        var newId = await _mediator.Send(cmd, ct);
        return Ok(ResponseViewModel<object>.Ok(new { userId = newId }));
    }

    // ---------- Update ----------
    [HttpPut("{id}")]
    [RequiresPrivilege("Users.Update")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserCommand body, CancellationToken ct)
    {
        body.userId = id;
        await _mediator.Send(body, ct);
        return Ok(ResponseViewModel<object>.Ok(new { userId = id }));
    }

    // ---------- Activate / Deactivate ----------
    [HttpPost("{id}/Activate")]
    [RequiresPrivilege("Users.Activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken ct)
    {
        await _mediator.Send(new SetUserActiveCommand(id, true), ct);
        return Ok(ResponseViewModel<object>.Ok(new { userId = id, isActive = true }));
    }

    [HttpPost("{id}/Deactivate")]
    [RequiresPrivilege("Users.Activate")]
    public async Task<IActionResult> Deactivate(string id, CancellationToken ct)
    {
        await _mediator.Send(new SetUserActiveCommand(id, false), ct);
        return Ok(ResponseViewModel<object>.Ok(new { userId = id, isActive = false }));
    }

    // ---------- Unlock ----------
    [HttpPost("{id}/UnlockUser")]
    [RequiresPrivilege("Users.UnlockUser")]
    public async Task<IActionResult> UnlockUser(string id, CancellationToken ct)
    {
        await _mediator.Send(new UnlockUserCommand(id), ct);
        return Ok(ResponseViewModel<object>.Ok(new { userId = id, isLockedOut = false }));
    }

    // ----------------------------------------------------------------
    // Helpers — manual privilege check used by Get(self-bypass).
    // Intentionally inline rather than refactoring the attribute, so
    // [RequiresPrivilege("...")] stays declarative for the common case
    // and only this one action carries the conditional logic.
    // ----------------------------------------------------------------

    private async Task<bool> CheckPrivilegeAsync(string code, CancellationToken ct)
    {
        var http = HttpContext;
        if (http.Items["__privset"] is not HashSet<string> privSet)
        {
            var svc = http.RequestServices.GetRequiredService<Application.Contracts.Persistence.IPrivilegeService>();
            var roleNames = http.User
                .FindAll(System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            var raw = await svc.GetPrivilegesForRolesAsync(roleNames, ct);
            privSet = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);
            http.Items["__privset"] = privSet;
        }

        // Bootstrap escape hatch — same shape as in the attribute.
        var auth = http.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Common.Options.AuthOptions>>()
            .Value;
        if (auth.BootstrapAdminRoles is { Count: > 0 })
        {
            foreach (var br in auth.BootstrapAdminRoles)
                foreach (var r in http.User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value))
                    if (string.Equals(r, br, StringComparison.OrdinalIgnoreCase))
                        return true;
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

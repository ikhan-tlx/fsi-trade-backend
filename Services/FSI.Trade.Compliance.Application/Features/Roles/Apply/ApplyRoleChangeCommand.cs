using FSI.Trade.Compliance.Application.Features.Roles.Create;
using FSI.Trade.Compliance.Application.Features.Roles.Privileges;
using FSI.Trade.Compliance.Application.Features.Roles.SetActive;
using FSI.Trade.Compliance.Application.Features.Roles.Update;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.Apply;

/// <summary>
/// Maker/checker apply for a Role Management Request. Bundles the role record
/// (create or update) AND its privilege grants into ONE replayable command so a
/// role's name/description and its privileges are approved together (a checker
/// must never be able to approve the name while the privilege change slips
/// through un-reviewed). Reuses the existing Create / Update / UpdateRolePrivileges
/// handlers via MediatR — no write logic duplicated.
/// </summary>
public class ApplyRoleChangeCommand : IRequest<int>
{
    public int?       roleId          { get; set; }   // null / 0 = create
    public string     roleName        { get; set; } = "";
    public string?    roleDescription { get; set; }
    public bool       isActive        { get; set; } = true;
    public List<int>? privilegeIds    { get; set; }   // null = don't touch privileges
}

public class ApplyRoleChangeCommandHandler : IRequestHandler<ApplyRoleChangeCommand, int>
{
    private readonly IMediator _mediator;
    public ApplyRoleChangeCommandHandler(IMediator mediator) => _mediator = mediator;

    public async Task<int> Handle(ApplyRoleChangeCommand req, CancellationToken ct)
    {
        int id;
        if (req.roleId is { } rid && rid > 0)
        {
            await _mediator.Send(new UpdateRoleCommand
            {
                roleId          = rid,
                roleName        = req.roleName,
                roleDescription = req.roleDescription,
            }, ct);
            // Apply the active/inactive state too (idempotent no-op if unchanged) —
            // the inline Role page toggles this as part of the same save.
            await _mediator.Send(new SetRoleActiveCommand(rid, req.isActive), ct);
            id = rid;
        }
        else
        {
            id = await _mediator.Send(new CreateRoleCommand
            {
                roleName        = req.roleName,
                roleDescription = req.roleDescription,
                isActive        = req.isActive,
            }, ct);
        }

        if (req.privilegeIds is not null)
            await _mediator.Send(new UpdateRolePrivilegesCommand { roleId = id, privilegeIds = req.privilegeIds }, ct);

        return id;
    }
}

using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.Privileges;

/// <summary>
/// Bulk-replace the role's privilege grants. Server diffs current vs requested:
/// new IDs become INSERTs, removed IDs become DELETEs, unchanged IDs are no-ops.
/// </summary>
public class UpdateRolePrivilegesCommand : IRequest<Unit>
{
    public int        roleId       { get; set; }
    public List<int>  privilegeIds { get; set; } = new();
}

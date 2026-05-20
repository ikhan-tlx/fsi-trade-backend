using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.Privileges;

public record GetRolePrivilegesQuery(int RoleId) : IRequest<List<RolePrivilegeDto>>;

public class RolePrivilegeDto
{
    public int     privilegeId  { get; set; }
    public string  code         { get; set; } = "";
    public string? description  { get; set; }
}

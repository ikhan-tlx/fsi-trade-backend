using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.Update;

public class UpdateRoleCommand : IRequest<Unit>
{
    public int     roleId          { get; set; }
    public string  roleName        { get; set; } = "";
    public string? roleDescription { get; set; }
}

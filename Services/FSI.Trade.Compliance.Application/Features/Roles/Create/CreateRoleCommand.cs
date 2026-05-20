using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.Create;

public class CreateRoleCommand : IRequest<int>
{
    public string  roleName        { get; set; } = "";
    public string? roleDescription { get; set; }
    public bool    isActive        { get; set; } = true;
}

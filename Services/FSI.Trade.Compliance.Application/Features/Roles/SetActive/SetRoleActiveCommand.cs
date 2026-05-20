using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.SetActive;

public record SetRoleActiveCommand(int RoleId, bool Active) : IRequest<Unit>;

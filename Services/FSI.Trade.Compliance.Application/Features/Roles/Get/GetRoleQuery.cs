using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Roles.Get;

public record GetRoleQuery(int RoleId) : IRequest<RoleDetailDto>;

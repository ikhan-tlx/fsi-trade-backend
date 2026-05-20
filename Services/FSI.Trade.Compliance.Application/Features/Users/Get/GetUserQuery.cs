using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.Get;

public record GetUserQuery(string UserId) : IRequest<UserDetailDto>;

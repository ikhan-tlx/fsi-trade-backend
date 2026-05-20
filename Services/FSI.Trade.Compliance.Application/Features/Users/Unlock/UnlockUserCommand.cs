using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.Unlock;

public record UnlockUserCommand(string UserId) : IRequest<Unit>;

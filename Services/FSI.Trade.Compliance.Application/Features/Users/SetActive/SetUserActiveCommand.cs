using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.SetActive;

public record SetUserActiveCommand(string UserId, bool Active) : IRequest<Unit>;

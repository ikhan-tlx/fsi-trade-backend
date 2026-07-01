using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.Lock;

/// <summary>Admin-initiated (indefinite) lock. Distinct from the auto-lockout
/// that failed logins trigger — this one stays until an admin unlocks it.</summary>
public record LockUserCommand(string UserId) : IRequest<Unit>;

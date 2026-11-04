using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.ResetTwoFactor;

/// <summary>Admin action: clear a user's 2FA enrolment (e.g. lost device) so they
/// must re-enrol at next login. Mirrors the Unlock primitive.</summary>
public record ResetUserTwoFactorCommand(string UserId) : IRequest<Unit>;

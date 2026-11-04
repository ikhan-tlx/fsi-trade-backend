using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using MediatR;

namespace FSI.Trade.Compliance.Application.Features.Users.ResetTwoFactor;

public class ResetUserTwoFactorCommandHandler : IRequestHandler<ResetUserTwoFactorCommand, Unit>
{
    private readonly IUserAuthenticationService _userAuth;
    private readonly ICurrentUserService        _current;

    public ResetUserTwoFactorCommandHandler(IUserAuthenticationService userAuth, ICurrentUserService current)
    {
        _userAuth = userAuth;
        _current  = current;
    }

    public async Task<Unit> Handle(ResetUserTwoFactorCommand req, CancellationToken ct)
    {
        var user = await _userAuth.FindByIdAsync(req.UserId, ct)
                   ?? throw new NotFoundException("user_not_found", $"User '{req.UserId}' not found.");

        var actor = _current.UserName ?? _current.UserId ?? "unknown";

        // Admin reset (lost device etc.): clear the TOTP enrolment so the user must
        // re-enrol at next login (forced by the login enforcer while 2FA is enforced).
        // The LastUpdatedBy/Date stamp lands the actor+time on the row; the TmX_User
        // UPDATE is recorded automatically by AuditSaveChangesInterceptor (who / what /
        // when + hash chain), so this reset shows in the Audit Trail like Unlock.
        user.TwoFactorEnabled          = false;
        user.TwoFactorAuthenticatorKey = null;
        user.LastUpdatedBy             = actor;
        user.LastUpdatedDate           = DateTime.UtcNow;
        await _userAuth.UpdateUserAsync(user, ct);

        return Unit.Value;
    }
}

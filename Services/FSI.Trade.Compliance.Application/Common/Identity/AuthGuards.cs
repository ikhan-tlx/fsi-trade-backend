using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;
using FSI.Trade.Compliance.Domain.Enums;

namespace FSI.Trade.Compliance.Application.Common.Identity;

/// <summary>
/// Pure auth-policy gate checks, expressed in Application-layer terms. Handlers
/// compose these in whatever order their use case requires. Each method either
/// returns silently (gate passed) or throws an <see cref="AuthenticationException"/>
/// with a stable error code the FE branches on.
/// </summary>
internal static class AuthGuards
{
    public static void EnsureUserActive(ApplicationUser user)
    {
        if (!string.Equals(user.Status, UserStatus.Active, StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException("user_inactive", "User account is not active.");
        if (!user.ActiveFlag)
            throw new AuthenticationException("user_inactive", "User account has been disabled.");
    }

    public static void EnsureNotFirstPasswordChange(ApplicationUser user)
    {
        if (user.FirstPasswordChange)
            throw new AuthenticationException("first_password_change_required", "Password must be changed on first login.");
    }

    public static void EnsureNotDormant(ApplicationUser user, int dormancyDays)
    {
        if (user.LastLoginDate.HasValue
            && user.LastLoginDate.Value < DateTime.UtcNow.AddDays(-dormancyDays))
            throw new AuthenticationException("account_dormant", "Account has been inactive for too long. Contact admin.");
    }

    public static void EnsurePasswordNotExpired(
        ApplicationUser user,
        IReadOnlyList<string> userRoles,
        IReadOnlyList<string> rolesWithoutExpiry)
    {
        if (user.PasswordExpiryDate.HasValue
            && user.PasswordExpiryDate.Value < DateTime.UtcNow
            && !userRoles.Any(r => rolesWithoutExpiry.Contains(r, StringComparer.OrdinalIgnoreCase)))
            throw new AuthenticationException("password_expired", "Password has expired. Please change it.");
    }

    public static void VerifyTwoFactor(ApplicationUser user, string? otp, ITwoFactorVerifier verifier)
    {
        if (!user.TwoFactorEnabled) return;

        if (string.IsNullOrWhiteSpace(otp))
            throw new AuthenticationException("otp_required", "One-time password is required.");
        if (string.IsNullOrEmpty(user.TwoFactorAuthenticatorKey))
            throw new AuthenticationException("otp_not_configured", "User has 2FA enabled but no authenticator key.");

        if (!verifier.Verify(user.TwoFactorAuthenticatorKey, otp))
            throw new AuthenticationException("invalid_otp", "One-time password is invalid.");
    }
}

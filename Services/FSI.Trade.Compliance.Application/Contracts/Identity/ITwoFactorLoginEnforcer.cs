using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// The login-time second-factor gate. Given the authenticated user and the OTP the
/// caller supplied (may be null on the first round-trip), it either:
///   * returns a CHALLENGE <see cref="AuthResponse"/> (Success=0, with a Code and,
///     for TOTP setup, a ProvisioningUri) that the login handler returns verbatim —
///     no access token is issued, so the app stays locked; or
///   * returns <c>null</c> meaning the factor is satisfied and login may proceed; or
///   * throws <c>AuthenticationException</c> for a hard verify failure (invalid/expired).
/// Method is chosen system-wide via config (<c>TwoFactor.Method</c> = Totp | Email).
/// </summary>
public interface ITwoFactorLoginEnforcer
{
    Task<AuthResponse?> EnforceAsync(ApplicationUser user, string? otp, CancellationToken ct = default);
}

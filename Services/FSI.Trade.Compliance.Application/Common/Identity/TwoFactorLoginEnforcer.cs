using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Models;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Application.Common.Identity;

/// <summary>
/// Enforces the configured second factor at login. See <see cref="ITwoFactorLoginEnforcer"/>.
///
/// TOTP (forced setup): a user who hasn't enrolled is issued a pending secret and
/// challenged with <c>totp_setup_required</c> + the provisioning URI; the QR is shown
/// on the login screen and the user must scan + enter a valid code, which flips
/// TwoFactorEnabled=true and lets login continue. An enrolled user is challenged with
/// <c>otp_required</c> and must supply a current TOTP code. No token is issued until a
/// valid code is verified — so the app is inaccessible until setup + verification are done.
///
/// Email: the first round-trip generates + emails an OTP (stored hashed, with expiry)
/// and challenges with <c>otp_required</c> (+ masked address); the next round-trip
/// verifies the submitted code against the stored record.
/// </summary>
public sealed class TwoFactorLoginEnforcer : ITwoFactorLoginEnforcer
{
    private readonly IOptionsMonitor<TwoFactorOptions> _opt;
    private readonly ITwoFactorVerifier         _totp;
    private readonly ITwoFactorSecretGenerator  _secretGen;
    private readonly IEmailOtpService           _emailOtp;
    private readonly IUserAuthenticationService _userAuth;
    private readonly ILogger<TwoFactorLoginEnforcer> _logger;

    public TwoFactorLoginEnforcer(
        IOptionsMonitor<TwoFactorOptions> opt,
        ITwoFactorVerifier         totp,
        ITwoFactorSecretGenerator  secretGen,
        IEmailOtpService           emailOtp,
        IUserAuthenticationService userAuth,
        ILogger<TwoFactorLoginEnforcer> logger)
    {
        _opt = opt; _totp = totp; _secretGen = secretGen;
        _emailOtp = emailOtp; _userAuth = userAuth; _logger = logger;
    }

    public async Task<AuthResponse?> EnforceAsync(ApplicationUser user, string? otp, CancellationToken ct = default)
    {
        var opt    = _opt.CurrentValue;
        if (!opt.Enabled) return null;
        var method = (opt.Method ?? "None").Trim();

        if (method.Equals("Email", StringComparison.OrdinalIgnoreCase))
            return await EnforceEmailAsync(user, otp, ct);

        if (method.Equals("Totp", StringComparison.OrdinalIgnoreCase))
            return await EnforceTotpAsync(user, otp, opt, ct);

        if (method.Equals("None", StringComparison.OrdinalIgnoreCase))
            return null;

        // Unknown/unsupported method (e.g. "SMS"): don't lock everyone out on a
        // misconfiguration — log and skip the factor.
        _logger.LogWarning("TwoFactor.Method '{Method}' is not supported; skipping the 2FA gate.", method);
        return null;
    }

    // ---- TOTP (forced setup + verify) ------------------------------------
    private async Task<AuthResponse?> EnforceTotpAsync(ApplicationUser user, string? otp, TwoFactorOptions opt, CancellationToken ct)
    {
        var enrolled = user.TwoFactorEnabled && !string.IsNullOrEmpty(user.TwoFactorAuthenticatorKey);

        if (!enrolled)
        {
            if (string.IsNullOrWhiteSpace(otp))
            {
                // Issue (once) a pending secret; enrolment is confirmed on the next round-trip.
                if (string.IsNullOrEmpty(user.TwoFactorAuthenticatorKey))
                {
                    user.TwoFactorAuthenticatorKey = _secretGen.GenerateSecret();
                    user.TwoFactorEnabled          = false;
                    await _userAuth.UpdateUserAsync(user, ct);
                }
                var uri = _secretGen.BuildProvisioningUri(
                    user.TwoFactorAuthenticatorKey!, user.UserName ?? user.Id, opt.Issuer);
                return Challenge("totp_setup_required", "Totp",
                    message: "Set up two-factor authentication: scan the QR code with your authenticator app, then enter the 6-digit code.",
                    provisioningUri: uri, secret: user.TwoFactorAuthenticatorKey);
            }

            // Confirming setup — verify the code against the pending secret, then enrol.
            if (!_totp.Verify(user.TwoFactorAuthenticatorKey!, otp))
                throw new AuthenticationException("invalid_otp", "The verification code is invalid. Please try again.");
            user.TwoFactorEnabled = true;
            await _userAuth.UpdateUserAsync(user, ct);
            return null; // enrolled + verified → proceed to token
        }

        // Already enrolled — verify.
        if (string.IsNullOrWhiteSpace(otp))
            return Challenge("otp_required", "Totp", message: "Enter the code from your authenticator app.");
        if (!_totp.Verify(user.TwoFactorAuthenticatorKey!, otp))
            throw new AuthenticationException("invalid_otp", "The verification code is invalid. Please try again.");
        return null;
    }

    // ---- Email OTP -------------------------------------------------------
    private async Task<AuthResponse?> EnforceEmailAsync(ApplicationUser user, string? otp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(otp))
        {
            var masked = await _emailOtp.SendAsync(user, ct); // throws if unconfigured / send fails
            return Challenge("otp_required", "Email",
                message: $"We emailed a verification code to {masked}. Enter it to continue.",
                maskedEmail: masked);
        }

        var result = await _emailOtp.VerifyAsync(user, otp, ct);
        return result switch
        {
            EmailOtpResult.Success         => null,
            EmailOtpResult.Expired         => throw new AuthenticationException("otp_expired", "The code has expired. Request a new one."),
            EmailOtpResult.TooManyAttempts => throw new AuthenticationException("otp_locked", "Too many incorrect attempts. Request a new code."),
            EmailOtpResult.Missing         => throw new AuthenticationException("otp_required", "No active code found. Request a new one."),
            _                              => throw new AuthenticationException("invalid_otp", "The verification code is invalid. Please try again."),
        };
    }

    private static AuthResponse Challenge(string code, string method, string message,
        string? provisioningUri = null, string? secret = null, string? maskedEmail = null)
        => new()
        {
            Success         = 0,
            Code            = code,
            Method          = method,
            Message         = message,
            ProvisioningUri = provisioningUri,
            Secret          = secret,
            MaskedEmail     = maskedEmail,
        };
}

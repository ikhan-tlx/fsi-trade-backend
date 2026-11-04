using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>Outcome of verifying a submitted Email OTP against the stored code.</summary>
public enum EmailOtpResult
{
    Success,
    Missing,          // no OTP on record (e.g. never sent / already consumed)
    Expired,
    Invalid,          // wrong code (attempt counted)
    TooManyAttempts   // attempt ceiling reached — a new code must be sent
}

/// <summary>
/// Email 2FA one-time passwords. Generates an N-digit code, stores only its
/// SHA-256 hash with an expiry + attempt counter (one current row per user), and
/// delivers it over the configured SMTP source. Verification is server-side.
/// </summary>
public interface IEmailOtpService
{
    /// <summary>Generate a fresh code, upsert the hashed record (resetting attempts +
    /// expiry), and email it to the user. Returns the masked destination address
    /// (e.g. <c>j***@bank.com</c>) for display. Throws <c>AuthenticationException</c>
    /// with code <c>email_otp_unavailable</c> when SMTP isn't configured, or
    /// <c>email_otp_send_failed</c> when delivery fails.</summary>
    Task<string> SendAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>Validate a submitted code against the current stored OTP (hash +
    /// expiry + attempt ceiling). On success the record is marked consumed.</summary>
    Task<EmailOtpResult> VerifyAsync(ApplicationUser user, string code, CancellationToken ct = default);
}

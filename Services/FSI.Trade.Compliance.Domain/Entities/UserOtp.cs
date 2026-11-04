namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// The current server-side one-time password for Email 2FA. Maps 1:1 to
/// dbo.TmX_User_Otp. Exactly one active row per (UserId, Purpose) — sending a
/// fresh OTP upserts this row. The plaintext code is NEVER stored; only its
/// SHA-256 hash, so verification compares hashes.
/// </summary>
public class UserOtp
{
    public long OtpId { get; set; }
    public string UserId { get; set; } = default!;
    public string Purpose { get; set; } = "Login";
    /// <summary>SHA-256 (hex) of the delivered code.</summary>
    public string OtpHash { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime SentAt { get; set; }
    /// <summary>Set when the code is successfully verified (spent).</summary>
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// Verifies a TOTP one-time password. Application doesn't know which library
/// (OtpNet today, possibly a hardware-key-based verifier later) — it just
/// asks "is this OTP valid for this Base32 secret?".
/// </summary>
public interface ITwoFactorVerifier
{
    bool Verify(string secretBase32, string otp);
}

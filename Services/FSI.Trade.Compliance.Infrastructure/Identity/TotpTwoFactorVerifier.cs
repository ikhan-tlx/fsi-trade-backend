using FSI.Trade.Compliance.Application.Contracts.Identity;
using OtpNet;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

public class TotpTwoFactorVerifier : ITwoFactorVerifier
{
    public bool Verify(string secretBase32, string otp)
    {
        if (string.IsNullOrWhiteSpace(secretBase32) || string.IsNullOrWhiteSpace(otp))
            return false;

        byte[] secret;
        try { secret = Base32Encoding.ToBytes(secretBase32); }
        catch { return false; }

        var totp = new Totp(secret);
        return totp.VerifyTotp(otp, out _, new VerificationWindow(2, 2));
    }
}

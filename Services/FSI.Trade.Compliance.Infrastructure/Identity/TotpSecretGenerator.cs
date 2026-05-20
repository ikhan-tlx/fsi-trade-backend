using System.Security.Cryptography;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using OtpNet;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

public class TotpSecretGenerator : ITwoFactorSecretGenerator
{
    public string GenerateSecret()
    {
        // 160 random bits — RFC 4226 recommended length for HOTP/TOTP.
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encoding.ToString(bytes.ToArray());
    }

    public string BuildProvisioningUri(string secretBase32, string accountName, string issuer)
    {
        var encIssuer  = Uri.EscapeDataString(issuer);
        var encAccount = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{encIssuer}:{encAccount}" +
               $"?secret={secretBase32}&issuer={encIssuer}&digits=6&period=30&algorithm=SHA1";
    }
}

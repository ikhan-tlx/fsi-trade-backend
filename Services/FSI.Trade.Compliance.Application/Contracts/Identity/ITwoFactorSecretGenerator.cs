namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// Issues a fresh, cryptographically random TOTP secret and the standard
/// otpauth:// provisioning URI an authenticator app can ingest as a QR code.
/// </summary>
public interface ITwoFactorSecretGenerator
{
    /// <returns>Base32-encoded shared secret (typically 160 bits per RFC 4226).</returns>
    string GenerateSecret();

    /// <summary>
    /// Builds the otpauth://totp/... URI the FE renders as a QR code.
    /// Format follows the Google Authenticator key URI spec.
    /// </summary>
    string BuildProvisioningUri(string secretBase32, string accountName, string issuer);
}

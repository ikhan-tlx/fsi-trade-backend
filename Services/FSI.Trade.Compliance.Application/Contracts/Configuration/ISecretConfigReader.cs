namespace FSI.Trade.Compliance.Application.Contracts.Configuration;

/// <summary>
/// Reads a sensitive Configuration value (Is_Sensitive = 1) straight from the
/// store and returns it DECRYPTED for point-of-use. Sensitive values are NOT
/// published through the DB→IConfiguration bridge, so consumers that need a
/// secret (e.g. FCCM Basic-auth password) resolve it through this reader instead
/// of <c>IConfiguration</c>/<c>IOptions</c>. The plaintext is never logged.
/// </summary>
public interface ISecretConfigReader
{
    /// <summary>Decrypted value for a dotted configuration key
    /// (e.g. <c>"Integration.Onboarding.Password"</c>), or <c>null</c> when the
    /// key/value is absent, empty, or cannot be decrypted.</summary>
    string? Get(string configurationKey);

    /// <summary>True when a non-empty value is stored for the key — an
    /// "is it configured?" check that never returns the value itself.</summary>
    bool HasValue(string configurationKey);
}

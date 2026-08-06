namespace FSI.Trade.Compliance.Application.Contracts.Configuration;

/// <summary>
/// Encrypts / decrypts Configuration secret values at rest. Secret-type settings
/// (Is_Sensitive = 1) are stored ENCRYPTED in the DB and only ever decrypted at
/// point of use — never bridged into IConfiguration, never returned to the FE.
/// Implemented with ASP.NET Core Data Protection (DPAPI-protected key ring).
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypt a plaintext secret for storage. Returns a self-describing,
    /// prefixed token (e.g. <c>enc:v1:…</c>) so protected values are detectable.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypt a stored value. If the input is not a protected token it is
    /// returned unchanged (back-compat for a legacy plaintext seed). Returns
    /// <c>null</c> for null input or when decryption fails (wrong key / tampered) —
    /// the value is never leaked on failure.</summary>
    string? Unprotect(string? stored);

    /// <summary>True when the stored value is an encrypted token this protector produced.</summary>
    bool IsProtected(string? stored);
}

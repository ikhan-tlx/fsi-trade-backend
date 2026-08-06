using FSI.Trade.Compliance.Application.Contracts.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

/// <summary>
/// <see cref="ISecretProtector"/> backed by ASP.NET Core Data Protection. The key
/// ring is persisted and (on Windows) DPAPI-encrypted, so ciphertext survives
/// restarts but only this application (same key ring) can decrypt it. Protected
/// values carry an <c>enc:v1:</c> prefix so a legacy plaintext seed is detectable
/// and passed through unchanged until it is next saved (and thereby encrypted).
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:v1:";
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("FSI.Trade.Compliance.Configuration.Secrets.v1");

    public string Protect(string plaintext)
        => Prefix + _protector.Protect(plaintext ?? string.Empty);

    public bool IsProtected(string? stored)
        => stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    public string? Unprotect(string? stored)
    {
        if (stored is null) return null;
        if (!IsProtected(stored)) return stored; // legacy plaintext / not yet encrypted
        try
        {
            return _protector.Unprotect(stored.Substring(Prefix.Length));
        }
        catch
        {
            // Wrong key ring or tampered ciphertext — never leak; treat as missing.
            return null;
        }
    }
}

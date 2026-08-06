namespace FSI.Trade.Compliance.Application.Contracts.Configuration;

/// <summary>
/// Resolves the configured third-party KYC provider title + the
/// external-verification flag from Configuration Management (Onboarding.*),
/// so screens, labels and logs can display any provider name instead of a
/// hardcoded vendor. Reads the DB-bridged IConfiguration (hot).
/// </summary>
public interface IProviderTitleProvider
{
    /// <summary>Display name for the external verification provider (e.g. "BRAINS").</summary>
    string Title { get; }

    /// <summary>Whether external onboarding verification is required for new KYC.</summary>
    bool RequireExternalVerification { get; }

    /// <summary>How a failed/unverifiable result is handled at KYC create:
    /// "Block" | "Allow with flag" | "Allow with warning" (default "Block").</summary>
    string FailureHandling { get; }
}

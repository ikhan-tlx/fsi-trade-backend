namespace FSI.Trade.Compliance.Application.Contracts.Kyc;

/// <summary>
/// Resolves the review cadence for a risk band. The default implementation
/// reads <c>PeriodicReviewOptions.Frequencies</c>; a future DB-backed policy
/// can replace it behind this seam with no caller changes.
/// </summary>
public interface IReviewPolicyProvider
{
    /// <summary>Frequency for the given band label (falls back to the default cadence).</summary>
    ReviewFrequencyResult GetFrequency(string? riskBand);
}

/// <summary>A resolved cadence: <c>Value</c> units of <c>Unit</c> (Days/Weeks/Months/Years).</summary>
public record ReviewFrequencyResult(int Value, string Unit);

namespace FSI.Trade.Compliance.Application.Contracts.Kyc;

/// <summary>
/// Computes a customer's next review date from their risk band and the KYC
/// completion date, using the configured per-band frequency. Pure/stateless.
/// </summary>
public interface INextReviewDateCalculator
{
    /// <param name="riskBand">Band label or numeric code; unresolved → default cadence.</param>
    /// <param name="completedOnUtc">The KYC terminal-decision timestamp (UTC).</param>
    DateTime Compute(string? riskBand, DateTime completedOnUtc);
}

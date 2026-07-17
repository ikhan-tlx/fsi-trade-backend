using FSI.Trade.Compliance.Application.Contracts.Kyc;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

/// <summary>
/// Default <see cref="INextReviewDateCalculator"/>: applies the band's cadence
/// (<see cref="IReviewPolicyProvider"/>) to the completion date. Unit is one of
/// Days/Weeks/Months/Years (defaults to Months).
/// </summary>
public class NextReviewDateCalculator : INextReviewDateCalculator
{
    private readonly IReviewPolicyProvider _policy;
    public NextReviewDateCalculator(IReviewPolicyProvider policy) => _policy = policy;

    public DateTime Compute(string? riskBand, DateTime completedOnUtc)
    {
        var f = _policy.GetFrequency(riskBand);
        return (f.Unit ?? "Months").Trim().ToLowerInvariant() switch
        {
            "day"  or "days"  => completedOnUtc.AddDays(f.Value),
            "week" or "weeks" => completedOnUtc.AddDays(f.Value * 7),
            "year" or "years" => completedOnUtc.AddYears(f.Value),
            _                  => completedOnUtc.AddMonths(f.Value)
        };
    }
}

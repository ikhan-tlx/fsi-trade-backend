using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Kyc;
using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

/// <summary>
/// appsettings-backed <see cref="IReviewPolicyProvider"/>. Normalises any raw
/// risk value (label or numeric) to a canonical band via
/// <see cref="RiskBandClassifier"/>, then reads the per-band cadence from
/// <see cref="PeriodicReviewOptions.Frequencies"/> (case-insensitive), falling
/// back to <see cref="PeriodicReviewOptions.DefaultFrequency"/>.
/// </summary>
public class ReviewPolicyProvider : IReviewPolicyProvider
{
    private readonly IOptionsMonitor<PeriodicReviewOptions> _opt;
    public ReviewPolicyProvider(IOptionsMonitor<PeriodicReviewOptions> opt) => _opt = opt;

    public ReviewFrequencyResult GetFrequency(string? riskBand)
    {
        var o   = _opt.CurrentValue;
        var key = NormalizeBand(riskBand);

        if (key is not null && o.Frequencies is not null)
        {
            foreach (var kv in o.Frequencies)
                if (kv.Value is not null && string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return new ReviewFrequencyResult(kv.Value.Value, string.IsNullOrWhiteSpace(kv.Value.Unit) ? "Months" : kv.Value.Unit);
        }

        var d = o.DefaultFrequency ?? new ReviewFrequency { Value = 3, Unit = "Months" };
        return new ReviewFrequencyResult(d.Value, string.IsNullOrWhiteSpace(d.Unit) ? "Months" : d.Unit);
    }

    /// <summary>Raw risk value ("1"/"High"/"PEP"…) → canonical Frequencies key, or null.</summary>
    public static string? NormalizeBand(string? raw) => RiskBandClassifier.Classify(raw) switch
    {
        RiskBand.Pep    => "PEP",
        RiskBand.High   => "High",
        RiskBand.Medium => "Medium",
        RiskBand.Low    => "Low",
        _               => null
    };
}

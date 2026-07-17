using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Infrastructure.Kyc.Review;
using Microsoft.Extensions.Options;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

/// <summary>
/// appsettings-backed cadence resolution: a raw risk value (label OR numeric BRAINS
/// code) normalises to a canonical band and maps to its configured frequency, with an
/// unknown value falling back to the default cadence.
/// </summary>
public class ReviewPolicyProviderTests
{
    private static ReviewPolicyProvider Build()
    {
        var opt = new PeriodicReviewOptions
        {
            DefaultFrequency = new ReviewFrequency { Value = 3, Unit = "Months" },
            Frequencies = new()
            {
                ["PEP"]    = new ReviewFrequency { Value = 1, Unit = "Months" },
                ["High"]   = new ReviewFrequency { Value = 1, Unit = "Months" },
                ["Medium"] = new ReviewFrequency { Value = 2, Unit = "Months" },
                ["Low"]    = new ReviewFrequency { Value = 3, Unit = "Months" },
            }
        };
        return new ReviewPolicyProvider(new StubMonitor<PeriodicReviewOptions>(opt));
    }

    [Theory]
    [InlineData("High",   1)]
    [InlineData("Medium", 2)]
    [InlineData("Low",    3)]
    [InlineData("PEP",    1)]
    [InlineData("1", 1)]   // numeric BRAINS: 1 = High
    [InlineData("2", 2)]   // 2 = Medium
    [InlineData("3", 3)]   // 3 = Low
    [InlineData("0", 1)]   // 0 = PEP -> 1 month
    public void GetFrequency_ResolvesBand(string raw, int expectedValue)
        => Assert.Equal(expectedValue, Build().GetFrequency(raw).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4")]        // legacy "not yet calculated"
    [InlineData("banana")]
    public void GetFrequency_UnknownBand_FallsBackToDefault(string? raw)
        => Assert.Equal(3, Build().GetFrequency(raw).Value);

    [Theory]
    [InlineData("High",   "High")]
    [InlineData("1",      "High")]
    [InlineData("medium", "Medium")]
    [InlineData("PEP",    "PEP")]
    [InlineData("0",      "PEP")]
    [InlineData("4",      null)]
    [InlineData(null,     null)]
    public void NormalizeBand_MapsRawToCanonical(string? raw, string? expected)
        => Assert.Equal(expected, ReviewPolicyProvider.NormalizeBand(raw));

    private sealed class StubMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public StubMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

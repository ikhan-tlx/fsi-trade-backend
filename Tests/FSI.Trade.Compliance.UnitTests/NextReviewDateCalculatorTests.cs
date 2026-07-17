using FSI.Trade.Compliance.Application.Contracts.Kyc;
using FSI.Trade.Compliance.Infrastructure.Kyc.Review;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

/// <summary>
/// Pure cadence math for the Periodic Customer Review feature: given a resolved
/// per-band frequency (Value + Unit), the calculator advances the completion date
/// by that cadence. Uses a fixed fake policy so the test is fully deterministic.
/// </summary>
public class NextReviewDateCalculatorTests
{
    // A month-end anchor so AddMonths clamping is exercised too.
    private static readonly DateTime Anchor = new(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc);

    private static NextReviewDateCalculator With(int value, string unit)
        => new(new FixedPolicy(value, unit));

    [Fact]
    public void Months_AddsCalendarMonths()
        => Assert.Equal(Anchor.AddMonths(2), With(2, "Months").Compute("Medium", Anchor));

    [Fact]
    public void Weeks_AddSevenDaysEach()
        => Assert.Equal(Anchor.AddDays(21), With(3, "Weeks").Compute("x", Anchor));

    [Fact]
    public void Days_AddsDays()
        => Assert.Equal(Anchor.AddDays(10), With(10, "Days").Compute("x", Anchor));

    [Fact]
    public void Years_AddsYears()
        => Assert.Equal(Anchor.AddYears(1), With(1, "Years").Compute("x", Anchor));

    [Theory]
    [InlineData("months")]
    [InlineData("MONTHS")]
    [InlineData("month")]
    public void Unit_IsCaseInsensitive_AndSingularOk(string unit)
        => Assert.Equal(Anchor.AddMonths(1), With(1, unit).Compute("High", Anchor));

    [Theory]
    [InlineData("")]
    [InlineData("fortnights")]
    public void UnknownOrEmptyUnit_DefaultsToMonths(string unit)
        => Assert.Equal(Anchor.AddMonths(2), With(2, unit).Compute("x", Anchor));

    private sealed class FixedPolicy : IReviewPolicyProvider
    {
        private readonly ReviewFrequencyResult _r;
        public FixedPolicy(int value, string unit) => _r = new ReviewFrequencyResult(value, unit);
        public ReviewFrequencyResult GetFrequency(string? riskBand) => _r;
    }
}

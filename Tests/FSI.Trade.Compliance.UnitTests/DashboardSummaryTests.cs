using FSI.Trade.Compliance.Application.Features.Dashboard.Summary;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class RiskBandClassifierTests
{
    [Theory]
    [InlineData("PEP", RiskBand.Pep)]
    [InlineData("High", RiskBand.High)]
    [InlineData("high risk", RiskBand.High)]
    [InlineData("Medium", RiskBand.Medium)]
    [InlineData("Medial", RiskBand.Medium)]
    [InlineData("Low", RiskBand.Low)]
    [InlineData("LOW", RiskBand.Low)]
    public void Classify_TextBands(string raw, RiskBand expected)
        => Assert.Equal(expected, RiskBandClassifier.Classify(raw));

    [Theory]
    [InlineData("0", RiskBand.Pep)]
    [InlineData("1", RiskBand.High)]
    [InlineData("1.0", RiskBand.High)]
    [InlineData("2", RiskBand.Medium)]
    [InlineData("3", RiskBand.Low)]
    [InlineData("4", RiskBand.Unknown)]   // legacy "not calculated" sentinel
    public void Classify_NumericV2Status(string raw, RiskBand expected)
        => Assert.Equal(expected, RiskBandClassifier.Classify(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    public void Classify_UnknownOrEmpty(string? raw)
        => Assert.Equal(RiskBand.Unknown, RiskBandClassifier.Classify(raw));

    [Theory]
    [InlineData(RiskBand.High, true)]
    [InlineData(RiskBand.Pep, true)]
    [InlineData(RiskBand.Medium, false)]
    [InlineData(RiskBand.Low, false)]
    [InlineData(RiskBand.Unknown, false)]
    public void IsHighRisk(RiskBand band, bool expected)
        => Assert.Equal(expected, RiskBandClassifier.IsHighRisk(band));
}

public class DashboardRangeTests
{
    private static readonly DateTime Now = new(2026, 6, 3, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Default_Is30d()
    {
        var (norm, from, to) = DashboardRange.Resolve(null, Now);
        Assert.Equal("30d", norm);
        Assert.Equal(Now.AddDays(-30), from);
        Assert.Equal(Now, to);
    }

    [Fact]
    public void SevenDay()
    {
        var (norm, from, _) = DashboardRange.Resolve("7d", Now);
        Assert.Equal("7d", norm);
        Assert.Equal(Now.AddDays(-7), from);
    }

    [Fact]
    public void Qtd_StartsAtQuarter()
    {
        var (norm, from, _) = DashboardRange.Resolve("qtd", Now);   // June → Q2 starts Apr 1
        Assert.Equal("QTD", norm);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), from);
    }

    [Fact]
    public void Ytd_StartsAtYear()
    {
        var (norm, from, _) = DashboardRange.Resolve("YTD", Now);
        Assert.Equal("YTD", norm);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), from);
    }
}

public class DashboardTrendTests
{
    [Fact]
    public void Weekly_30d_BucketsAndCounts()
    {
        var from = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);
        var dates = new List<DateTime>
        {
            new(2026, 5, 5,  0, 0, 0, DateTimeKind.Utc),   // Wk 1
            new(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),   // Wk 3
            new(2026, 6, 2,  0, 0, 0, DateTimeKind.Utc),   // Wk 5
        };

        var trend = DashboardTrend.Build("30d", from, to, dates);

        Assert.Equal("count", trend.unit);
        Assert.Equal(5, trend.points.Count);
        Assert.Equal("Wk 1", trend.points[0].label);
        Assert.Equal(1, trend.points[0].value);   // Wk 1
        Assert.Equal(0, trend.points[1].value);   // Wk 2
        Assert.Equal(1, trend.points[2].value);   // Wk 3
        Assert.Equal(1, trend.points[4].value);   // Wk 5
    }

    [Fact]
    public void Daily_7d_BucketsAndCounts()
    {
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        var dates = new List<DateTime>
        {
            new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc),
            new(2026, 6, 2, 14, 0, 0, DateTimeKind.Utc),
            new(2026, 6, 4,  9, 0, 0, DateTimeKind.Utc),
        };

        var trend = DashboardTrend.Build("7d", from, to, dates);

        Assert.Equal(4, trend.points.Count);       // 6/1, 6/2, 6/3, 6/4
        Assert.Equal(2, trend.points[1].value);    // 6/2 → 2
        Assert.Equal(0, trend.points[2].value);    // 6/3 → 0
        Assert.Equal(1, trend.points[3].value);    // 6/4 → 1
    }
}

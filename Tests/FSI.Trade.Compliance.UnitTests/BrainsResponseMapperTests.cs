using FSI.Trade.Compliance.Infrastructure.Integrations.Brains;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class BrainsResponseMapperTests
{
    [Theory]
    [InlineData("0", "PEP")]
    [InlineData("1", "High")]
    [InlineData("2", "Medium")]
    [InlineData("3", "Low")]
    public void RiskBand_MapsKnownStatuses(string status, string expected)
        => Assert.Equal(expected, BrainsResponseMapper.RiskBand(status));

    [Theory]
    [InlineData("9")]
    [InlineData("")]
    [InlineData(null)]
    public void RiskBand_UnknownOrEmpty_IsNull(string? status)
        => Assert.Null(BrainsResponseMapper.RiskBand(status));

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSuccess_OnlyZeroIsSuccess(string? transOk, bool expected)
        => Assert.Equal(expected, BrainsResponseMapper.IsSuccess(transOk));

    [Fact]
    public void ToKycResult_Success_ReturnsBand_NoName()
    {
        var resp = new BrainsKycResponse { Status = "2", TransOk = "0" };
        var result = BrainsResponseMapper.ToKycResult(resp);

        Assert.NotNull(result);
        Assert.Equal("Medium", result!.RiskScore);
        Assert.Null(result.CustomerName);   // v2.0 returns no name
    }

    [Fact]
    public void ToKycResult_Failed_IsNull()
    {
        // TRANSOK=1 bundles not-found / multiple / error.
        var resp = new BrainsKycResponse { Status = "", TransOk = "1", ErrorNo = "-1", ErrorMsg = "x" };
        Assert.Null(BrainsResponseMapper.ToKycResult(resp));
    }

    [Fact]
    public void ToKycResult_SuccessButUnknownStatus_IsNull()
    {
        var resp = new BrainsKycResponse { Status = "9", TransOk = "0" };
        Assert.Null(BrainsResponseMapper.ToKycResult(resp));
    }

    [Fact]
    public void ToKycResult_Null_IsNull()
        => Assert.Null(BrainsResponseMapper.ToKycResult(null));
}

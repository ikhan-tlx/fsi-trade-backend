using FSI.Trade.Compliance.Infrastructure.Integrations.Brains;
using FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

/// <summary>
/// Verifies the ported legacy caseInsertionController / Cstm_Brains reconciliation:
/// FCCM (TBML) 1/2/3 = Low/Medium/High vs BRAINS 0/1/2/3 = PEP/High/Medium/Low,
/// "higher-risk wins" with the legacy quirks (BRAINS PEP=0 always wins; the raw
/// cross-scale numeric comparison is preserved verbatim), Final numeric → label
/// (1 Low / 2 Medium / 3 or 0 High), and the attribution suffix.
/// </summary>
public class CstmBrainsReconciliationTests
{
    // ---- BRAINS unavailable → FCCM only, " - TBML" ----
    [Theory]
    [InlineData(1, 1, "Low",    "Low - TBML")]
    [InlineData(2, 2, "Medium", "Medium - TBML")]
    [InlineData(3, 3, "High",   "High - TBML")]
    public void Reconcile_NoBrains_UsesFccm(int fccm, int expFinal, string expLabel, string expDisplay)
    {
        var (finalNum, label, display) = FccmCaseIdPoller.Reconcile(fccm, null);
        Assert.Equal(expFinal, finalNum);
        Assert.Equal(expLabel, label);
        Assert.Equal(expDisplay, display);
    }

    // ---- BRAINS PEP (0) always wins → Final 0 → High, " - BRAINS" ----
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Reconcile_BrainsPep_AlwaysWins(int fccm)
    {
        var (finalNum, label, display) = FccmCaseIdPoller.Reconcile(fccm, 0);
        Assert.Equal(0, finalNum);
        Assert.Equal("High", label);
        Assert.Equal("High - BRAINS", display);
    }

    // ---- FCCM > BRAINS (raw numeric) → FCCM wins, " - TBML" ----
    [Fact]
    public void Reconcile_FccmGreater_UsesFccm_Tbml()
    {
        var (finalNum, label, display) = FccmCaseIdPoller.Reconcile(3, 1);
        Assert.Equal(3, finalNum);
        Assert.Equal("High", label);
        Assert.Equal("High - TBML", display);
    }

    // ---- FCCM < BRAINS (raw numeric) → BRAINS wins, " - BRAINS" ----
    [Fact]
    public void Reconcile_BrainsGreater_UsesBrains()
    {
        var (finalNum, label, display) = FccmCaseIdPoller.Reconcile(1, 3);
        Assert.Equal(3, finalNum);
        Assert.Equal("High", label);
        Assert.Equal("High - BRAINS", display);
    }

    // ---- Equal → FCCM, "SCORES ARE EQUAL" ----
    [Fact]
    public void Reconcile_Equal_UsesFccm_EqualSuffix()
    {
        var (finalNum, label, display) = FccmCaseIdPoller.Reconcile(2, 2);
        Assert.Equal(2, finalNum);
        Assert.Equal("Medium", label);
        Assert.Equal("Medium - BRAINS & TBML SCORES ARE EQUAL", display);
    }

    // ---- $.RiskScore JSON writer preserves other keys ----
    [Fact]
    public void SetRiskScoreJson_NullBlob_CreatesObject()
        => Assert.Equal("{\"RiskScore\":\"High - TBML\"}",
                        FccmCaseIdPoller.SetRiskScoreJson(null, "High - TBML"));

    [Fact]
    public void SetRiskScoreJson_ExistingBlob_PreservesOtherKeys()
    {
        var result = FccmCaseIdPoller.SetRiskScoreJson("{\"Foo\":\"bar\",\"RiskScore\":\"2\"}", "Low - BRAINS");
        Assert.Contains("\"Foo\":\"bar\"", result);
        Assert.Contains("\"RiskScore\":\"Low - BRAINS\"", result);
    }

    // ---- BRAINS mapper now understands the legacy "{code}|{name}" STATUS ----
    [Fact]
    public void RiskBand_PipeDelimited_TakesCode()
        => Assert.Equal("Medium", BrainsResponseMapper.RiskBand("2|ACME Corp"));

    [Fact]
    public void ToKycResult_PipeDelimited_FillsNameFromSecondSegment()
    {
        var result = BrainsResponseMapper.ToKycResult(new BrainsKycResponse { Status = "1|John Doe", TransOk = "0" });
        Assert.NotNull(result);
        Assert.Equal("High", result!.RiskScore);
        Assert.Equal("John Doe", result.CustomerName);
    }
}

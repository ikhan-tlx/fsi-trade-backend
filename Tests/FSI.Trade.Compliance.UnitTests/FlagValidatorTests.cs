using FSI.Trade.Compliance.Application.Features.Flags.Admin.Scopes;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class AddFlagScopeCommandValidatorTests
{
    private readonly AddFlagScopeCommandValidator _v = new();

    [Fact]
    public void AllProducts_NullProduct_NoTab_IsValid()
    {
        var result = _v.Validate(new AddFlagScopeCommand { flagId = 1, productId = null, tabId = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ProductLevel_IsValid()
    {
        var result = _v.Validate(new AddFlagScopeCommand { flagId = 1, productId = 5, tabId = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ProductPlusTab_IsValid()
    {
        var result = _v.Validate(new AddFlagScopeCommand { flagId = 1, productId = 5, tabId = 9 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void TabWithoutProduct_IsInvalid()
    {
        var result = _v.Validate(new AddFlagScopeCommand { flagId = 1, productId = null, tabId = 9 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddFlagScopeCommand.productId));
    }

    [Fact]
    public void NonPositiveProduct_IsInvalid()
    {
        var result = _v.Validate(new AddFlagScopeCommand { flagId = 1, productId = 0, tabId = null });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonPositiveFlag_IsInvalid()
    {
        var result = _v.Validate(new AddFlagScopeCommand { flagId = 0, productId = 5, tabId = null });
        Assert.False(result.IsValid);
    }
}

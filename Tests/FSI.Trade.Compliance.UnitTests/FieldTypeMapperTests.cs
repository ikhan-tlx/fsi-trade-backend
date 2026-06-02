using FSI.Trade.Compliance.Application.Features.Products.Forms;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

public class FieldTypeMapperTests
{
    [Theory]
    [InlineData(2,  "date")]
    [InlineData(3,  "text")]
    [InlineData(4,  "textarea")]
    [InlineData(24, "spacer")]
    [InlineData(25, "select")]
    [InlineData(26, "multi")]
    [InlineData(27, "repeat")]
    [InlineData(28, "boolean")]
    [InlineData(30, "section")]
    [InlineData(40, "tableRoot")]
    [InlineData(55, "number")]
    [InlineData(64, "endMarker")]
    public void MapsKnownFieldTypeLkpValues(int lkp, string expected)
        => Assert.Equal(expected, FieldTypeMapper.ToSemanticType(lkp, null));

    [Fact]
    public void ProductTabLookup_WinsOverNumericType()
    {
        // Field_Type_Lkp for ProductTab is a runtime Lookup_ID (not a fixed
        // number) — detection must be by Field_Lookup_Type.
        Assert.Equal("productTab", FieldTypeMapper.ToSemanticType(99999, "ProductTab"));
        Assert.Equal("productTab", FieldTypeMapper.ToSemanticType(28, "producttab")); // case-insensitive
    }

    [Theory]
    [InlineData(null)]
    [InlineData(999)]
    [InlineData(0)]
    public void UnknownOrNull_DefaultsToText(int? lkp)
        => Assert.Equal("text", FieldTypeMapper.ToSemanticType(lkp, null));

    [Fact]
    public void NonProductTabLookupType_DoesNotForceProductTab()
        => Assert.Equal("select", FieldTypeMapper.ToSemanticType(25, "Currency"));

    // ---- dynamic display-name resolution (from the 'field' lookup) ----

    [Fact]
    public void ResolveTypeName_ReturnsLookupVisibleValue_WhenPresent()
    {
        var map = new Dictionary<int, string> { [3] = "Text", [25] = "Dropdown" };
        Assert.Equal("Dropdown", FieldTypeMapper.ResolveTypeName(25, map));
        Assert.Equal("Text",     FieldTypeMapper.ResolveTypeName(3,  map));
    }

    [Fact]
    public void ResolveTypeName_ReturnsNull_WhenCodeMissingOrNull()
    {
        var map = new Dictionary<int, string> { [3] = "Text" };
        Assert.Null(FieldTypeMapper.ResolveTypeName(99,   map));   // no row for code
        Assert.Null(FieldTypeMapper.ResolveTypeName(null, map));   // field has no type code
    }
}

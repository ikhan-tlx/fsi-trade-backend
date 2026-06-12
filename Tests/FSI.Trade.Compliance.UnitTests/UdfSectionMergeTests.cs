using System.Text.Json;
using FSI.Trade.Compliance.Application.Features.Transactions.Update;
using Xunit;

namespace FSI.Trade.Compliance.UnitTests;

/// <summary>
/// Covers the PUT /Transaction/{id} UDF section-routing fix (2026-06-04).
/// The FE sends dynamic-form values as top-level objects keyed by
/// Field_Table_Name; these helpers merge each section into the matching
/// entity's UDF_Data without clobbering keys the FE didn't resend.
/// </summary>
public class UdfSectionMergeTests
{
    private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement;

    private static Dictionary<string, JsonElement> Sections(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void TryGetSection_FindsObjectSection_CaseInsensitive()
    {
        var sections = Sections("""
            { "TmxTransactionDetail": { "ILF_LCAmount": "123" } }
            """);

        var hit = UdfSectionMerge.TryGetSection(sections, "tmxtransactiondetail");

        Assert.True(hit.HasValue);
        Assert.Equal("123", hit!.Value.GetProperty("ILF_LCAmount").GetString());
    }

    [Theory]
    [InlineData("""{ "TmxTransactionDetail": null }""")]
    [InlineData("""{ "TmxTransactionDetail": [ {"x":1} ] }""")]
    [InlineData("""{ "Other": { "x": 1 } }""")]
    public void TryGetSection_ReturnsNull_ForNonObjectOrMissing(string json)
        => Assert.False(UdfSectionMerge.TryGetSection(Sections(json), "TmxTransactionDetail").HasValue);

    [Fact]
    public void Merge_IntoEmpty_KeepsIncomingKeys()
    {
        var result = UdfSectionMerge.Merge(null, Obj("""{ "ILF_LCAmount": "123", "ILF_AppCountry": "36400" }"""));

        var parsed = Sections(result);
        Assert.Equal("123",   parsed["ILF_LCAmount"].GetString());
        Assert.Equal("36400", parsed["ILF_AppCountry"].GetString());
    }

    [Fact]
    public void Merge_PreservesServerKeys_NotResentByFe()
    {
        // Customer UDF already carries a server-managed RiskScore (set at create).
        // A later save that only sends customer field values must NOT drop it.
        var existing = """{ "RiskScore": 42 }""";

        var result = UdfSectionMerge.Merge(existing, Obj("""{ "ILF_CustomerNo": "123" }"""));

        var parsed = Sections(result);
        Assert.Equal(42, parsed["RiskScore"].GetInt32());
        Assert.Equal("123", parsed["ILF_CustomerNo"].GetString());
    }

    [Fact]
    public void Merge_IncomingOverwritesSameKey()
    {
        var result = UdfSectionMerge.Merge("""{ "ILF_LCAmount": "100" }""", Obj("""{ "ILF_LCAmount": "123" }"""));
        Assert.Equal("123", Sections(result)["ILF_LCAmount"].GetString());
    }

    [Fact]
    public void Merge_MalformedExistingJson_TreatedAsEmpty()
    {
        // Legacy / corrupt UDF must not blow up the save — incoming wins.
        var result = UdfSectionMerge.Merge("not-json", Obj("""{ "ILF_LCAmount": "123" }"""));
        Assert.Equal("123", Sections(result)["ILF_LCAmount"].GetString());
    }

    // ---- MergeDetail: flat fields at root + grid sections nested ----

    private static List<KeyValuePair<string, JsonElement>> Nested(string json)
    {
        var d = Sections(json);
        return new List<KeyValuePair<string, JsonElement>>(d);
    }

    [Fact]
    public void MergeDetail_PutsFlatFieldsAtRoot_AndNestsGridSections()
    {
        var flat = Obj("""{ "ILF_LCAmount": "123", "ILF_AppCountry": "36400" }""");
        var nested = Nested("""{ "TmxHsCodeListILF": { "GOODSDETAILF": [ { "ILF_HScode": "1" } ] } }""");

        var result = UdfSectionMerge.MergeDetail(null, flat, null, nested);
        var parsed = Sections(result!);

        // flat fields are at the root, keyed by Field_Name
        Assert.Equal("123", parsed["ILF_LCAmount"].GetString());
        // the grid section is nested under its own key (legacy shape)
        var grid = parsed["TmxHsCodeListILF"].GetProperty("GOODSDETAILF");
        Assert.Equal(JsonValueKind.Array, grid.ValueKind);
        Assert.Equal("1", grid[0].GetProperty("ILF_HScode").GetString());
    }

    [Fact]
    public void MergeDetail_PreservesExistingKeys_NotResent()
    {
        var existing = """{ "ILF_HSCode": 26349, "RiskScore": "High" }""";
        var flat = Obj("""{ "ILF_LCAmount": "123" }""");

        var result = UdfSectionMerge.MergeDetail(existing, flat, null, null);
        var parsed = Sections(result!);

        Assert.Equal(26349, parsed["ILF_HSCode"].GetInt32());   // survived
        Assert.Equal("High", parsed["RiskScore"].GetString());  // survived
        Assert.Equal("123", parsed["ILF_LCAmount"].GetString()); // added
    }

    [Fact]
    public void MergeDetail_NothingToApply_PreservesExistingBlob()
    {
        var existing = """{ "ILF_HSCode": 26349 }""";
        var result = UdfSectionMerge.MergeDetail(existing, null, null, null);
        Assert.Equal(existing, result); // unchanged — never nulled
    }
}

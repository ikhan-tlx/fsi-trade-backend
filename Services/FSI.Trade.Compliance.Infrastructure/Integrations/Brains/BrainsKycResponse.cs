using System.Text.Json.Serialization;
using FSI.Trade.Compliance.Application.Contracts.Integrations;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Brains;

/// <summary>
/// Raw BRAINS QueryKycInfoServlet v2.0 response. Keys are upper-case per the
/// interface doc; <see cref="JsonPropertyName"/> binds them explicitly.
///
/// <code>
/// { "STATUS":"2", "TRANSOK":"0", "ERROR_NO":"", "ERROR_MSG":"" }
/// </code>
/// </summary>
public class BrainsKycResponse
{
    /// <summary>Risk band: 0 = PEP, 1 = High, 2 = Medium, 3 = Low.</summary>
    [JsonPropertyName("STATUS")]    public string? Status   { get; set; }

    /// <summary>Transaction status: 0 = success, 1 = failed (not found / multiple / other).</summary>
    [JsonPropertyName("TRANSOK")]   public string? TransOk  { get; set; }

    [JsonPropertyName("ERROR_NO")]  public string? ErrorNo  { get; set; }
    [JsonPropertyName("ERROR_MSG")] public string? ErrorMsg { get; set; }
}

/// <summary>
/// Pure mapping from the BRAINS v2.0 response to the vendor-agnostic
/// <see cref="KycResult"/>. Public + static so it can be unit-tested without
/// an HttpClient. Mapping rules (interface doc v2.0):
/// <list type="bullet">
///   <item><c>TRANSOK != "0"</c> ⇒ no usable result (customer not found in
///         BRAINS / multiple matches / error) ⇒ null.</item>
///   <item><c>STATUS</c> ⇒ risk band: 0 PEP, 1 High, 2 Medium, 3 Low.</item>
///   <item>Unknown <c>STATUS</c> ⇒ null (don't pass junk downstream).</item>
/// </list>
/// Note: v2.0 returns no customer name — <see cref="KycResult.CustomerName"/>
/// is always null from BRAINS now (the name comes from the customer master).
/// </summary>
public static class BrainsResponseMapper
{
    public static bool IsSuccess(string? transOk)
        => string.Equals(transOk?.Trim(), "0", StringComparison.Ordinal);

    public static string? RiskBand(string? status) => status?.Trim() switch
    {
        "0" => "PEP",
        "1" => "High",
        "2" => "Medium",
        "3" => "Low",
        _   => null
    };

    public static KycResult? ToKycResult(BrainsKycResponse? resp)
    {
        if (resp is null || !IsSuccess(resp.TransOk)) return null;

        var band = RiskBand(resp.Status);
        if (band is null) return null;

        return new KycResult { RiskScore = band, CustomerName = null };
    }
}

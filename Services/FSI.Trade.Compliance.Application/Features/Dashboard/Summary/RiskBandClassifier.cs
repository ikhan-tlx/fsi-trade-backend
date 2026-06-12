namespace FSI.Trade.Compliance.Application.Features.Dashboard.Summary;

public enum RiskBand { Low, Medium, High, Pep, Unknown }

/// <summary>
/// Classifies a customer-snapshot <c>RiskScore</c> value (from UDF_Data) into a
/// risk band. The stored value varies — it may be a text band ("PEP" / "High" /
/// "Medium" / "Low") or a numeric code. Numeric is interpreted per the BRAINS
/// v2.0 STATUS dictionary: 0 = PEP, 1 = High, 2 = Medium, 3 = Low. Anything
/// unrecognised (including the legacy "4" = not-yet-calculated, or missing risk
/// on non-KYC customers) is <see cref="RiskBand.Unknown"/> and not counted in a
/// band.
///
/// NOTE: if the live data uses a different numeric scheme, only the numeric
/// branch needs adjusting — verify with:
///   SELECT DISTINCT JSON_VALUE(UDF_Data,'$.RiskScore') FROM dbo.TmX_Customer_Master;
/// </summary>
public static class RiskBandClassifier
{
    public static RiskBand Classify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return RiskBand.Unknown;
        var v = raw.Trim();

        // Text bands first (most reliable).
        var lower = v.ToLowerInvariant();
        if (lower.Contains("pep"))                                return RiskBand.Pep;
        if (lower.Contains("high"))                               return RiskBand.High;
        if (lower.Contains("medium") || lower.Contains("medial")) return RiskBand.Medium;
        if (lower.Contains("low"))                                return RiskBand.Low;

        // Numeric (BRAINS v2.0 STATUS): 0 PEP / 1 High / 2 Medium / 3 Low.
        if (double.TryParse(v, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return (int)n switch
            {
                0 => RiskBand.Pep,
                1 => RiskBand.High,
                2 => RiskBand.Medium,
                3 => RiskBand.Low,
                _ => RiskBand.Unknown
            };
        }

        return RiskBand.Unknown;
    }

    /// <summary>High + PEP are both "high-risk" for the KPI / donut high segment.</summary>
    public static bool IsHighRisk(RiskBand band) => band is RiskBand.High or RiskBand.Pep;
}

namespace FSI.Trade.Compliance.Domain.Entities.Reference;

/// <summary>
/// Read-only row of the high-risk / sanctioned country watchlist. Maps to
/// the existing <c>dbo.REPO_SANCTIONED_COUNTRIES</c> table (the same list
/// the legacy screening used). The engine screens a transaction's country
/// fields (ISO code) against this for the country auto flags.
/// </summary>
public class SanctionedCountry
{
    public int      RecordId        { get; set; }   // V_RECORD_ID
    public string   CountryName     { get; set; } = "";
    public string   IsoCountryCode  { get; set; } = "";
    public string   Iso3CountryCode { get; set; } = "";
    public string   CountrySynonyms { get; set; } = "";
    public string   DataSource      { get; set; } = "";
    public int      RiskScore       { get; set; }
}

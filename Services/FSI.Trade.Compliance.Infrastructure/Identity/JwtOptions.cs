namespace FSI.Trade.Compliance.Infrastructure.Identity;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer                    { get; set; } = "fsi.trade.compliance";
    public string Audience                  { get; set; } = "fsi.trade.compliance.client";
    public string Key                       { get; set; } = "";
    public int    AccessTokenDefaultMinutes { get; set; } = 60;
    public double RefreshTokenFactor        { get; set; } = 1.5;
    public Dictionary<string, int> RoleTtlMinutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

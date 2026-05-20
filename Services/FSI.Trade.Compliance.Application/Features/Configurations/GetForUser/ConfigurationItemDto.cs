namespace FSI.Trade.Compliance.Application.Features.Configurations.GetForUser;

/// <summary>
/// One configuration row scoped to the caller's tenant. The FE consumes these
/// at app-init for feature toggles, business limits, and integration endpoints.
/// </summary>
public class ConfigurationItemDto
{
    public int     configurationId          { get; set; }
    public string? configurationKey         { get; set; }
    public string? configurationValue       { get; set; }
    public string? configurationDescription { get; set; }
    public int?    productId                { get; set; }
    public int?    timeZoneId               { get; set; }
}

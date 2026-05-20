namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Configuration. Tenant-scoped key/value pairs
/// that drive feature toggles, business limits, and integration endpoints.
///
/// Filter by tenant on every read: a caller's <c>TenantId</c> determines which
/// configuration set they see. Cross-tenant reads are not allowed.
/// </summary>
public class AppConfiguration
{
    public int       Id                       { get; set; }
    public int       TenantId                 { get; set; } = 1;
    public string?   ConfigurationKey         { get; set; }
    public string?   ConfigurationValue       { get; set; }
    public string?   ConfigurationDescription { get; set; }
    public int?      ConfigurationTypeLkpId   { get; set; }
    public int?      ConfigurationStatusLkpId { get; set; }
    public DateTime? EffectiveStartDate       { get; set; }
    public DateTime? EffectiveEndDate         { get; set; }
    public int?      TimeZoneId               { get; set; }
    public int?      ProductId                { get; set; }
    public string?   CreatedBy                { get; set; }
    public DateTime? CreatedDate              { get; set; }
    public string?   LastUpdatedBy            { get; set; }
    public DateTime? LastUpdatedDate          { get; set; }
}

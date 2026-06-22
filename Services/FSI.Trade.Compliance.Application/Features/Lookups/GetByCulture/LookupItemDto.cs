namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;

/// <summary>
/// One row of the lookup catalogue (v1 — flat list shape; FE groups
/// client-side by <see cref="lookupType"/>).
///
/// <para>
/// Trimmed in Slice 11: dropped <c>lookupName</c> and <c>localeLabel</c>
/// — neither is consumed by the FE. <c>localeLabel</c> resolution now
/// happens server-side, surfacing the correct text via
/// <see cref="visibleValue"/> based on the requested culture.
/// </para>
///
/// <para>
/// For the leaner per-type grouped shape, see the v2 endpoint
/// <c>GET /api/v2/Lookup/{culture}</c>.
/// </para>
/// </summary>
public class LookupItemDto
{
    public int lookupId { get; set; }
    public int? parentLookupId { get; set; }
    public string lookupType { get; set; } = "";
    public string lookupName { get; set; }
    public string localeLabel { get; set; }
    public string? hiddenValue { get; set; }
    public string? visibleValue { get; set; }
    // Carries per-row metadata the FE parses (e.g. REPORT_TYPE rows encode the
    // report's stored-procedure name + parameters here: "sp_name,from_date,...").
    // Without this the Business Reports page can't resolve the SP and sends an
    // empty StoredProcedure -> 400 "Stored procedure name is required".
    public string? description { get; set; }
    public int? sortOrder { get; set; }
    public bool isActive { get; set; }
}

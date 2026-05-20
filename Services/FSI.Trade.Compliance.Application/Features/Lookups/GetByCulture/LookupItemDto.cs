namespace FSI.Trade.Compliance.Application.Features.Lookups.GetByCulture;

/// <summary>
/// One row of the lookup catalog. The FE typically groups these client-side
/// by <see cref="lookupType"/> for dropdown population.
/// </summary>
public class LookupItemDto
{
    public int     lookupId       { get; set; }
    public int?    parentLookupId { get; set; }
    public string  lookupType     { get; set; } = "";
    public string? lookupName     { get; set; }
    public string? visibleValue   { get; set; }
    public string? hiddenValue    { get; set; }
    public string? localeLabel    { get; set; }
    public int?    sortOrder      { get; set; }
    public bool    isActive       { get; set; }
}

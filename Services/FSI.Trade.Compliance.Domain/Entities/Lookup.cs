namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Lookup. Single denormalised reference table for
/// every enumerable value the FE needs (countries, products, status codes,
/// designations, etc.). Grouped by <see cref="LookupType"/> in code, not by
/// schema.
///
/// Localisation: <see cref="LocaleLabel"/> holds the localised display text;
/// <see cref="LocaleId"/> identifies which culture/locale the row belongs to.
/// Slice 3 uses Locale_Label as a culture-token match; refine if the live
/// schema turns out to use a separate locale table for the mapping.
///
/// Active_Flag is currently ignored on read (per FSI direction, May 2026) —
/// every row is treated as live until the lifecycle semantics are formalised.
/// Tracked in BACKLOG.md.
/// </summary>
public class Lookup
{
    public int       Id                  { get; set; }
    public int?      ParentLookupId      { get; set; }
    public int       TenantId            { get; set; } = 1;
    public string?   LookupType          { get; set; }
    public string?   LookupName          { get; set; }
    public string?   Description         { get; set; }
    public string?   VisibleValue        { get; set; }
    public string?   HiddenValue         { get; set; }
    public bool      IsActive            { get; set; } = true;     // Is_Active
    public bool      ActiveFlag          { get; set; } = true;     // Active_Flag (separate column from Is_Active in legacy)
    public bool      UserEditable        { get; set; }
    public int?      SortOrder           { get; set; }
    public string?   LocaleLabel         { get; set; }
    public int?      LocaleId            { get; set; }
    public string?   CreatedBy           { get; set; }
    public DateTime  CreatedDate         { get; set; }
    public string?   LastUpdatedBy       { get; set; }
    public DateTime? LastUpdatedDate     { get; set; }
}

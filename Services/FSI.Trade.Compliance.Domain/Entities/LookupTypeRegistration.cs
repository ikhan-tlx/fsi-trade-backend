namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Registry of lookup TYPES that may be managed from the Lookup Management module
/// (<c>TmX_Lookup_Type</c>). A lookup "type" is otherwise just a distinct
/// <c>Lookup_Type</c> string across <c>TmX_Lookup</c> rows; this table records which
/// types are admin-manageable so newly created types are governed as data (not a
/// compiled allow-list). The legacy compiled allow-list still covers the original
/// curated types; a row here (Is_Manageable = 1) additionally makes a type manageable.
/// </summary>
public class LookupTypeRegistration
{
    public long     Id           { get; set; }
    public string   LookupType   { get; set; } = "";
    public bool     IsManageable { get; set; } = true;
    /// <summary>True for seeded/system types; false for admin-created ones.</summary>
    public bool     IsSystem     { get; set; }
    public string?  CreatedBy    { get; set; }
    public DateTime CreatedDate  { get; set; }
}

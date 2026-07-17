namespace FSI.Trade.Compliance.Domain.Entities.Reference;

/// <summary>
/// A reference-list CATEGORY that Auto Flag rules point at (High-risk country,
/// Tax-haven country, Sanctioned countries, Screened goods…). Maps to
/// <c>dbo.TmX_Reference_Type</c> (migration 2026_07_023). <see cref="TypeCode"/>
/// is the stable identifier a rule stores in <c>Reference_List_Code</c>.
/// </summary>
public class ReferenceType
{
    public long      ReferenceTypeId { get; set; }
    public string    TypeCode        { get; set; } = "";
    public string    TypeName        { get; set; } = "";
    public string?   Description     { get; set; }
    /// <summary>The TmX_Lookup group (domain) this category's values are drawn from.
    /// Drives the FE value picker + the rule-builder dropdown filter. NULL = literal.</summary>
    public string?   LookupType      { get; set; }
    /// <summary>JSON array describing the UDF fields every value of this type carries.</summary>
    public string?   UdfSchema       { get; set; }
    public bool      ActiveFlag      { get; set; } = true;
    /// <summary>"migrated" | "user" — provenance chip only; all types are editable.</summary>
    public string    Source          { get; set; } = "user";
    public string?   CreatedBy       { get; set; }
    public DateTime  CreatedDate     { get; set; }
    public string?   LastUpdatedBy   { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

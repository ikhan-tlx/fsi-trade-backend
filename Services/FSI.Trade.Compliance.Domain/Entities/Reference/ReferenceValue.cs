namespace FSI.Trade.Compliance.Domain.Entities.Reference;

/// <summary>
/// A deduplicated reference value (a country, an HS code…). Maps to
/// <c>dbo.TmX_Reference_Value</c> (migration 2026_07_023). <see cref="Code"/>
/// equals the canonical <c>TmX_Lookup.Hidden_Value</c> within its
/// <see cref="LookupType"/> group, so rule matching compares the transaction's
/// resolved hidden value directly against Code (design D1-a).
/// </summary>
public class ReferenceValue
{
    public long      ReferenceValueId { get; set; }
    public string    Code             { get; set; } = "";
    public string?   Name             { get; set; }
    public string?   Description      { get; set; }
    /// <summary>The lookup group this value belongs to; NULL = literal (no lookup).</summary>
    public string?   LookupType       { get; set; }
    /// <summary>JSON object of the UDF values, keyed per the owning type's schema.</summary>
    public string?   UdfData          { get; set; }
    public bool      ActiveFlag       { get; set; } = true;
    public string?   CreatedBy        { get; set; }
    public DateTime  CreatedDate      { get; set; }
    public string?   LastUpdatedBy    { get; set; }
    public DateTime? LastUpdatedDate  { get; set; }
}

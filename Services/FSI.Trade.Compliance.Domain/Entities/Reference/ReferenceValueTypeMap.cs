namespace FSI.Trade.Compliance.Domain.Entities.Reference;

/// <summary>
/// The many-to-many membership between a <see cref="ReferenceValue"/> and a
/// <see cref="ReferenceType"/> — a value can belong to many categories and a
/// category holds many values. Maps to <c>dbo.TmX_Reference_Value_Type_Map</c>
/// (migration 2026_07_023).
/// </summary>
public class ReferenceValueTypeMap
{
    public long     MapId            { get; set; }
    public long     ReferenceValueId { get; set; }
    public long     ReferenceTypeId  { get; set; }
    public bool     ActiveFlag       { get; set; } = true;
    public string?  CreatedBy        { get; set; }
    public DateTime CreatedDate      { get; set; }
}

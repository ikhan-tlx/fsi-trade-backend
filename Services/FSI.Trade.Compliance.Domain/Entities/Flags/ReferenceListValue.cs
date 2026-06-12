namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// A single value belonging to a <see cref="ReferenceList"/>. Maps to
/// <c>dbo.TmX_Reference_List_Value</c> (migration 2026_06_040).
/// </summary>
public class ReferenceListValue
{
    public int      ReferenceListValueId { get; set; }
    public int      ReferenceListId      { get; set; }
    public string   Value                { get; set; } = "";
    public string?  ValueType            { get; set; }
    public bool     ActiveFlag           { get; set; } = true;
    public string?  CreatedBy            { get; set; }
    public DateTime CreatedDate          { get; set; }
}

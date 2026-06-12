namespace FSI.Trade.Compliance.Domain.Entities.Flags;

/// <summary>
/// A named reference list addressable by code (tax-haven, land-locked,
/// dual-use HS, perishable HS, …). Maps to <c>dbo.TmX_Reference_List</c>
/// (migration 2026_06_040). Country/goods screening reuse the existing
/// <c>REPO_SANCTIONED_COUNTRIES</c> / <c>REPO_SCREENING_GOODS</c> tables,
/// addressed through the same code abstraction in the engine.
/// </summary>
public class ReferenceList
{
    public int       ReferenceListId { get; set; }
    public string    ListCode        { get; set; } = "";
    public string    ListName        { get; set; } = "";
    public bool      ActiveFlag      { get; set; } = true;
    public string?   CreatedBy       { get; set; }
    public DateTime  CreatedDate     { get; set; }
    public string?   LastUpdatedBy   { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

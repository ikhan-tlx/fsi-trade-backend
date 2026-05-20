namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// READ-ONLY projection over <c>TmX_Location</c>. Slice 6 uses the
/// parent-child relationship to expand a user's "home location" into its
/// full descendant tree (so a regional manager's Transaction list includes
/// every branch below them).
///
/// Schema source: <c>D:\ICBC - Latest\ICBC_DEMO-Schema.sql</c> line 714
/// (<c>CREATE TABLE [dbo].[TmX_Location]</c>).
///
/// Hierarchy is small (&lt;500 rows in this deployment) so we walk it in
/// memory in <c>LocationHierarchyService</c>. If the size ever crosses
/// ~10k rows, swap the impl to a recursive CTE — the interface
/// <c>ILocationHierarchyService</c> insulates the handlers.
/// </summary>
public class Location
{
    public int       Id                 { get; set; }   // Location_ID
    public int?      ParentLocationId   { get; set; }
    public int       TenantId           { get; set; }
    public string    LocationCode       { get; set; } = "";
    public string?   LocationName       { get; set; }
    public bool      ActiveFlag         { get; set; }
    public DateTime  EffectiveStartDate { get; set; }
    public DateTime  EffectiveEndDate   { get; set; }
    public int       LocationTypeLkpId  { get; set; }
}

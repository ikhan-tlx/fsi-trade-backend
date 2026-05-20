namespace FSI.Trade.Compliance.Domain.Entities.Workflow;

/// <summary>
/// READ-ONLY projection over OptimaJet's <c>WorkflowProcessScheme</c> table.
/// One row per registered workflow scheme; the XML lives in <see cref="Scheme"/>.
///
/// OWNERSHIP: OptimaJet's runtime writes to this table (via the
/// <c>DesignerAPIAsync</c> or scheme registration flow). Our backend only
/// READS from it — never INSERTs / UPDATEs / DELETEs. Hence no audit fields
/// and no write-side handlers.
///
/// v21 NOTES: column is named <c>SchemeCode</c> (not <c>Code</c> as v3.x
/// inferred). If the live schema differs, adjust the EF configuration
/// <c>HasColumnName(...)</c> mapping — the entity property stays.
/// </summary>
public class WorkflowProcessScheme
{
    public Guid    Id                     { get; set; }
    public string  SchemeCode             { get; set; } = "";
    public string? Scheme                 { get; set; }          // ntext / nvarchar(max) — the XML
    public string? AllowedActivities      { get; set; }
    public string? StartingTransition     { get; set; }
    public string? DefiningParameters     { get; set; }
    public bool    IsObsolete             { get; set; }
    public Guid?   RootSchemeId           { get; set; }
    public string? RootSchemeCode         { get; set; }
    public string? DefiningParametersHash { get; set; }
}

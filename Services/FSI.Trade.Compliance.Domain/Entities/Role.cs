namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Role. Slice 2 (Step 3) expands the projection
/// from name-only to the full schema so we can support role CRUD via the
/// admin UI.
///
/// Active_Flag is read-only-honoured for now (see BACKLOG: "Active_Flag
/// handling on RBAC tables") — the new backend doesn't filter on it, but
/// admin Activate/Deactivate writes flip it correctly so legacy consumers
/// keep working.
/// </summary>
public class Role
{
    public int      Id                 { get; set; }
    public int      TenantId           { get; set; } = 1;
    public string   Name               { get; set; } = "";
    public string?  Description        { get; set; }
    public bool     IsActive           { get; set; } = true;
    public DateTime EffectiveStartDate { get; set; }
    public DateTime EffectiveEndDate   { get; set; }
    public string   CreatedBy          { get; set; } = "";
    public DateTime CreatedDate        { get; set; }
    public string?  LastUpdatedBy      { get; set; }
    public DateTime? LastUpdatedDate   { get; set; }
}

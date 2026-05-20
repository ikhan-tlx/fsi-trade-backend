namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Role_Privilege_Mapping. Junction table wiring
/// roles to the privileges they grant. Read by <c>IPrivilegeService</c> on
/// every authenticated request that hits a <c>[RequiresPrivilege("...")]</c>
/// action; written by <c>UpdateRolePrivilegesCommandHandler</c>.
///
/// Audit columns (Effective*, Created_*, Last_Updated_*) are NOT NULL in the
/// schema except the last two — every INSERT must populate the rest.
///
/// Active_Flag is currently ignored on read (per FSI direction, May 2026)
/// — every row is treated as live until the lifecycle semantics are formalised.
/// Tracked in BACKLOG.md.
/// </summary>
public class RolePrivilegeMapping
{
    public int       Id                 { get; set; }
    public int       TenantId           { get; set; } = 1;
    public int       RoleId             { get; set; }
    public int       PrivilegeId        { get; set; }
    public bool      IsActive           { get; set; } = true;
    public DateTime  EffectiveStartDate { get; set; }
    public DateTime  EffectiveEndDate   { get; set; }
    public string    CreatedBy          { get; set; } = "";
    public DateTime  CreatedDate        { get; set; }
    public string?   LastUpdatedBy      { get; set; }
    public DateTime? LastUpdatedDate    { get; set; }
}

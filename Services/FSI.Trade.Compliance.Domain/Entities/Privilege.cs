namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Privilege. Each row defines a privilege code
/// (Privilege_Name) referenced by name from <c>[RequiresPrivilege("...")]</c>
/// attributes on API actions.
///
/// Schema notes (from live DB inventory, May 2026):
///   • Privilege_Name        is nvarchar(100), nullable in schema (we treat as required in practice).
///   • Privilege_Description is nvarchar(100), nullable.
///   • Last_Updated_By       is NOT NULL — every INSERT must populate it (unusual quirk).
///   • Tenant_ID             defaults to 1 in the new backend until multi-tenant is needed.
/// </summary>
public class Privilege
{
    public int     Id          { get; set; }
    public int     TenantId    { get; set; } = 1;
    public string? Name        { get; set; }
    public string? Description { get; set; }
}

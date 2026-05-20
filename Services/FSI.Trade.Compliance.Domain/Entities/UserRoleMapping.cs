namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_User_Role_Mapping (junction between TmX_User and TmX_Role).
/// Slice 1 reads only role names for the JWT; Slice 2 (Step 4) writes during user
/// create/update — diff additions/removals against the requested role set.
///
/// Schema notes (from live DB inventory):
///   • Tenant_Id, User_Id, Role_Id, User_Role_Mapping_Id, Effective_Start_Date,
///     Active_Flag, Created_By, Created_Date are NOT NULL.
///   • Effective_End_Date, Last_Updated_By, Last_Updated_Date are nullable.
///   • User_Id column is nvarchar(50) but TmX_User.User_ID is nvarchar(100) —
///     known type mismatch, FSI-deferred. Active_Flag is currently ignored on read.
/// </summary>
public class UserRoleMapping
{
    public int       Id                 { get; set; }
    public int       TenantId           { get; set; } = 1;
    public string    UserId             { get; set; } = "";
    public int       RoleId             { get; set; }
    public bool      IsActive           { get; set; } = true;
    public DateTime  EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
    public string    CreatedBy          { get; set; } = "";
    public DateTime  CreatedDate        { get; set; }
    public string?   LastUpdatedBy      { get; set; }
    public DateTime? LastUpdatedDate    { get; set; }
}

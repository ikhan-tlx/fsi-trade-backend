namespace FSI.Trade.Compliance.Application.Features.Privileges.ListEntities;

/// <summary>
/// One row of the privilege catalog grouped by "entity" — the part before the
/// dot in <c>Privilege_Name</c> (e.g. "Users" in "Users.View"). Privileges
/// without a dot are grouped under <see cref="UngroupedEntityName"/>.
/// </summary>
public class PrivilegeEntityDto
{
    public const string UngroupedEntityName = "Common";

    public string                     entity     { get; set; } = "";
    public List<PrivilegeRowDto>      privileges { get; set; } = new();
}

public class PrivilegeRowDto
{
    public int     privilegeId  { get; set; }
    public string  code         { get; set; } = "";
    public string? description  { get; set; }
}

namespace FSI.Trade.Compliance.Application.Features.Roles.Get;

public class RoleDetailDto
{
    public int       roleId             { get; set; }
    public int       tenantId           { get; set; }
    public string    roleName           { get; set; } = "";
    public string?   roleDescription    { get; set; }
    public bool      isActive           { get; set; }
    public DateTime  effectiveStartDate { get; set; }
    public DateTime  effectiveEndDate   { get; set; }
    public DateTime  createdDate        { get; set; }
    public string?   createdBy          { get; set; }
    public DateTime? lastUpdatedDate    { get; set; }
    public string?   lastUpdatedBy      { get; set; }
    public int       userCount          { get; set; }
}

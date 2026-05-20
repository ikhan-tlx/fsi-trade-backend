namespace FSI.Trade.Compliance.Application.Features.Roles.List;

public class RoleListItemDto
{
    public int       roleId          { get; set; }
    public string    roleName        { get; set; } = "";
    public string?   roleDescription { get; set; }
    public bool      isActive        { get; set; }
    public DateTime  createdDate     { get; set; }
    public string?   createdBy       { get; set; }
    public DateTime? lastUpdatedDate { get; set; }
    public string?   lastUpdatedBy   { get; set; }

    /// <summary>How many users are currently mapped to this role.</summary>
    public int       userCount       { get; set; }
}

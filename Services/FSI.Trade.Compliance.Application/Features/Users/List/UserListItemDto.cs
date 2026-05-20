namespace FSI.Trade.Compliance.Application.Features.Users.List;

public class UserListItemDto
{
    public string    userId          { get; set; } = "";
    public string?   userName        { get; set; }
    public string?   emailAddress    { get; set; }
    public string?   firstName       { get; set; }
    public string?   middleName      { get; set; }
    public string?   lastName        { get; set; }
    public string?   phoneNumber     { get; set; }
    public string?   status          { get; set; }
    public bool      isActive        { get; set; }
    public bool      isLockedOut     { get; set; }
    public DateTime? lastLoginDate   { get; set; }
    public DateTime  createdDate     { get; set; }
    public string?   createdBy       { get; set; }

    public List<UserRoleRefDto> roles { get; set; } = new();
}

public class UserRoleRefDto
{
    public int    roleId   { get; set; }
    public string roleName { get; set; } = "";
}

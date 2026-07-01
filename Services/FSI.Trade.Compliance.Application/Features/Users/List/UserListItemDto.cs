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
    /// <summary>Derived single badge: Active | Inactive | Locked | PasswordExpired.</summary>
    public string?   accountStatus   { get; set; }
    public bool      isActive        { get; set; }
    public bool      isLockedOut     { get; set; }
    public DateTime? lockoutEndDateUtc   { get; set; }
    public string?   lockoutReason       { get; set; }
    public int       accessFailedCount   { get; set; }
    public DateTime? lastLoginDate       { get; set; }
    public DateTime? lastFailedLoginDate { get; set; }
    public DateTime? passwordExpiryDate  { get; set; }
    public bool      mustChangePassword  { get; set; }
    public DateTime  createdDate     { get; set; }
    public string?   createdBy       { get; set; }

    // Slice 14 — the user's (first active) mapped branch, for the grid's
    // Branch Name column. Resolved via TmX_Company_Branch_Users_Mapping.
    public int?      branchId        { get; set; }
    public string?   branchName      { get; set; }

    public List<UserRoleRefDto> roles { get; set; } = new();
}

public class UserRoleRefDto
{
    public int    roleId   { get; set; }
    public string roleName { get; set; } = "";
}

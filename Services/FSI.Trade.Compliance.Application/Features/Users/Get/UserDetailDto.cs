using FSI.Trade.Compliance.Application.Features.Users.List;

namespace FSI.Trade.Compliance.Application.Features.Users.Get;

public class UserDetailDto
{
    public string    userId             { get; set; } = "";
    public string?   userName           { get; set; }
    public string?   emailAddress       { get; set; }
    public string?   firstName          { get; set; }
    public string?   middleName         { get; set; }
    public string?   lastName           { get; set; }
    public string?   phoneNumber        { get; set; }
    public string?   imageUrl           { get; set; }
    public int       tenantId           { get; set; }
    public int?      locationId         { get; set; }
    public int?      userTypeLkpId      { get; set; }
    public int?      designationLkpId   { get; set; }
    public string?   status             { get; set; }
    public bool      isActive           { get; set; }
    public bool      isLockedOut        { get; set; }
    public bool      twoFactorEnabled   { get; set; }
    public bool      firstPasswordChange{ get; set; }
    public DateTime? passwordExpiryDate { get; set; }
    public DateTime? lastLoginDate      { get; set; }
    public DateTime  effectiveStartDate { get; set; }
    public DateTime  effectiveEndDate   { get; set; }
    public DateTime  createdDate        { get; set; }
    public string?   createdBy          { get; set; }
    public DateTime? lastUpdatedDate    { get; set; }
    public string?   lastUpdatedBy      { get; set; }

    public List<UserRoleRefDto> roles { get; set; } = new();
}

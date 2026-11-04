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
    /// <summary>Derived single badge: Active | Inactive | Locked | PasswordExpired.</summary>
    public string?   accountStatus      { get; set; }
    public bool      isActive           { get; set; }
    public bool      isLockedOut        { get; set; }
    public DateTime? lockoutEndDateUtc  { get; set; }
    public string?   lockoutReason      { get; set; }
    public int       accessFailedCount  { get; set; }
    public DateTime? lockedAt           { get; set; }
    public string?   lockedBy           { get; set; }
    public DateTime? unlockedAt         { get; set; }
    public string?   unlockedBy         { get; set; }
    public DateTime? deactivatedAt      { get; set; }
    public string?   deactivatedBy      { get; set; }
    public bool      twoFactorEnabled   { get; set; }
    /// <summary>True when 2FA is enforced org-wide (TwoFactor.Enabled). While enforced,
    /// a user can't self-disable their 2FA — only an admin "Reset 2FA" clears it. The FE
    /// uses this to hide the self-service "Turn off" affordance on the profile page.</summary>
    public bool      twoFactorEnforced  { get; set; }
    public bool      firstPasswordChange{ get; set; }
    public bool      mustChangePassword { get; set; }
    public DateTime? passwordExpiryDate { get; set; }
    public DateTime? lastLoginDate      { get; set; }
    public DateTime? lastFailedLoginDate{ get; set; }
    public DateTime  effectiveStartDate { get; set; }
    public DateTime  effectiveEndDate   { get; set; }
    public DateTime  createdDate        { get; set; }
    public string?   createdBy          { get; set; }
    public DateTime? lastUpdatedDate    { get; set; }
    public string?   lastUpdatedBy      { get; set; }

    public List<UserRoleRefDto> roles { get; set; } = new();

    /// <summary>
    /// Current company-branch assignments (legacy "Company &amp; Branch
    /// Association"), so the edit form can prefill the grid.
    /// </summary>
    public List<UserBranchRefDto> companyBranchMappings { get; set; } = new();
}

public class UserBranchRefDto
{
    public int      companyBranchId    { get; set; }
    public string?  branchCode         { get; set; }
    public string?  branchName         { get; set; }
    public int?     companyId          { get; set; }
    public string?  reportingBossId    { get; set; }
    public string?  visibleValue       { get; set; }
    public DateTime effectiveStartDate { get; set; }
    public DateTime effectiveEndDate   { get; set; }
    public bool     isActive           { get; set; }
}

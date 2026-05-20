namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// User aggregate. Maps 1:1 to ICBC_DEMO.dbo.TmX_User. Does NOT inherit from IdentityUser
/// — Identity wraps this POCO via the custom TmxUserStore in Infrastructure.
/// </summary>
public class ApplicationUser
{
    // Identity keys
    public string Id { get; set; } = default!;
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

    // Profile
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? ImageURL { get; set; }
    public int TenantId { get; set; }
    public int? LocationId { get; set; }
    public int? UserTypeLkpId { get; set; }
    public int? DesignationLkpId { get; set; }

    // Auth columns added by 2026_05_001
    public string? PasswordHash { get; set; }
    public string? SecurityStamp { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorAuthenticatorKey { get; set; }
    public DateTime? LockoutEndDateUtc { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }
    public string? Status { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public DateTime? PasswordExpiryDate { get; set; }
    public bool FirstPasswordChange { get; set; }

    // Lifecycle
    public bool ActiveFlag { get; set; }
    public DateTime EffectiveStartDate { get; set; }
    public DateTime EffectiveEndDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? LastUpdatedBy { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

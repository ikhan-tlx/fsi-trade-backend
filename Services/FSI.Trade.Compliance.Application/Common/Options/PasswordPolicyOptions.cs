namespace FSI.Trade.Compliance.Application.Common.Options;

public class PasswordPolicyOptions
{
    public const string SectionName = "PasswordPolicy";

    /// <summary>
    /// If true, login is blocked when <c>user.FirstPasswordChange = true</c>.
    /// User must reset before logging in. Recovery flow (anonymous reset
    /// endpoint) lands in slice 2 — keep this in sync with that work.
    /// </summary>
    public bool EnforceFirstPasswordChange { get; set; } = true;

    /// <summary>
    /// If true, login is blocked when <c>LastLoginDate &lt; now - DormancyDays</c>.
    /// Default off (matches the recent IdentityService change).
    /// </summary>
    public bool EnforceDormancy            { get; set; } = false;
    public int  DormancyDays               { get; set; } = 90;

    /// <summary>
    /// If true, login is blocked when <c>PasswordExpiryDate</c> has passed
    /// (unless the user has a role listed in <see cref="DisableExpiryForRoles"/>).
    /// Recovery flow (anonymous reset endpoint) lands in slice 2.
    /// </summary>
    public bool EnforceExpiry              { get; set; } = true;
    public int  ExpiryDays                 { get; set; } = 90;
    public List<string> DisableExpiryForRoles { get; set; } = new();

    public int  MaxFailedAttempts          { get; set; } = 5;
    public int  LockoutMinutes             { get; set; } = 3;
}

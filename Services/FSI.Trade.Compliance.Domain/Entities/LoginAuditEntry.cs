namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.TmX_Login_Audit. Append-only event log.
/// </summary>
public class LoginAuditEntry
{
    public long      AuditId         { get; set; }
    public string?   UserId          { get; set; }
    public string?   UsernameAttempt { get; set; }
    public string?   DeviceId        { get; set; }
    public string?   IpAddress       { get; set; }
    public string?   UserAgent       { get; set; }
    public string    Action          { get; set; } = "";
    public string    Result          { get; set; } = "";
    public string?   Detail          { get; set; }
    public DateTime  CreatedAt       { get; set; }
}

/// <summary>Stable string codes for <see cref="LoginAuditEntry.Action"/>.</summary>
public static class LoginAuditAction
{
    public const string Login                = "Login";
    public const string LoginFailed          = "LoginFailed";
    public const string Logout               = "Logout";
    public const string Refresh              = "Refresh";
    public const string RefreshChainRevoked  = "RefreshChainRevoked";
    public const string PasswordChanged      = "PasswordChanged";
    public const string PasswordResetExpired = "PasswordResetExpired";
    public const string TwoFactorEnabled     = "TwoFactorEnabled";
    public const string TwoFactorDisabled    = "TwoFactorDisabled";
    public const string DeviceRevoked        = "DeviceRevoked";
}

/// <summary>Stable string codes for <see cref="LoginAuditEntry.Result"/>.</summary>
public static class LoginAuditResult
{
    public const string Success         = "Success";
    public const string BadCredentials  = "BadCredentials";
    public const string AccountLocked   = "AccountLocked";
    public const string AccountInactive = "AccountInactive";
    public const string PasswordExpired = "PasswordExpired";
    public const string FirstChange     = "FirstChange";
    public const string Dormant         = "Dormant";
    public const string OtpRequired     = "OtpRequired";
    public const string OtpInvalid      = "OtpInvalid";
    public const string Forbidden       = "Forbidden";
    public const string Error           = "Error";
}

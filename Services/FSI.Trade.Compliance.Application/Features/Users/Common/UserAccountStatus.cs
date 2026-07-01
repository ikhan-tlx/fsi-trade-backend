namespace FSI.Trade.Compliance.Application.Features.Users.Common;

/// <summary>
/// Single, derived account status for admin display. Status is NOT stored as a
/// second column alongside Active_Flag — it is computed from the orthogonal
/// facts (enabled? locked? password expired?) with a fixed priority.
/// </summary>
public static class UserAccountStatus
{
    public const string Active          = "Active";
    public const string Inactive        = "Inactive";
    public const string Locked          = "Locked";
    public const string PasswordExpired = "PasswordExpired";

    public static string Derive(
        bool isActive,
        DateTime? lockoutEndUtc,
        DateTime? passwordExpiryUtc,
        DateTime now)
    {
        if (!isActive) return Inactive;
        if (lockoutEndUtc.HasValue && lockoutEndUtc.Value > now) return Locked;
        if (passwordExpiryUtc.HasValue && passwordExpiryUtc.Value <= now) return PasswordExpired;
        return Active;
    }
}

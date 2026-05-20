namespace FSI.Trade.Compliance.Domain.Enums;

/// <summary>
/// String values stored in TmX_User.Status. Kept as constants because the column is NVARCHAR.
/// </summary>
public static class UserStatus
{
    public const string Active   = "Active";
    public const string InActive = "InActive";
}

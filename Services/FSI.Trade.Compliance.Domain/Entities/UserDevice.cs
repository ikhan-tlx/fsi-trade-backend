namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// Maps to ICBC_DEMO.dbo.UserDevices (NOT the legacy TmX_User_Device which has
/// a different schema and stays untouched). One row per (user, device).
/// Server-issued <see cref="DeviceId"/> — FE never invents one.
/// </summary>
public class UserDevice
{
    public string    DeviceId      { get; set; } = "";
    public string    UserId        { get; set; } = "";
    public string?   Label         { get; set; }
    public string?   UserAgent     { get; set; }
    public DateTime  FirstSeenAt   { get; set; }
    public DateTime  LastSeenAt    { get; set; }
    public string?   FirstSeenIp   { get; set; }
    public string?   LastSeenIp    { get; set; }
    public bool      IsTrusted     { get; set; }
    public DateTime? RevokedAt     { get; set; }
    public string?   RevokeReason  { get; set; }

    public bool IsActive => RevokedAt is null;
}

namespace FSI.Trade.Compliance.Domain.Entities;

/// <summary>
/// JWT refresh-token row. Maps 1:1 to ICBC_DEMO.dbo.RefreshTokens.
/// Each token is bound to a device (Device_ID column added by 2026_05_004).
/// </summary>
public class RefreshToken
{
    public string    Id            { get; set; } = default!;
    public string    UserId        { get; set; } = default!;
    public string?   DeviceId      { get; set; }
    public DateTime  IssuedAt      { get; set; }
    public DateTime  ExpiresAt     { get; set; }
    public DateTime? RevokedAt     { get; set; }
    public string?   ReplacedBy    { get; set; }
    public string?   RevokeReason  { get; set; }
    public string?   CreatedByIp   { get; set; }
    public string?   RevokedByIp   { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}

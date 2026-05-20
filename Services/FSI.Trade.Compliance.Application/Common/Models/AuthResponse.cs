namespace FSI.Trade.Compliance.Application.Common.Models;

/// <summary>
/// The "data" payload of a successful auth response. Tokens + DeviceId are
/// inside data (camelCase via ASP.NET Core 8's default JSON casing) so the
/// response is a single envelope: { status, data: { ... } }.
/// </summary>
public class AuthResponse
{
    public int     Success      { get; set; }
    public string? AppLink      { get; set; }
    public string? AppVersion   { get; set; }
    public string? UserId       { get; set; }
    public string? UserName     { get; set; }
    public bool    IsFirstLogin { get; set; }
    public string? Message      { get; set; }

    public string? AccessToken  { get; set; }
    public string? RefreshToken { get; set; }
    public int?    ExpiresIn    { get; set; }

    /// <summary>
    /// Server-issued device identifier the FE must persist and send back as
    /// <c>X-Device-Id</c> on every subsequent request. Returned on Login;
    /// also returned on Refresh so the FE can repair localStorage if needed.
    /// </summary>
    public string? DeviceId     { get; set; }
}

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

    // ---- 2FA challenge (returned with Success=0 when login must complete a
    //      second factor before a token is issued) --------------------------
    /// <summary>Stable challenge/error code the FE branches on, e.g.
    /// <c>otp_required</c> or <c>totp_setup_required</c>.</summary>
    public string? Code            { get; set; }
    /// <summary>Active 2FA method for this challenge: <c>Totp</c> or <c>Email</c>.</summary>
    public string? Method          { get; set; }
    /// <summary>otpauth:// URI for the FE to render as a QR code (TOTP forced setup).</summary>
    public string? ProvisioningUri { get; set; }
    /// <summary>Base32 shared secret shown for manual authenticator entry (TOTP setup).</summary>
    public string? Secret          { get; set; }
    /// <summary>Masked destination address the Email OTP was sent to (e.g. j***@bank.com).</summary>
    public string? MaskedEmail     { get; set; }

    /// <summary>
    /// Server-issued device identifier the FE must persist and send back as
    /// <c>X-Device-Id</c> on every subsequent request. Returned on Login;
    /// also returned on Refresh so the FE can repair localStorage if needed.
    /// </summary>
    public string? DeviceId     { get; set; }
}

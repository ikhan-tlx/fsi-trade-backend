namespace FSI.Trade.Compliance.Application.Common.Options;

public class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    /// <summary>Global on/off for the login-time OTP gate. When false, login skips the OTP check entirely.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Verification method. Slice 1 supports only "Totp".</summary>
    public string Method { get; set; } = "Totp";

    /// <summary>The label that appears in the authenticator app (e.g. "FSI Trade Compliance").</summary>
    public string Issuer { get; set; } = "FSI.Trade.Compliance";
}

namespace FSI.Trade.Compliance.Application.Common.Options;

public class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    /// <summary>Global on/off for the login-time 2FA gate. When false, login skips it entirely.
    /// Config key: <c>TwoFactor.Enabled</c>.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Verification method: <c>Totp</c> | <c>Email</c> | <c>None</c>. Config key:
    /// <c>TwoFactor.Method</c>. (SMS is reserved/allowed in config but not implemented.)</summary>
    public string Method { get; set; } = "Totp";

    /// <summary>The label that appears in the authenticator app (e.g. "FSI Trade Compliance").</summary>
    public string Issuer { get; set; } = "FSI.Trade.Compliance";

    /// <summary>Email-OTP tuning (config keys <c>TwoFactor.Email.*</c>).</summary>
    public EmailOtpOptions Email { get; set; } = new();
}

public class EmailOtpOptions
{
    /// <summary>Number of digits in the emailed OTP. Config key: <c>TwoFactor.Email.OtpLength</c>.</summary>
    public int OtpLength { get; set; } = 6;
    /// <summary>Validity window in minutes. Config key: <c>TwoFactor.Email.OtpExpiryMinutes</c>.</summary>
    public int OtpExpiryMinutes { get; set; } = 1;
    /// <summary>Wrong-code attempts before the code is locked and a new one must be sent.
    /// Config key: <c>TwoFactor.Email.MaxAttempts</c>.</summary>
    public int MaxAttempts { get; set; } = 5;
}

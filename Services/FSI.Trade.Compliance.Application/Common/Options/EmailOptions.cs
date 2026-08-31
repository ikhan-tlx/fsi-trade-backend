namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// SMTP email delivery tunables. Bound from the <c>Email</c> section via
/// <c>IOptionsMonitor</c>; the operational values are managed in Configuration
/// Management (Email / SMTP) and hot-reload via the DB→IConfiguration bridge.
/// The SMTP <b>password</b> is NOT here — it is the sensitive, encrypted
/// <c>Email.Password</c> config key, read at point of use via
/// <c>ISecretConfigReader</c>. OFF by default (<see cref="Enabled"/> = false) and
/// a no-op until an operator sets Host + Username + the encrypted password and
/// enables it. Only notifications at or above <see cref="MinSeverity"/> are emailed.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public bool   Enabled     { get; set; } = false;

    public string Host        { get; set; } = "";
    public int    Port        { get; set; } = 25;
    public string Username    { get; set; } = "";
    // NOTE: the SMTP password is NOT here — it's a sensitive Configuration
    // Management key (Email.Password), stored encrypted and read at point of use
    // via ISecretConfigReader (sensitive keys are not bridged into IConfiguration).
    public string From        { get; set; } = "";
    public bool   UseSsl      { get; set; } = true;

    /// <summary>Only email notifications whose severity is at or above this level.</summary>
    public string MinSeverity { get; set; } = "critical";
}

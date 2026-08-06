namespace FSI.Trade.Compliance.Application.Common.Options;

/// <summary>
/// SMTP email delivery tunables. Bound from the <c>Email</c> section in
/// appsettings.json via <c>IOptionsMonitor</c> so credentials/host edits
/// hot-reload without a restart. OFF by default (<see cref="Enabled"/> = false):
/// until an operator supplies SMTP creds and flips it on, the notification email
/// channel is a no-op and there is zero behaviour change. Only notifications at or
/// above <see cref="MinSeverity"/> are emailed.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public bool   Enabled     { get; set; } = false;

    /// <summary>
    /// Delivery transport: <c>"Smtp"</c> (default — send over the network via Host/Port/creds)
    /// or <c>"PickupDirectory"</c> (write a <c>.eml</c> file to <see cref="PickupDirectory"/> with
    /// no SMTP server, no credentials and no network). Case-insensitive.
    /// </summary>
    public string DeliveryMode { get; set; } = "Smtp";

    /// <summary>
    /// Folder that receives generated <c>.eml</c> files when <see cref="DeliveryMode"/> is
    /// <c>"PickupDirectory"</c>. Created automatically if missing. Ignored in Smtp mode.
    /// </summary>
    public string PickupDirectory { get; set; } = "";

    public string Host        { get; set; } = "";
    public int    Port        { get; set; } = 25;
    public string Username    { get; set; } = "";
    public string Password    { get; set; } = "";
    public string From        { get; set; } = "";
    public bool   UseSsl      { get; set; } = true;

    /// <summary>Only email notifications whose severity is at or above this level.</summary>
    public string MinSeverity { get; set; } = "critical";
}

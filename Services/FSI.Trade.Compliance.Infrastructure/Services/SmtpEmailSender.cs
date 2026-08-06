using System.IO;
using System.Net;
using System.Net.Mail;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Services;

/// <summary>
/// <see cref="IEmailSender"/> over the BCL <see cref="System.Net.Mail.SmtpClient"/>
/// (no third-party NuGet dependency). Reads <see cref="EmailOptions"/> via
/// <c>IOptionsMonitor</c> so an operator can toggle/reconfigure SMTP at runtime.
/// Fully best-effort: a no-op when disabled/unconfigured, and any send failure is
/// logged and swallowed — this method NEVER throws so it can be called safely from
/// inside the notification emitter's post-commit path.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<SmtpEmailSender>       _logger;

    public SmtpEmailSender(IOptionsMonitor<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger  = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;

        // No-op when disabled or no recipient. NOTE: Host is only required in Smtp
        // mode — pickup-directory mode writes a local .eml with no host/network.
        if (!opts.Enabled || string.IsNullOrWhiteSpace(toEmail))
            return;

        var isPickup = string.Equals(opts.DeliveryMode, "PickupDirectory", StringComparison.OrdinalIgnoreCase);

        // Smtp mode still requires a configured host — stay a no-op otherwise.
        if (!isPickup && string.IsNullOrWhiteSpace(opts.Host))
            return;

        try
        {
            // A valid From is required to build a well-formed message (pickup mode too).
            var from = string.IsNullOrWhiteSpace(opts.From) ? "notifications@icbc.local" : opts.From;

            using var msg = new MailMessage(from, toEmail)
            {
                Subject    = subject,
                Body       = body,
                IsBodyHtml = false,
            };

            if (isPickup)
            {
                // Pickup-directory delivery: no SMTP server, credentials or network —
                // System.Net.Mail serialises the message to a .eml file in the folder.
                if (string.IsNullOrWhiteSpace(opts.PickupDirectory))
                {
                    _logger.LogWarning(
                        "Email DeliveryMode=PickupDirectory but PickupDirectory is blank — skipping send to {ToEmail}.",
                        toEmail);
                    return;
                }

                Directory.CreateDirectory(opts.PickupDirectory);

                using var pickupClient = new SmtpClient
                {
                    DeliveryMethod          = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                    PickupDirectoryLocation = opts.PickupDirectory,
                };

                await pickupClient.SendMailAsync(msg, ct);
                return;
            }

            // Default Smtp transport: network delivery via Host/Port/credentials.
            using var client = new SmtpClient(opts.Host, opts.Port)
            {
                EnableSsl   = opts.UseSsl,
                Credentials = string.IsNullOrEmpty(opts.Username)
                    ? null
                    : new NetworkCredential(opts.Username, opts.Password),
            };

            await client.SendMailAsync(msg, ct);
        }
        catch (Exception ex)
        {
            // Email is a best-effort side channel — log and swallow so a delivery
            // failure can never affect the notification/business write.
            _logger.LogError(ex, "Failed to send notification email to {ToEmail} subject={Subject}.", toEmail, subject);
        }
    }
}

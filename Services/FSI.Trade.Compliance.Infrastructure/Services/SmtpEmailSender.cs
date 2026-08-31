using System.Net;
using System.Net.Mail;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Configuration;
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
    private readonly ISecretConfigReader           _secrets;
    private readonly ILogger<SmtpEmailSender>       _logger;

    public SmtpEmailSender(
        IOptionsMonitor<EmailOptions> options,
        ISecretConfigReader           secrets,
        ILogger<SmtpEmailSender>      logger)
    {
        _options = options;
        _secrets = secrets;
        _logger  = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;

        // No-op when disabled, no recipient, or no SMTP host configured.
        if (!opts.Enabled || string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(opts.Host))
            return;

        try
        {
            // A valid From is required to build a well-formed message.
            var from = string.IsNullOrWhiteSpace(opts.From) ? "notifications@icbc.local" : opts.From;

            using var msg = new MailMessage(from, toEmail)
            {
                Subject    = subject,
                Body       = body,
                IsBodyHtml = false,
            };

            // The SMTP password is an encrypted, sensitive config value — NOT bridged
            // into IConfiguration — so read+decrypt it at point of use.
            var password = _secrets.Get("Email.Password") ?? string.Empty;

            // SMTP transport: network delivery via Host/Port/credentials.
            using var client = new SmtpClient(opts.Host, opts.Port)
            {
                EnableSsl   = opts.UseSsl,
                Credentials = string.IsNullOrEmpty(opts.Username)
                    ? null
                    : new NetworkCredential(opts.Username, password),
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

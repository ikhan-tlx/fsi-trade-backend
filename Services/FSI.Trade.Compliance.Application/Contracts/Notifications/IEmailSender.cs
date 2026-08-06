namespace FSI.Trade.Compliance.Application.Contracts.Notifications;

/// <summary>
/// Best-effort SMTP email delivery port for the notification stack. The
/// implementation is a no-op when email is disabled or misconfigured and NEVER
/// throws — an email failure must never surface to (or roll back) the business
/// write that raised the notification.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

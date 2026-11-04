using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FSI.Trade.Compliance.Application.Common.Exceptions;
using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Configuration;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Identity;

/// <summary>
/// Email 2FA OTP generation, storage (hashed, single current row per user) and
/// delivery over the configured SMTP source. Unlike the best-effort notification
/// sender, OTP delivery SURFACES failures (throws) so the login flow can tell the
/// user their code could not be sent instead of silently stranding them.
/// </summary>
public sealed class EmailOtpService : IEmailOtpService
{
    private const string Purpose = "Login";

    private readonly IApplicationDbContext          _db;
    private readonly IOptionsMonitor<TwoFactorOptions> _tfa;
    private readonly IOptionsMonitor<EmailOptions>      _email;
    private readonly ISecretConfigReader               _secrets;
    private readonly ILogger<EmailOtpService>          _logger;

    public EmailOtpService(
        IApplicationDbContext             db,
        IOptionsMonitor<TwoFactorOptions> tfa,
        IOptionsMonitor<EmailOptions>     email,
        ISecretConfigReader               secrets,
        ILogger<EmailOtpService>          logger)
    {
        _db = db; _tfa = tfa; _email = email; _secrets = secrets; _logger = logger;
    }

    public async Task<string> SendAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new AuthenticationException("email_otp_no_address",
                "No email address is on file for this account. Contact your administrator.");

        var cfg      = _tfa.CurrentValue.Email;
        var length   = Math.Clamp(cfg.OtpLength, 4, 8);
        var expiryMin = Math.Clamp(cfg.OtpExpiryMinutes, 1, 30);
        var maxAtt   = Math.Clamp(cfg.MaxAttempts, 1, 10);

        var code = GenerateNumericCode(length);
        var now  = DateTime.UtcNow;

        // Upsert the single current OTP row for this user (reset attempts + expiry).
        var row = await _db.UserOtps
            .FirstOrDefaultAsync(o => o.UserId == user.Id && o.Purpose == Purpose, ct);
        if (row is null)
        {
            row = new UserOtp { UserId = user.Id, Purpose = Purpose, CreatedDate = now };
            _db.UserOtps.Add(row);
        }
        row.OtpHash         = Sha256Hex(code);
        row.ExpiresAt       = now.AddMinutes(expiryMin);
        row.Attempts        = 0;
        row.MaxAttempts     = maxAtt;
        row.SentAt          = now;
        row.ConsumedAt      = null;
        row.LastUpdatedDate = now;
        await _db.SaveChangesAsync(ct);

        // Deliver over the configured SMTP source — surfacing failures.
        await DeliverAsync(user.Email!, code, expiryMin, ct);

        return MaskEmail(user.Email!);
    }

    public async Task<EmailOtpResult> VerifyAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        var row = await _db.UserOtps
            .FirstOrDefaultAsync(o => o.UserId == user.Id && o.Purpose == Purpose, ct);
        if (row is null || row.ConsumedAt != null) return EmailOtpResult.Missing;

        if (DateTime.UtcNow > row.ExpiresAt) return EmailOtpResult.Expired;
        if (row.Attempts >= row.MaxAttempts) return EmailOtpResult.TooManyAttempts;

        if (!FixedTimeEquals(row.OtpHash, Sha256Hex(code ?? string.Empty)))
        {
            row.Attempts++;
            row.LastUpdatedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return row.Attempts >= row.MaxAttempts ? EmailOtpResult.TooManyAttempts : EmailOtpResult.Invalid;
        }

        row.ConsumedAt      = DateTime.UtcNow;   // spent — cannot be reused
        row.LastUpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return EmailOtpResult.Success;
    }

    // ---- delivery --------------------------------------------------------
    private async Task DeliverAsync(string toEmail, string code, int expiryMin, CancellationToken ct)
    {
        var opts = _email.CurrentValue;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.Host) || string.IsNullOrWhiteSpace(opts.From))
            throw new AuthenticationException("email_otp_unavailable",
                "Email delivery is not configured. Configure the SMTP settings in Configuration Management.");

        var subject = "Your verification code";
        var body =
            $"Your one-time verification code is: {code}\r\n\r\n" +
            $"It is valid for {expiryMin} minute{(expiryMin == 1 ? "" : "s")}. " +
            "If you did not attempt to sign in, please contact your administrator.";

        try
        {
            var password = _secrets.Get("Email.Password") ?? string.Empty;
            using var msg = new MailMessage(opts.From, toEmail) { Subject = subject, Body = body, IsBodyHtml = false };
            using var client = new SmtpClient(opts.Host, opts.Port)
            {
                EnableSsl   = opts.UseSsl,
                Credentials = string.IsNullOrEmpty(opts.Username) ? null : new NetworkCredential(opts.Username, password),
            };
            await client.SendMailAsync(msg, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send 2FA OTP email to {ToEmail}.", toEmail);
            throw new AuthenticationException("email_otp_send_failed",
                "Could not send the verification code. Please try again or contact your administrator.");
        }
    }

    // ---- helpers ---------------------------------------------------------
    private static string GenerateNumericCode(int length)
    {
        // Uniform random N-digit code (leading zeros allowed). Rejection-sample
        // the top 6 byte values so 0..249 map evenly onto digits 0..9.
        var sb = new StringBuilder(length);
        Span<byte> buf = stackalloc byte[1];
        for (var i = 0; i < length; i++)
        {
            byte b;
            do { RandomNumberGenerator.Fill(buf); b = buf[0]; }
            while (b >= 250);
            sb.Append((char)('0' + (b % 10)));
        }
        return sb.ToString();
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes); // upper-case hex
    }

    private static bool FixedTimeEquals(string aHex, string bHex)
        => CryptographicOperations.FixedTimeEquals(
               Encoding.ASCII.GetBytes(aHex.ToUpperInvariant()),
               Encoding.ASCII.GetBytes(bHex.ToUpperInvariant()));

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var name = email[..at];
        var domain = email[at..];
        var head = name.Length <= 1 ? name : name[..1];
        return $"{head}***{domain}";
    }
}

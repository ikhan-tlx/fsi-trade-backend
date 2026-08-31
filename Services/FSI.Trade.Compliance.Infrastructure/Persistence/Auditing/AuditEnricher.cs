using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Auditing;

/// <summary>
/// Shared helpers for populating the compliance fields on an <see cref="AuditLog"/>
/// row (who / where / how / context) and for the tamper-evidence hash chain.
/// Used by both the EF <c>AuditSaveChangesInterceptor</c> (data mutations) and the
/// explicit <c>AuditWriter</c> (reads / login / failures).
/// </summary>
internal static class AuditEnricher
{
    /// <summary>Service/application name recorded as the processing app.</summary>
    public const string ApplicationName = "FSI.Trade.Compliance.API";

    private static readonly string MachineName = SafeMachineName();

    /// <summary>Stamp the request/identity context onto an audit row.</summary>
    public static void Enrich(AuditLog row, HttpContext? ctx, ICurrentUserService user)
    {
        row.TenantId    = user.TenantId;
        row.UserId      = user.UserId;
        row.UserName    = user.UserName;
        row.IpAddress   = user.IpAddress;
        row.Host        = MachineName;
        row.Application = ApplicationName;

        if (ctx is null) return;

        var roles = ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        row.UserRole      = roles.Length > 0 ? string.Join(", ", roles) : null;
        row.OnBehalfOf    = ctx.User.FindFirst("onBehalfOf")?.Value
                            ?? ctx.User.FindFirst("impersonatedBy")?.Value;
        row.SessionId     = FindClaim(ctx.User, "jti");
        row.CorrelationId = FirstNonEmpty(ctx.Request.Headers["X-Correlation-Id"].ToString(), ctx.TraceIdentifier);
        row.UserAgent     = Trunc(ctx.Request.Headers.UserAgent.ToString(), 512);
        row.Screen        = Empty(ctx.Request.Headers["X-Screen"].ToString());
        row.RequestPath ??= ctx.Request.Path.Value;
        row.HttpMethod  ??= ctx.Request.Method;
    }

    /// <summary>
    /// Compute the chained SHA-256 for one row from the prior row's hash and the
    /// row's canonical content, set <c>Row_Hash</c> + <c>Prev_Hash</c>, and return
    /// this row's hash so the caller can feed it forward as the next prev-hash.
    /// </summary>
    public static string ChainHash(AuditLog row, string? prevHash)
    {
        row.PrevHash = prevHash;
        row.RowHash  = ComputeHash(row, prevHash);
        return row.RowHash;
    }

    /// <summary>
    /// Pure re-computation of a row's chained hash from a given prior hash and the
    /// row's canonical content — does NOT mutate the row. Used by the integrity
    /// verifier to recompute the chain and detect tampering (a stored Row_Hash that
    /// no longer matches its recomputed value, or a broken Prev_Hash link).
    /// </summary>
    public static string ComputeHash(AuditLog row, string? prevHash)
        => Sha256Hex((prevHash ?? "") + "|" + Canonical(row));

    /// <summary>Immutable content used as the hash input (excludes the hashes + identity PK).</summary>
    private static string Canonical(AuditLog a) => string.Join("|", new[]
    {
        a.CreatedDate.ToString("O"),
        a.UserId ?? "", a.UserName ?? "", a.UserRole ?? "",
        a.Action, a.EntityType, a.EntityKey ?? "",
        a.Outcome ?? "", a.StatusCode?.ToString() ?? "",
        a.IpAddress ?? "", a.CorrelationId ?? "", a.SessionId ?? "",
        a.ChangesJson ?? "", a.Description ?? "",
    });

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? FindClaim(ClaimsPrincipal user, string shortType)
        => user.FindFirst(shortType)?.Value
           ?? user.Claims.FirstOrDefault(c => c.Type == shortType || c.Type.EndsWith("/" + shortType, StringComparison.Ordinal))?.Value;

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string? FirstNonEmpty(string? a, string? b) => !string.IsNullOrWhiteSpace(a) ? a : Empty(b);
    public static string? Trunc(string? s, int max) => s is { Length: > 0 } && s.Length > max ? s[..max] : Empty(s);

    private static string SafeMachineName()
    {
        try { return Environment.MachineName; } catch { return "unknown"; }
    }
}

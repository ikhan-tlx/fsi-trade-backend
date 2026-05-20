using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Application.Contracts.Identity;

/// <summary>
/// Append-only auth-lifecycle event log. Fire-and-forget — handlers shouldn't
/// fail because audit failed (implementations must swallow + log internally).
/// </summary>
public interface ILoginAuditService
{
    Task LogAsync(LoginAuditEntry entry, CancellationToken ct = default);
}

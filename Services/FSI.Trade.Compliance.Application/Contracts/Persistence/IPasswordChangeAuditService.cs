namespace FSI.Trade.Compliance.Application.Contracts.Persistence;

/// <summary>
/// Writes a row to Password_Change_Audit_Trail every time a user's password
/// changes. Append-only by design.
/// </summary>
public interface IPasswordChangeAuditService
{
    Task LogAsync(string userId, string? passwordHashSnapshot, string? createdBy, CancellationToken ct = default);
}

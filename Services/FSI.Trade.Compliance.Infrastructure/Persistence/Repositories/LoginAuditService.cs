using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;

public class LoginAuditService : ILoginAuditService
{
    private readonly IApplicationDbContext   _db;
    private readonly ILogger<LoginAuditService> _log;

    public LoginAuditService(IApplicationDbContext db, ILogger<LoginAuditService> log)
    {
        _db  = db;
        _log = log;
    }

    public async Task LogAsync(LoginAuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            entry.CreatedAt = DateTime.UtcNow;
            _db.LoginAudit.Add(entry);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit must never break the auth path. Swallow + log to the host log.
            _log.LogError(ex, "Failed to write login audit entry: {Action} {Result} for {User}",
                entry.Action, entry.Result, entry.UserId ?? entry.UsernameAttempt);
        }
    }
}

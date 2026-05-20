using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Repositories;

public class PasswordChangeAuditService : IPasswordChangeAuditService
{
    private readonly IApplicationDbContext _db;
    public PasswordChangeAuditService(IApplicationDbContext db) => _db = db;

    public async Task LogAsync(string userId, string? passwordHashSnapshot, string? createdBy, CancellationToken ct = default)
    {
        _db.PasswordChangeAudits.Add(new PasswordChangeAudit
        {
            UserId       = userId,
            PasswordHash = passwordHashSnapshot,
            CreatedBy    = createdBy,
            CreatedDate  = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }
}

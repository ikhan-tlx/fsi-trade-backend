using FSI.Trade.Compliance.Application.Contracts.Audit;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Auditing;

/// <summary>
/// Verifies the <c>TmX_Audit_Log</c> hash chain by recomputing it in Audit_Id order
/// (the order in which rows were written) and comparing each recomputed hash to the
/// stored <c>Row_Hash</c>. Reuses <see cref="AuditEnricher.ComputeHash"/> so the
/// canonical content + algorithm match exactly what the writer used.
/// </summary>
public sealed class AuditIntegrityVerifier : IAuditIntegrityVerifier
{
    private readonly IApplicationDbContext _db;
    public AuditIntegrityVerifier(IApplicationDbContext db) => _db = db;

    public async Task<AuditIntegrityResult> VerifyAsync(CancellationToken ct = default)
    {
        // Project only the columns that feed the canonical hash (+ stored hash + id)
        // into an anonymous shape (a safe DTO projection), then rebuild the AuditLog
        // in memory for the pure hash recomputation.
        var rows = await _db.AuditLogs.AsNoTracking()
            .OrderBy(a => a.AuditId)
            .Select(a => new
            {
                a.AuditId, a.CreatedDate, a.UserId, a.UserName, a.UserRole,
                a.Action, a.EntityType, a.EntityKey, a.Outcome, a.StatusCode,
                a.IpAddress, a.CorrelationId, a.SessionId, a.ChangesJson, a.Description,
                a.RowHash,
            })
            .ToListAsync(ct);

        string? prev = null;
        long checkedCount = 0;
        foreach (var a in rows)
        {
            checkedCount++;
            var row = new AuditLog
            {
                AuditId       = a.AuditId,
                CreatedDate   = a.CreatedDate,
                UserId        = a.UserId,
                UserName      = a.UserName,
                UserRole      = a.UserRole,
                Action        = a.Action,
                EntityType    = a.EntityType,
                EntityKey     = a.EntityKey,
                Outcome       = a.Outcome,
                StatusCode    = a.StatusCode,
                IpAddress     = a.IpAddress,
                CorrelationId = a.CorrelationId,
                SessionId     = a.SessionId,
                ChangesJson   = a.ChangesJson,
                Description   = a.Description,
            };
            var expected = AuditEnricher.ComputeHash(row, prev);
            if (!string.Equals(expected, a.RowHash, StringComparison.OrdinalIgnoreCase))
            {
                return new AuditIntegrityResult
                {
                    Intact             = false,
                    RowsChecked        = checkedCount,
                    FirstBrokenAuditId = a.AuditId,
                };
            }
            prev = a.RowHash;   // chain forward on the STORED hash
        }

        return new AuditIntegrityResult
        {
            Intact             = true,
            RowsChecked        = checkedCount,
            FirstBrokenAuditId = null,
        };
    }
}

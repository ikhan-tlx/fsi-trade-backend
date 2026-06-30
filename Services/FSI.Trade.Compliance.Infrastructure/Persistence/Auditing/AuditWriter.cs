using FSI.Trade.Compliance.Application.Contracts.Audit;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Auditing;

/// <summary>
/// Writes explicit (non-mutation) audit rows — READ / LOGIN / EXPORT / FAILURE —
/// reusing the shared <see cref="AuditEnricher"/> context + hash chain so these
/// rows are indistinguishable in shape from interceptor-written mutation rows.
/// Best-effort: never throws into the request path.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService   _user;
    private readonly IHttpContextAccessor  _http;
    private readonly ILogger<AuditWriter>  _log;

    public AuditWriter(
        IApplicationDbContext db,
        ICurrentUserService user,
        IHttpContextAccessor http,
        ILogger<AuditWriter> log)
    {
        _db   = db;
        _user = user;
        _http = http;
        _log  = log;
    }

    public async Task WriteAsync(AuditEvent evt, CancellationToken ct = default)
    {
        try
        {
            var row = new AuditLog
            {
                Action        = evt.Action,
                EntityType    = evt.EntityType,
                EntityKey     = evt.EntityKey,
                ChangesJson   = evt.ChangesJson,
                Description   = evt.Description,
                Outcome       = evt.Outcome,
                StatusCode    = evt.StatusCode,
                FailureReason = AuditEnricher.Trunc(evt.FailureReason, 1000),
                CreatedDate   = DateTime.UtcNow,
            };
            AuditEnricher.Enrich(row, _http.HttpContext, _user);

            var prevHash = await _db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.AuditId).Select(a => a.RowHash).FirstOrDefaultAsync(ct);
            AuditEnricher.ChainHash(row, prevHash);

            _db.AuditLogs.Add(row);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit is best-effort; swallow so a logging failure never breaks the action.
            _log.LogWarning(ex, "Audit write failed for {Action} {EntityType}", evt.Action, evt.EntityType);
        }
    }
}

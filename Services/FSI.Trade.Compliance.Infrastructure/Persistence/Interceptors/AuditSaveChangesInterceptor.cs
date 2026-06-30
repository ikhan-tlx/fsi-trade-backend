using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Identity;
using FSI.Trade.Compliance.Domain.Entities;
using FSI.Trade.Compliance.Infrastructure.Persistence.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FSI.Trade.Compliance.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Generic audit interceptor: for every SaveChanges, records one TmX_Audit_Log
/// row per changed entity — who (user / role / IP / session), what (action verb +
/// entity + key + field old->new + description), where (screen / endpoint / host /
/// app) and how/when (method / status / outcome / correlation / user-agent), plus a
/// tamper-evidence SHA-256 hash chained to the prior row. Two-phase so
/// store-generated keys are known: capture in SavingChanges, persist in
/// SavedChanges (a second save containing only audit rows, which are skipped to
/// avoid recursion).
///
/// Scope: this captures data MUTATIONS (CREATE / UPDATE / DELETE, and the semantic
/// APPROVE / REJECT on a Request). Reads, logins, exports and failures are recorded
/// separately by the explicit <c>AuditWriter</c> + audit action filter.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _user;
    private readonly IHttpContextAccessor _http;

    // Entity types we never audit (the audit table itself + high-volume infra noise
    // like the per-request device "last seen" heartbeat and session/token churn).
    private static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        nameof(AuditLog), "WorkflowInbox", "RefreshToken", "UserSession",
        "LoginAuditEntry", "UserDevice",
    };

    private readonly List<Captured> _captured = new();
    private bool _writing;

    public AuditSaveChangesInterceptor(ICurrentUserService user, IHttpContextAccessor http)
    {
        _user = user;
        _http = http;
    }

    private sealed class Captured
    {
        public required string Action;
        public required string EntityType;
        public required string? EntityKey;
        public required string? Changes;
        public required string Description;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> r)
    { Capture(e.Context); return base.SavingChanges(e, r); }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default)
    { Capture(e.Context); return base.SavingChangesAsync(e, r, ct); }

    public override int SavedChanges(SaveChangesCompletedEventData e, int result)
    { Persist(e.Context); return base.SavedChanges(e, result); }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    { await PersistAsync(e.Context, ct); return await base.SavedChangesAsync(e, result, ct); }

    private void Capture(DbContext? db)
    {
        if (db is null || _writing) return;
        _captured.Clear();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            var type = entry.Entity.GetType().Name;
            if (Skip.Contains(type)) continue;

            string? newStatusCode = null;
            string? newRejectReason = null;

            var changes = new List<object>();
            foreach (var p in entry.Properties)
            {
                if (entry.State == EntityState.Modified && !p.IsModified) continue;
                object? oldV = entry.State == EntityState.Added ? null : p.OriginalValue;
                object? newV = entry.State == EntityState.Deleted ? null : p.CurrentValue;
                if (entry.State == EntityState.Modified && Equals(oldV, newV)) continue;

                if (type == "Request" && p.Metadata.Name == "RequestStatusCode")
                    newStatusCode = newV?.ToString();
                if (type == "Request" && p.Metadata.Name == "RejectReason")
                    newRejectReason = newV?.ToString();

                changes.Add(new { field = p.Metadata.Name, old = Trunc(oldV), @new = Trunc(newV) });
            }
            if (changes.Count == 0 && entry.State == EntityState.Modified) continue;

            var key   = KeyOf(entry);
            var verb  = VerbFor(entry.State, type, newStatusCode);
            var descr = Describe(verb, type, key, changes.Count, newRejectReason);

            _captured.Add(new Captured
            {
                Action      = verb,
                EntityType  = type,
                EntityKey   = key,
                Changes     = JsonSerializer.Serialize(changes),
                Description = descr,
            });
        }
    }

    private void Persist(DbContext? db)
    {
        if (db is null || _writing || _captured.Count == 0) return;
        var rows = BuildRows(db);
        _captured.Clear();
        if (rows.Count == 0) return;
        _writing = true;
        try { db.Set<AuditLog>().AddRange(rows); db.SaveChanges(); }
        finally { _writing = false; }
    }

    private async ValueTask PersistAsync(DbContext? db, CancellationToken ct)
    {
        if (db is null || _writing || _captured.Count == 0) return;
        var rows = BuildRows(db);
        _captured.Clear();
        if (rows.Count == 0) return;
        _writing = true;
        try { db.Set<AuditLog>().AddRange(rows); await db.SaveChangesAsync(ct); }
        finally { _writing = false; }
    }

    private List<AuditLog> BuildRows(DbContext db)
    {
        var ctx    = _http.HttpContext;
        var status = ctx?.Response?.StatusCode;
        var now    = DateTime.UtcNow;

        // Tip of the hash chain (most recent existing row). The new rows extend it.
        string? prevHash = db.Set<AuditLog>().AsNoTracking()
            .OrderByDescending(a => a.AuditId).Select(a => a.RowHash).FirstOrDefault();

        var list = new List<AuditLog>(_captured.Count);
        foreach (var c in _captured)
        {
            var row = new AuditLog
            {
                Action      = c.Action,
                EntityType  = c.EntityType,
                EntityKey   = c.EntityKey,
                ChangesJson = c.Changes,
                Description = c.Description,
                Outcome     = "SUCCESS",
                StatusCode  = status,
                CreatedDate = now,
            };
            AuditEnricher.Enrich(row, ctx, _user);
            prevHash = AuditEnricher.ChainHash(row, prevHash);
            list.Add(row);
        }
        return list;
    }

    private static string VerbFor(EntityState state, string type, string? newStatusCode) => state switch
    {
        EntityState.Added   => "CREATE",
        EntityState.Deleted => "DELETE",
        _ when type == "Request" && newStatusCode == "0012" => "APPROVE",
        _ when type == "Request" && newStatusCode == "0013" => "REJECT",
        _ => "UPDATE",
    };

    private static string Describe(string verb, string type, string? key, int fieldCount, string? rejectReason)
    {
        var who = key is null ? type : $"{type} #{key}";
        return verb switch
        {
            "CREATE"  => $"Created {who}",
            "DELETE"  => $"Deleted {who}",
            "APPROVE" => $"Approved {who}",
            "REJECT"  => string.IsNullOrWhiteSpace(rejectReason)
                            ? $"Rejected {who}"
                            : $"Rejected {who} — {Trunc(rejectReason)}",
            _         => $"Updated {who} ({fieldCount} field{(fieldCount == 1 ? "" : "s")} changed)",
        };
    }

    private static string? KeyOf(EntityEntry entry)
    {
        var pk = entry.Metadata.FindPrimaryKey();
        if (pk is null) return null;
        var parts = pk.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "");
        return string.Join("|", parts);
    }

    private static string? Trunc(object? v)
    {
        if (v is null) return null;
        var s = v.ToString();
        return s is { Length: > 1000 } ? s[..1000] : s;
    }
}

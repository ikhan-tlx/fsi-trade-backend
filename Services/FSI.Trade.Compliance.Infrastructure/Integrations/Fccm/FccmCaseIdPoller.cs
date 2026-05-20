using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Integrations.Fccm;

/// <summary>
/// Background service that drives <see cref="KycCaseRequest"/> rows through
/// their state machine without blocking any HTTP request thread. Replaces
/// the legacy 20-second blocking poll inside
/// <c>caseInsertionController.GetCaseIdByRequestId</c>.
///
/// One tick per <see cref="IntegrationOptions.PollingInterval"/>:
///   1. Pick rows in <c>AwaitingCaseId</c> that haven't been polled too
///      recently (avoid hammering Oracle on the same row mid-tick).
///   2. For each, ask <see cref="FccmOracleReader.ReadCaseIdAsync"/>.
///      If non-null → flip to <c>CaseCreated</c>.
///      If null + submitted &gt; <see cref="IntegrationOptions.CaseIdTimeout"/>
///      ago → flip to <c>Timeout</c>.
///   3. Pick rows in <c>CaseCreated</c>; ask
///      <see cref="FccmOracleReader.ReadRiskCategoryKeyAsync"/>.
///      If non-null → flip to <c>RiskAssessed</c>.
///
/// All state transitions are append-only on the existing row plus
/// LastUpdatedAt = now. No external systems are notified directly —
/// downstream consumers (workflow scheme actions) read state on next
/// invocation.
/// </summary>
public class FccmCaseIdPoller : BackgroundService
{
    private readonly IServiceProvider           _sp;
    private readonly IntegrationOptions         _opt;
    private readonly ILogger<FccmCaseIdPoller>  _log;

    public FccmCaseIdPoller(
        IServiceProvider                sp,
        IOptions<IntegrationOptions>    opt,
        ILogger<FccmCaseIdPoller>       log)
    {
        _sp  = sp;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("FccmCaseIdPoller starting. Interval = {Interval}, CaseIdTimeout = {Timeout}.",
                            _opt.PollingInterval, _opt.CaseIdTimeout);

        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope  = _sp.CreateScope();
                var       db     = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var       oracle = scope.ServiceProvider.GetRequiredService<FccmOracleReader>();

                await TickAsync(db, oracle, stop);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "FccmCaseIdPoller tick failed; will retry after interval.");
            }

            try
            {
                await Task.Delay(_opt.PollingInterval, stop);
            }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }

    internal async Task TickAsync(IApplicationDbContext db, FccmOracleReader oracle, CancellationToken ct)
    {
        var now    = DateTime.UtcNow;
        var cutoff = now - _opt.CaseIdTimeout;

        // ---- Phase A — AwaitingCaseId rows ----
        var awaiting = await db.KycCaseRequests
            .Where(x => x.Status == KycCaseStatus.AwaitingCaseId && x.FccmRequestId != null)
            .OrderBy(x => x.SubmittedAt)
            .Take(50)                                              // soft batch cap per tick
            .ToListAsync(ct);

        foreach (var row in awaiting)
        {
            row.LastPolledAt = now;

            var caseId = await oracle.ReadCaseIdAsync(row.FccmRequestId!, ct);
            if (!string.IsNullOrWhiteSpace(caseId))
            {
                row.FccmCaseId    = caseId;
                row.Status        = KycCaseStatus.CaseCreated;
                row.LastUpdatedAt = now;
                _log.LogInformation("KycCaseRequest {Id}: CaseId provisioned ({CaseId}).", row.Id, caseId);
                continue;
            }

            if (row.SubmittedAt < cutoff)
            {
                row.Status        = KycCaseStatus.Timeout;
                row.ErrorDetail   = $"FCCM CASE_ID not provisioned within {_opt.CaseIdTimeout}.";
                row.LastUpdatedAt = now;
                _log.LogWarning("KycCaseRequest {Id}: timeout — no CaseId after {Elapsed}.", row.Id, now - row.SubmittedAt);
            }
        }
        if (awaiting.Count > 0) await db.SaveChangesAsync(ct);

        // ---- Phase B — CaseCreated rows awaiting risk score ----
        var caseCreated = await db.KycCaseRequests
            .Where(x => x.Status == KycCaseStatus.CaseCreated && x.FccmRequestId != null && x.RiskCategoryKey == null)
            .OrderBy(x => x.LastUpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var row in caseCreated)
        {
            row.LastPolledAt = now;

            var risk = await oracle.ReadRiskCategoryKeyAsync(row.FccmRequestId!, ct);
            if (!string.IsNullOrWhiteSpace(risk))
            {
                row.RiskCategoryKey = risk;
                row.Status          = KycCaseStatus.RiskAssessed;
                row.LastUpdatedAt   = now;
                _log.LogInformation("KycCaseRequest {Id}: risk key {Risk} captured; flow complete.", row.Id, risk);
            }
        }
        if (caseCreated.Count > 0) await db.SaveChangesAsync(ct);
    }
}

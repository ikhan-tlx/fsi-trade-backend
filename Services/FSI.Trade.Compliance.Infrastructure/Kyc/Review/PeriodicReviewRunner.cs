using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Kyc;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

public class PeriodicReviewRunner : IPeriodicReviewRunner
{
    private const string Actor = "PERIODIC REVIEWER";
    private readonly IApplicationDbContext        _db;
    private readonly IKycReviewOrchestrator        _orchestrator;
    private readonly ILogger<PeriodicReviewRunner> _log;
    private readonly IOptionsMonitor<PeriodicReviewOptions> _opt;

    public PeriodicReviewRunner(IApplicationDbContext db, IKycReviewOrchestrator orchestrator,
        ILogger<PeriodicReviewRunner> log, IOptionsMonitor<PeriodicReviewOptions> opt)
    { _db = db; _orchestrator = orchestrator; _log = log; _opt = opt; }

    public async Task<int> RunDueAsync(int batchSize, int leadTimeDays, CancellationToken ct = default)
    {
        await ReconcileStaleInReviewAsync(ct);

        var dueBy = DateTime.UtcNow.AddDays(leadTimeDays);
        var candidates = await _db.CustomerReviewSchedules.AsNoTracking()
            .Where(s => s.Status == ReviewScheduleStatus.Scheduled
                     && s.NextReviewDate != null && s.NextReviewDate <= dueBy)
            .OrderBy(s => s.NextReviewDate)
            .Take(batchSize <= 0 ? 50 : batchSize)
            .Select(s => new { s.Id, s.CustomerCode, s.LastKycTransactionId })
            .ToListAsync(ct);

        var processed = 0;
        foreach (var c in candidates)
        {
            if (ct.IsCancellationRequested) break;
            try { await ProcessOneAsync(c.Id, c.CustomerCode, c.LastKycTransactionId, allowNonScheduled: false, ct); processed++; }
            catch (Exception ex) { _log.LogError(ex, "Periodic review: customer {Cust} (schedule {Id}) threw.", c.CustomerCode, c.Id); }
        }
        return processed;
    }

    public async Task<ReviewRunOutcome> RunForCustomerAsync(string customerCode, CancellationToken ct = default)
    {
        var sched = await _db.CustomerReviewSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CustomerCode == customerCode, ct);
        if (sched is null)
            return new ReviewRunOutcome(ReviewOutcome.Skipped, null, null, "No review schedule for customer.");
        return await ProcessOneAsync(sched.Id, sched.CustomerCode, sched.LastKycTransactionId, allowNonScheduled: true, ct);
    }

    private async Task<ReviewRunOutcome> ProcessOneAsync(int scheduleId, string customerCode, int? sourceTxnId, bool allowNonScheduled, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var row = await _db.CustomerReviewSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (row is null) return new ReviewRunOutcome(ReviewOutcome.Skipped, null, null, "Schedule not found.");
        if (row.Status == ReviewScheduleStatus.InReview)
            return new ReviewRunOutcome(ReviewOutcome.Skipped, null, null, "A review is already in progress for this customer.");
        if (!allowNonScheduled && row.Status != ReviewScheduleStatus.Scheduled)
            return new ReviewRunOutcome(ReviewOutcome.Skipped, null, null, $"Schedule not in Scheduled state (was {row.Status}).");

        if (sourceTxnId is null || sourceTxnId <= 0)
        {
            row.Status = ReviewScheduleStatus.Failed;
            row.LastRemark = "No source KYC transaction on the schedule.";
            row.LastUpdatedBy = Actor; row.LastUpdatedDate = now;
            await _db.SaveChangesAsync(ct);
            await AddHistoryAsync(customerCode, row.NextReviewDate, ReviewOutcome.Skipped, null, null, null, row.LastRemark, ct);
            return new ReviewRunOutcome(ReviewOutcome.Skipped, null, null, row.LastRemark);
        }

        // Atomic-ish claim (single active instance): flip to InReview before running.
        row.Status = ReviewScheduleStatus.InReview;
        row.LastUpdatedBy = Actor; row.LastUpdatedDate = now;
        await _db.SaveChangesAsync(ct);

        // Delegate to the orchestrator (impersonates PERIODIC REVIEWER, clones,
        // drives workflow -> onboarding via the existing finalise hook).
        var outcome = await _orchestrator.RunAsync(customerCode, sourceTxnId.Value, ct);

        // Reflect the outcome on the schedule. Completed stays InReview and is
        // healed back to Scheduled (with a fresh date) by the new KYC's terminal
        // callback; Blocked/Failed surface for manual attention (no auto-retry).
        row.Status = outcome.Status switch
        {
            ReviewOutcome.Completed => ReviewScheduleStatus.InReview,
            ReviewOutcome.Blocked   => ReviewScheduleStatus.Blocked,
            _                        => ReviewScheduleStatus.Failed,
        };
        row.LastRemark    = outcome.Remark;
        row.LastUpdatedBy = Actor; row.LastUpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await AddHistoryAsync(customerCode, row.NextReviewDate, outcome.Status,
                              sourceTxnId, outcome.NewTransactionId, outcome.KycRequestId, outcome.Remark, ct);
        return outcome;
    }

    /// <summary>Hardening: a schedule left InReview past the configured stale window
    /// (its re-KYC never reached a terminal decision to heal it) is reset to Scheduled so
    /// the sweep retries it. Safe: the create-gate still Blocks if a KYC is genuinely active.</summary>
    private async Task ReconcileStaleInReviewAsync(CancellationToken ct)
    {
        var hours = Math.Abs(_opt.CurrentValue.StaleInReviewHours);
        if (hours <= 0) return;
        var staleBefore = DateTime.UtcNow.AddHours(-hours);
        var stale = await _db.CustomerReviewSchedules
            .Where(s => s.Status == ReviewScheduleStatus.InReview
                     && s.LastUpdatedDate != null && s.LastUpdatedDate < staleBefore)
            .ToListAsync(ct);
        if (stale.Count == 0) return;
        foreach (var s in stale)
        {
            s.Status = ReviewScheduleStatus.Scheduled;
            s.LastRemark = $"Auto-reconciled from stale InReview (> {hours}h).";
            s.LastUpdatedBy = Actor; s.LastUpdatedDate = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        _log.LogWarning("Periodic review: reconciled {N} stale InReview schedule(s).", stale.Count);
    }

    private async Task AddHistoryAsync(string customerCode, DateTime? dueDate, string outcome,
        int? srcTxn, int? newTxn, long? kycReqId, string? remark, CancellationToken ct)
    {
        _db.CustomerReviewHistories.Add(new CustomerReviewHistory
        {
            TenantId = 1, CustomerCode = customerCode, DueDate = dueDate ?? DateTime.UtcNow,
            Outcome = outcome, SourceTransactionId = srcTxn, NewTransactionId = newTxn,
            KycRequestId = kycReqId, Remarks = remark, CreatedBy = Actor, CreatedDate = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }
}

using FSI.Trade.Compliance.Application.Contracts.Flags;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Flags;

/// <summary>
/// Background worker for the aggregate auto-flag queue (TmX_AutoFlag_Job). Enqueued at
/// transaction save when an aggregate flag is in scope; this worker computes the aggregate
/// at runtime (compute-on-read, no fact table) and stages the aggregate flags off the save
/// path. Each job is processed in its own DI scope so a failed job's partial changes are
/// discarded — the job is then re-queued (with backoff) or dead-lettered in a fresh scope.
/// </summary>
public class AutoFlagJobWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int BatchSize   = 20;
    private const int MaxAttempts = 5;

    private readonly IServiceProvider _sp;
    private readonly ILogger<AutoFlagJobWorker> _log;

    public AutoFlagJobWorker(IServiceProvider sp, ILogger<AutoFlagJobWorker> log)
    {
        _sp = sp; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("AutoFlagJobWorker starting. Interval = {Interval}.", Interval);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                List<int> jobIds;
                using (var scope = _sp.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                    jobIds = await db.AutoFlagJobs
                        .Where(j => j.Status == "Pending")
                        .OrderBy(j => j.EnqueuedDate)
                        .Take(BatchSize)
                        .Select(j => j.JobId)
                        .ToListAsync(stop);
                }

                foreach (var id in jobIds)
                {
                    if (stop.IsCancellationRequested) break;
                    await ProcessOneAsync(id, stop);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "AutoFlagJobWorker tick failed; will retry after interval.");
            }

            try { await Task.Delay(Interval, stop); }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }

    private async Task ProcessOneAsync(int jobId, CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db  = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var eng = scope.ServiceProvider.GetRequiredService<IAutoFlagEvaluator>();

            // Claim: only proceed if still Pending (single-instance deployment; the status
            // flip + save is the claim). Skip if another tick already took it.
            var job = await db.AutoFlagJobs.FirstOrDefaultAsync(j => j.JobId == jobId && j.Status == "Pending", ct);
            if (job is null) return;

            job.Status      = "Processing";
            job.ClaimedDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await eng.EvaluateAndStageAggregateAsync(job.TransactionId, "AutoFlagWorker", DateTime.UtcNow, ct);

            job.Status        = "Done";
            job.ProcessedDate = DateTime.UtcNow;
            job.Error         = null;
            await db.SaveChangesAsync(ct);   // commits staged flags + job status together
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AutoFlagJobWorker: job {JobId} failed.", jobId);
            await FailJobAsync(jobId, ex.Message, ct);   // fresh scope — discards partial changes
        }
    }

    /// <summary>Re-queue (with backoff via Attempts) or dead-letter a failed job in a
    /// clean scope, so the failed processing scope's partial flag changes are never saved.</summary>
    private async Task FailJobAsync(int jobId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db  = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var job = await db.AutoFlagJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
            if (job is null) return;

            job.Attempts += 1;
            job.Error     = error.Length > 3900 ? error[..3900] : error;
            job.Status    = job.Attempts >= MaxAttempts ? "DeadLetter" : "Pending";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AutoFlagJobWorker: could not update failed job {JobId}.", jobId);
        }
    }
}

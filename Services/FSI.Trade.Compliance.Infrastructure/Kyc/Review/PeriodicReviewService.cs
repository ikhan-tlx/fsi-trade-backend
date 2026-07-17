using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Kyc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Kyc.Review;

/// <summary>
/// Background service that periodically scans TmX_Customer_Review_Schedule for
/// customers whose Next_Review_Date is due and initiates their KYC review via
/// <see cref="IPeriodicReviewRunner"/>. Mirrors the FccmCaseIdPoller pattern
/// (scope-per-tick, per-tick try/catch, config-driven interval). NOT a "poller".
/// </summary>
public class PeriodicReviewService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<PeriodicReviewOptions> _opt;
    private readonly ILogger<PeriodicReviewService> _log;

    public PeriodicReviewService(IServiceProvider sp, IOptionsMonitor<PeriodicReviewOptions> opt, ILogger<PeriodicReviewService> log)
    { _sp = sp; _opt = opt; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("PeriodicReviewService starting. Interval = {Interval}, BatchSize = {Batch}, Enabled = {Enabled}.",
            _opt.CurrentValue.ScanInterval, _opt.CurrentValue.BatchSize, _opt.CurrentValue.Enabled);

        while (!stop.IsCancellationRequested)
        {
            var o = _opt.CurrentValue;
            try
            {
                if (o.Enabled)
                {
                    using var scope = _sp.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<IPeriodicReviewRunner>();
                    var n = await runner.RunDueAsync(o.BatchSize, o.LeadTimeDays, stop);
                    if (n > 0) _log.LogInformation("PeriodicReviewService: initiated {N} due customer review(s).", n);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PeriodicReviewService tick failed; will retry after the interval.");
            }

            try { await Task.Delay(o.ScanInterval, stop); }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }
}

using FSI.Trade.Compliance.Application.Common.Options;
using FSI.Trade.Compliance.Application.Contracts.Customer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.CustomerInsight;

/// <summary>
/// Daily hosted job that upserts the book-wide Customer Intelligence KPI snapshot
/// (TmX_Customer_Insight_Daily) so the listing strip can show real "vs last week"
/// deltas (plan §6 opt-1). Mirrors <c>PeriodicReviewService</c>: scope-per-tick,
/// per-tick try/catch, config-driven interval. The write is idempotent (keyed on
/// the UTC date), so ticking several times a day only refreshes today's row.
/// </summary>
public class CustomerInsightSnapshotService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<CustomerInsightSnapshotOptions> _opt;
    private readonly ILogger<CustomerInsightSnapshotService> _log;

    public CustomerInsightSnapshotService(
        IServiceProvider sp,
        IOptionsMonitor<CustomerInsightSnapshotOptions> opt,
        ILogger<CustomerInsightSnapshotService> log)
    {
        _sp = sp;
        _opt = opt;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("CustomerInsightSnapshotService starting. Interval = {Interval}, Enabled = {Enabled}.",
            _opt.CurrentValue.CheckInterval, _opt.CurrentValue.Enabled);

        while (!stop.IsCancellationRequested)
        {
            var o = _opt.CurrentValue;
            try
            {
                if (o.Enabled)
                {
                    using var scope = _sp.CreateScope();
                    var writer = scope.ServiceProvider.GetRequiredService<ICustomerInsightSnapshotWriter>();
                    await writer.WriteTodayAsync(stop);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "CustomerInsightSnapshotService tick failed; will retry after the interval.");
            }

            try { await Task.Delay(o.CheckInterval, stop); }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }
}

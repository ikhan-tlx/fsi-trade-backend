using System.Globalization;

namespace FSI.Trade.Compliance.Infrastructure.Flags.Metrics;

/// <summary>
/// Days since the customer's previous transaction (relative to this transaction's value
/// date). Left_Source='Aggregate', Handler_Key='DAYS_SINCE_LAST_ACTIVITY'. Powers
/// "dormant then activity" flags — e.g. compare with "&gt; 180". Not computable for a
/// customer with no prior activity (a brand-new customer is not "dormant then active").
/// </summary>
public sealed class RecencyMetricHandler : IRuleMetricHandler
{
    public string   Key      => "DAYS_SINCE_LAST_ACTIVITY";
    public MetricKind Kind   => MetricKind.Aggregate;
    public string   DataType => "number";

    public IReadOnlyList<MetricParamDef> Params { get; } = new[]
    {
        new MetricParamDef("groupBy", "Grouped by", "enum", Options: new[] { "Customer" }),
    };

    public async Task<MetricResult> ComputeAsync(MetricContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.CustomerCode))
            return MetricResult.NotComputable(new { reason = "no customer code" });

        var last = await ctx.Data.GetLastActivityDateBeforeAsync(
            ctx.CustomerCode!, ctx.ValueDate, ctx.Tx.TransactionId, ct);
        if (last is null)
            return MetricResult.NotComputable(new { reason = "no prior activity" });

        var days = (int)Math.Floor((ctx.ValueDate.Date - last.Value.Date).TotalDays);
        if (days < 0) days = 0;
        return MetricResult.Ok(days.ToString(CultureInfo.InvariantCulture),
            new { lastActivity = last, valueDate = ctx.ValueDate, daysSince = days });
    }
}

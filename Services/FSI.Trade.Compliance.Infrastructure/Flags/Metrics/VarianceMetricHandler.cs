using System.Globalization;
using System.Text.Json;

namespace FSI.Trade.Compliance.Infrastructure.Flags.Metrics;

/// <summary>
/// Variance between two operands: <c>(A − B) / B</c>, returned as a signed ratio
/// (0.20 = 20% above B, −0.20 = 20% below). A rule sets Left_Source='Aggregate',
/// Handler_Key='VARIANCE' and an Aggregate_Spec JSON:
///   { "a": { "kind":"field",     "field":"&lt;code&gt;" },
///     "b": { "kind":"aggregate", "function":"Avg", "field":"&lt;code&gt;", "windowDays":180 } }
/// Each operand is either a current-transaction field (<c>kind:"field"|"kyc"</c>, read from
/// the flattened UDF) or a cross-transaction aggregate (<c>kind:"aggregate"</c>, computed over
/// the customer's window, self excluded). The single ratio is fed into the existing operator
/// comparison — e.g. <c>&gt; 0.20</c> for a spike, a second OR condition <c>&lt; −0.20</c> for a
/// drop. Not computable when either operand is missing or B is zero (cold-start / no baseline).
/// </summary>
public sealed class VarianceMetricHandler : IRuleMetricHandler
{
    public string   Key      => "VARIANCE";
    public MetricKind Kind   => MetricKind.Aggregate;
    public string   DataType => "number";

    public IReadOnlyList<MetricParamDef> Params { get; } = new[]
    {
        new MetricParamDef("a", "Field A", "operand", Required: true),
        new MetricParamDef("b", "Compared with (Field B)", "operand", Required: true),
    };

    public async Task<MetricResult> ComputeAsync(MetricContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.SpecJson)) return MetricResult.NotComputable(new { reason = "no spec" });

        JsonElement spec;
        try { using var doc = JsonDocument.Parse(ctx.SpecJson); spec = doc.RootElement.Clone(); }
        catch { return MetricResult.NotComputable(new { reason = "bad spec json" }); }

        if (!spec.TryGetProperty("a", out var aSpec) || !spec.TryGetProperty("b", out var bSpec))
            return MetricResult.NotComputable(new { reason = "variance needs operands a and b" });

        var a = await ResolveOperandAsync(aSpec, ctx, ct);
        var b = await ResolveOperandAsync(bSpec, ctx, ct);

        if (a is null) return MetricResult.NotComputable(new { reason = "operand A not computable", a, b });
        if (b is null) return MetricResult.NotComputable(new { reason = "operand B not computable", a, b });
        if (b.Value == 0m) return MetricResult.NotComputable(new { reason = "baseline B is zero", a, b });

        var variance = (a.Value - b.Value) / b.Value;
        return MetricResult.Ok(
            variance.ToString(CultureInfo.InvariantCulture),
            new { a, b, computed = variance, formula = "(a-b)/b" });
    }

    /// <summary>Resolve one operand to a decimal: a current-transaction field, or a
    /// cross-transaction aggregate over the customer window (self excluded).</summary>
    private static async Task<decimal?> ResolveOperandAsync(JsonElement op, MetricContext ctx, CancellationToken ct)
    {
        if (op.ValueKind != JsonValueKind.Object) return null;
        var kind = (Str(op, "kind") ?? "field").ToLowerInvariant();

        if (kind != "aggregate")
        {
            // Current transaction field (Trade or KYC) — first numeric value in the flattened UDF.
            var field = Str(op, "field");
            if (string.IsNullOrWhiteSpace(field) || !ctx.Udf.TryGetValue(field!, out var vals)) return null;
            return FirstNumber(vals);
        }

        // Aggregate over the customer window.
        var function = Str(op, "function") ?? "Avg";
        var aggField = Str(op, "field");
        var window   = Int(op, "windowDays");
        if (string.IsNullOrWhiteSpace(ctx.CustomerCode) || window is null or <= 0) return null;

        var to   = ctx.ValueDate;
        var from = to.AddDays(-window.Value);
        var rows = (await ctx.Data.GetCustomerWindowAsync(ctx.CustomerCode!, from, to, ct))
            .Where(r => r.TransactionId != ctx.Tx.TransactionId)   // baseline excludes the current txn
            .ToList();

        if (string.Equals(function, "Count", StringComparison.OrdinalIgnoreCase))
            return rows.Count;

        var nums = new List<decimal>();
        foreach (var r in rows)
        {
            var flat = UdfFlatten.Flatten(r.UdfData);
            var codes = string.IsNullOrWhiteSpace(aggField)
                ? await ctx.Data.GetAmountFieldCodesAsync(r.ProductId, r.SubProductTabId, ct)
                : new[] { aggField! };
            foreach (var code in codes)
                if (flat.TryGetValue(code, out var vals))
                    foreach (var v in vals)
                        if (decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) nums.Add(d);
        }
        if (nums.Count == 0) return null;

        return function switch
        {
            "Sum" => nums.Sum(),
            "Avg" => nums.Average(),
            "Min" => nums.Min(),
            "Max" => nums.Max(),
            _     => nums.Average(),
        };
    }

    private static decimal? FirstNumber(IEnumerable<string> vals)
    {
        foreach (var v in vals)
            if (decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static int? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;
}

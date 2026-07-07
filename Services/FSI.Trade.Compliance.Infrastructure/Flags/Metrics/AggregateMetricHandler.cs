using System.Globalization;
using System.Text.Json;

namespace FSI.Trade.Compliance.Infrastructure.Flags.Metrics;

/// <summary>
/// The one generic aggregate metric. A rule sets Left_Source='Aggregate',
/// Handler_Key='AGGREGATE' and an Aggregate_Spec JSON:
///   { "function":"Sum|Avg|Min|Max|Count|CountDistinct",
///     "mode":"IntraTransaction|CrossTransaction",
///     "field":"&lt;UDF field code&gt;",   // intra: the field to aggregate
///     "role":"Amount",                     // cross value funcs: per-product field role
///     "windowDays":90,                      // cross: window ending on the txn value date
///     "includeSelf":true,
///     "filter":{ "field":"&lt;code&gt;", "op":"LessThan|GreaterThan|Equals|NotEquals",
///                "value":"10000" } }        // optional: only aggregate rows/values that pass
/// Intra-transaction aggregates run over the current document's flattened UDF; cross-
/// transaction over the customer's window (compute-on-read via IRuleDataProvider). The
/// optional filter powers structuring / sub-threshold patterns ("count txns where amount
/// &lt; 10,000"). Produces a single numeric scalar fed into the existing operator comparison.
/// </summary>
public sealed class AggregateMetricHandler : IRuleMetricHandler
{
    public string   Key      => "AGGREGATE";
    public MetricKind Kind   => MetricKind.Aggregate;
    public string   DataType => "number";

    public IReadOnlyList<MetricParamDef> Params { get; } = new[]
    {
        new MetricParamDef("function", "Function", "enum", Required: true,
            Options: new[] { "Count", "CountDistinct", "Sum", "Avg", "Min", "Max" }),
        new MetricParamDef("mode", "Scope", "enum", Required: true,
            Options: new[] { "IntraTransaction", "CrossTransaction" }),
        new MetricParamDef("field", "Field", "field",
            FieldFilter: "numericForValueFunctions"),
        new MetricParamDef("role", "Field role", "enum", Options: new[] { "Amount" }),
        new MetricParamDef("windowDays", "Window (days)", "number"),
        new MetricParamDef("groupBy", "Grouped by", "enum", Options: new[] { "Customer" }),
        // Optional predicate ("only include where …"): a field, an operator, and a value.
        new MetricParamDef("filterField", "Only include where — field", "field"),
        new MetricParamDef("filterOp", "Only include where — operator", "enum",
            Options: new[] { "LessThan", "GreaterThan", "Equals", "NotEquals" }),
        new MetricParamDef("filterValue", "Only include where — value", "text"),
    };

    public async Task<MetricResult> ComputeAsync(MetricContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.SpecJson)) return MetricResult.NotComputable(new { reason = "no spec" });

        JsonElement spec;
        try { using var doc = JsonDocument.Parse(ctx.SpecJson); spec = doc.RootElement.Clone(); }
        catch { return MetricResult.NotComputable(new { reason = "bad spec json" }); }

        var function = Str(spec, "function") ?? "Count";
        var mode     = Str(spec, "mode") ?? "IntraTransaction";
        var field    = Str(spec, "field");
        var window   = Int(spec, "windowDays");

        var filter = ParseFilter(spec);

        if (string.Equals(mode, "IntraTransaction", StringComparison.OrdinalIgnoreCase))
        {
            var values = (!string.IsNullOrWhiteSpace(field) && ctx.Udf.TryGetValue(field!, out var v)) ? v : new List<string>();
            if (filter is not null)
            {
                // Same-field predicate → filter the aggregated values directly.
                // Different-field predicate → document-level gate (all-or-nothing) since the
                // flattened UDF doesn't preserve cross-column row alignment.
                if (string.IsNullOrWhiteSpace(filter.Field) || filter.Field == field)
                    values = values.Where(x => filter.Passes(x)).ToList();
                else
                {
                    var fv = ctx.Udf.TryGetValue(filter.Field!, out var fvals) ? fvals : new List<string>();
                    if (!fv.Any(filter.Passes)) values = new List<string>();
                }
            }
            return Compute(function, values, new { function, mode, field, filtered = filter is not null, count = values.Count });
        }

        // CrossTransaction
        if (string.IsNullOrWhiteSpace(ctx.CustomerCode))
            return MetricResult.NotComputable(new { reason = "no customer code", mode });
        if (window is null or <= 0)
            return MetricResult.NotComputable(new { reason = "windowDays required", mode });

        var to   = ctx.ValueDate;
        var from = to.AddDays(-window.Value);
        var rows = await ctx.Data.GetCustomerWindowAsync(ctx.CustomerCode!, from, to, ct);

        var includeSelf = Bool(spec, "includeSelf") ?? true;
        if (!includeSelf) rows = rows.Where(r => r.TransactionId != ctx.Tx.TransactionId).ToList();

        // Pre-flatten once so both the filter and the value collection read the same map.
        var flats = rows.Select(r => (row: r, flat: UdfFlatten.Flatten(r.UdfData))).ToList();
        if (filter is not null && !string.IsNullOrWhiteSpace(filter.Field))
            flats = flats.Where(x =>
            {
                var fv = x.flat.TryGetValue(filter.Field!, out var fvals) ? fvals : new List<string>();
                return fv.Any(filter.Passes);
            }).ToList();

        if (function == "Count")
            return MetricResult.Ok(flats.Count.ToString(CultureInfo.InvariantCulture),
                new { function, mode, windowDays = window, from, to, filtered = filter is not null, txnCount = flats.Count });

        // Gather the target values across the (filtered) window (value funcs + distinct need a field/role).
        var collected = new List<string>();
        foreach (var (row, flat) in flats)
        {
            var codes = string.IsNullOrWhiteSpace(field)
                ? await ctx.Data.GetAmountFieldCodesAsync(row.ProductId, row.SubProductTabId, ct)
                : new[] { field! };
            foreach (var code in codes)
                if (flat.TryGetValue(code, out var vals)) collected.AddRange(vals);
        }

        return Compute(function, collected,
            new { function, mode, windowDays = window, from, to, filtered = filter is not null, txnCount = flats.Count, valueCount = collected.Count });
    }

    private static MetricResult Compute(string function, List<string> values, object inputs)
    {
        switch (function)
        {
            case "Count":
                return MetricResult.Ok(values.Count.ToString(CultureInfo.InvariantCulture), inputs);
            case "CountDistinct":
                return MetricResult.Ok(
                    values.Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(CultureInfo.InvariantCulture), inputs);
        }

        var nums = values
            .Select(v => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (decimal?)d : null)
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        if (nums.Count == 0) return MetricResult.NotComputable(inputs);

        decimal result = function switch
        {
            "Sum" => nums.Sum(),
            "Avg" => nums.Average(),
            "Min" => nums.Min(),
            "Max" => nums.Max(),
            _     => 0m,
        };
        return MetricResult.Ok(result.ToString(CultureInfo.InvariantCulture), inputs);
    }

    /// <summary>Optional "only include where" predicate parsed from the spec.</summary>
    private sealed record AggFilter(string? Field, string Op, string Value)
    {
        public bool Passes(string candidate)
        {
            switch (Op)
            {
                case "Equals":    return string.Equals(candidate, Value, StringComparison.OrdinalIgnoreCase);
                case "NotEquals": return !string.Equals(candidate, Value, StringComparison.OrdinalIgnoreCase);
                case "GreaterThan":
                case "LessThan":
                    if (decimal.TryParse(candidate, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && decimal.TryParse(Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                        return Op == "GreaterThan" ? a > b : a < b;
                    return false;
                default: return false;
            }
        }
    }

    private static AggFilter? ParseFilter(JsonElement spec)
    {
        if (!spec.TryGetProperty("filter", out var f) || f.ValueKind != JsonValueKind.Object) return null;
        var op  = Str(f, "op");
        var val = Str(f, "value");
        if (string.IsNullOrWhiteSpace(op) || val is null) return null;
        return new AggFilter(Str(f, "field"), op!, val);
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static int? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;
    private static bool? Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False) ? p.GetBoolean() : null;
}

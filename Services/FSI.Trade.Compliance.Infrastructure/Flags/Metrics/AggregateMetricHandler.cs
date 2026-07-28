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
        // Structural scope layered ON TOP of the (always-customer) window: customer → product →
        // sub-product. Optional; omitting them counts the customer's whole in-window history.
        new MetricParamDef("productId", "Restrict to product", "number"),
        new MetricParamDef("subProductTabId", "Restrict to sub-product", "number"),
        // "SubProduct" makes CountDistinct count distinct sub-products (SubProductTabId) rather
        // than distinct field values — powers the "distinct sub-product count" scenario.
        new MetricParamDef("dimension", "Count of", "enum", Options: new[] { "Transaction", "SubProduct" }),
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

        // Structural scope + distinct dimension (all optional, layered on the customer window).
        var dimension       = Str(spec, "dimension");
        var restrictProduct = Int(spec, "productId");
        var restrictSubProd = Int(spec, "subProductTabId");

        var filter = ParseFilter(spec);

        // Enhancement #1 — cross-subproduct aggregation. When `sources` is present the
        // metric aggregates the UNION of values pulled from an explicit list of
        // (product?, sub-product?, field) sources, so e.g. Sum can combine
        // ILF_LcAmount (LC Issuance) + IDF_Amount (Import Drawing). Each source is only
        // read from a row whose product/sub-product matches the source's scope (a source
        // with no scope applies to every row). When `sources` is absent, behaviour is
        // unchanged (single `field`, or the per-(product,sub-product) Amount role).
        var sources = ParseSources(spec);

        if (string.Equals(mode, "IntraTransaction", StringComparison.OrdinalIgnoreCase))
        {
            List<string> values;
            if (sources.Count > 0)
            {
                values = new List<string>();
                foreach (var s in sources)
                    if (ctx.Udf.TryGetValue(s.Field, out var sv)) values.AddRange(sv);
            }
            else
            {
                values = (!string.IsNullOrWhiteSpace(field) && ctx.Udf.TryGetValue(field!, out var v)) ? v.ToList() : new List<string>();
            }
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

        // Structural narrowing layered ON TOP of the always-customer window: customer → product
        // → sub-product. Both optional; omitting them keeps the whole-customer-history behaviour.
        if (restrictProduct is int rp) rows = rows.Where(r => r.ProductId == rp).ToList();
        if (restrictSubProd is int rs) rows = rows.Where(r => r.SubProductTabId == rs).ToList();

        // Distinct sub-product count: how many *different* sub-products the customer used in the
        // window (within the product scope), independent of any UDF field. Powers the
        // "distinct sub-products > N" scenario.
        if (string.Equals(function, "CountDistinct", StringComparison.OrdinalIgnoreCase)
            && string.Equals(dimension, "SubProduct", StringComparison.OrdinalIgnoreCase))
        {
            var distinctSub = rows.Where(r => r.SubProductTabId != null)
                                  .Select(r => r.SubProductTabId!.Value)
                                  .Distinct().Count();
            return MetricResult.Ok(distinctSub.ToString(CultureInfo.InvariantCulture),
                new { function, mode, dimension = "SubProduct", windowDays = window, from, to,
                      productId = restrictProduct, distinctSubProducts = distinctSub, txnCount = rows.Count });
        }

        // Pre-flatten once so both the filter and the value collection read the same map.
        // (For a plain Count the structural restriction above already yields the sub-product
        // frequency, so no field is needed — powers the "sub-product used > N times" scenario.)
        var flats = rows.Select(r => (row: r, flat: UdfFlatten.Flatten(r.UdfData))).ToList();
        if (filter is not null && !string.IsNullOrWhiteSpace(filter.Field))
            flats = flats.Where(x =>
            {
                var fv = x.flat.TryGetValue(filter.Field!, out var fvals) ? fvals : new List<string>();
                return fv.Any(filter.Passes);
            }).ToList();

        // ── Enhancement #2 — group-based aggregation. Partition the (customer-scoped) window rows
        // by a composite key whose parts each map PER SUB-PRODUCT to a field (so the same LC number
        // is read from ILF_ContractRef on an issuance and IDF_UnderlyingLcRef on a drawing). Then
        // aggregate PER GROUP and, optionally, compute a per-group counterpart (`rhs`) the evaluator
        // compares against (e.g. Σ drawings vs Max LC amount, per LC). Customer is always the implicit
        // outer group — grouping never crosses customers.
        var groupBy = ParseGroupBy(spec);
        if (groupBy.Count > 0)
        {
            var rhsSpec = ParseRhs(spec);
            var cmpSpec = ParseCompare(spec);   // #3 — field-vs-counterpart per group
            var buckets = new Dictionary<string, (Dictionary<string, string> key, List<string> lhs, List<string> rhs, List<string> cmpL, List<string> cmpR)>(StringComparer.Ordinal);
            foreach (var (row, flat) in flats)
            {
                var keyParts = new Dictionary<string, string>();
                var composite = new System.Text.StringBuilder();
                var keyed = true;
                foreach (var part in groupBy)
                {
                    var kv = new List<string>();
                    CollectFrom(part.Sources, row.ProductId, row.SubProductTabId, flat, kv);
                    var raw = kv.FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(raw)) { keyed = false; break; }   // row can't join this group
                    var display = part.Trim ? raw!.Trim() : raw!;
                    var norm = part.Trim ? raw!.Trim() : raw!;
                    if (part.Ci) norm = norm.ToLowerInvariant();
                    keyParts[part.As] = display;
                    composite.Append(part.As).Append('=').Append(norm).Append('|');
                }
                if (!keyed) continue;
                var ck = composite.ToString();
                if (!buckets.TryGetValue(ck, out var b))
                {
                    b = (keyParts, new List<string>(), new List<string>(), new List<string>(), new List<string>());
                    buckets[ck] = b;
                }
                if (cmpSpec is not null)
                {
                    // Field-vs-counterpart: collect the left and right field values in this group.
                    CollectFrom(cmpSpec.Left,  row.ProductId, row.SubProductTabId, flat, b.cmpL);
                    CollectFrom(cmpSpec.Right, row.ProductId, row.SubProductTabId, flat, b.cmpR);
                }
                else
                {
                    // LHS values for this row (sources, else a single field).
                    if (sources.Count > 0) CollectFrom(sources, row.ProductId, row.SubProductTabId, flat, b.lhs);
                    else if (!string.IsNullOrWhiteSpace(field) && flat.TryGetValue(field!, out var fv)) b.lhs.AddRange(fv);
                    if (rhsSpec is not null) CollectFrom(rhsSpec.Sources, row.ProductId, row.SubProductTabId, flat, b.rhs);
                }
            }

            var groups = new List<MetricGroup>();
            foreach (var b in buckets.Values)
            {
                if (cmpSpec is not null)
                {
                    // Compare the group's left field value SET to its right field value SET
                    // (per-group field-vs-counterpart). Need both sides present to compare.
                    var left  = b.cmpL.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var right = b.cmpR.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (left.Count == 0 || right.Count == 0) continue;   // nothing to correlate in this group
                    groups.Add(new MetricGroup(b.key, string.Join(",", left), string.Join(",", right)) { LeftSet = left, RightSet = right });
                    continue;
                }
                var lhsVal = Reduce(function, b.lhs);
                if (lhsVal is null) continue;                              // group's LHS not computable → skip
                var rhsVal = rhsSpec is not null ? Reduce(rhsSpec.Function, b.rhs) : null;
                groups.Add(new MetricGroup(b.key, lhsVal, rhsVal));
            }
            return MetricResult.Grouped(groups,
                new { function, mode, windowDays = window, from, to, grouped = true,
                      groupKeys = groupBy.Select(g => g.As).ToArray(), groupCount = groups.Count,
                      hasRhs = rhsSpec is not null, compare = cmpSpec is not null });
        }

        if (function == "Count")
            return MetricResult.Ok(flats.Count.ToString(CultureInfo.InvariantCulture),
                new { function, mode, windowDays = window, from, to, filtered = filter is not null, txnCount = flats.Count });

        // Gather the target values across the (filtered) window (value funcs + distinct need a field/role).
        var collected = new List<string>();
        foreach (var (row, flat) in flats)
        {
            if (sources.Count > 0)
            {
                // Cross-subproduct: pull each source's field only from rows in that source's
                // (product/sub-product) scope. A source with no scope matches every row.
                foreach (var s in sources)
                {
                    if (s.ProductId is int sp && row.ProductId != sp) continue;
                    if (s.SubProductTabId is int st && row.SubProductTabId != st) continue;
                    if (flat.TryGetValue(s.Field, out var vals)) collected.AddRange(vals);
                }
            }
            else
            {
                var codes = string.IsNullOrWhiteSpace(field)
                    ? await ctx.Data.GetAmountFieldCodesAsync(row.ProductId, row.SubProductTabId, ct)
                    : new[] { field! };
                foreach (var code in codes)
                    if (flat.TryGetValue(code, out var vals)) collected.AddRange(vals);
            }
        }

        return Compute(function, collected,
            new { function, mode, windowDays = window, from, to, filtered = filter is not null,
                  sources = sources.Count, txnCount = flats.Count, valueCount = collected.Count });
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

    /// <summary>One aggregation source (enhancement #1): a field to pull, optionally scoped to a
    /// product / sub-product. Scope null = read the field from every row that has it.</summary>
    private sealed record AggSource(int? ProductId, int? SubProductTabId, string Field);

    private static List<AggSource> ParseSources(JsonElement spec) => ParseSourcesFrom(spec, "sources");

    private static List<AggSource> ParseSourcesFrom(JsonElement parent, string prop)
    {
        var list = new List<AggSource>();
        if (!parent.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var f = Str(e, "field");
            if (string.IsNullOrWhiteSpace(f)) continue;
            list.Add(new AggSource(Int(e, "productId"), Int(e, "subProductTabId"), f!.Trim()));
        }
        return list;
    }

    // ── Enhancement #2 helpers: group-by key parts, per-group counterpart, value collection ──
    private sealed record GroupKeyPart(string As, List<AggSource> Sources, bool Trim, bool Ci);

    private static List<GroupKeyPart> ParseGroupBy(JsonElement spec)
    {
        var list = new List<GroupKeyPart>();
        if (!spec.TryGetProperty("groupBy", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var name = Str(e, "as") ?? "key";
            var srcs = ParseSourcesFrom(e, "sources");
            if (srcs.Count == 0) continue;
            // Default normalisation = trim + case-insensitive; honour an explicit list if given.
            bool trim = true, ci = true;
            if (e.TryGetProperty("normalize", out var n) && n.ValueKind == JsonValueKind.Array)
            {
                var opts = n.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString()!.ToLowerInvariant()).ToHashSet();
                trim = opts.Contains("trim");
                ci   = opts.Contains("ci");
            }
            list.Add(new GroupKeyPart(name, srcs, trim, ci));
        }
        return list;
    }

    private sealed record RhsAgg(string Function, List<AggSource> Sources);

    private static RhsAgg? ParseRhs(JsonElement spec)
    {
        if (!spec.TryGetProperty("rhs", out var r) || r.ValueKind != JsonValueKind.Object) return null;
        var fn = Str(r, "function") ?? "Max";
        var srcs = ParseSourcesFrom(r, "sources");
        if (srcs.Count == 0)
        {
            var f = Str(r, "field");
            if (!string.IsNullOrWhiteSpace(f)) srcs.Add(new AggSource(Int(r, "productId"), Int(r, "subProductTabId"), f!.Trim()));
        }
        return srcs.Count == 0 ? null : new RhsAgg(fn, srcs);
    }

    /// <summary>Append every scope-matching source's field values from one row's flat UDF.</summary>
    // #3 — a per-group field comparison: left field value set vs the counterpart's field value set.
    private sealed record CompareSpec(List<AggSource> Left, List<AggSource> Right);

    private static CompareSpec? ParseCompare(JsonElement spec)
    {
        if (!spec.TryGetProperty("compare", out var c) || c.ValueKind != JsonValueKind.Object) return null;
        List<AggSource> Side(string name)
        {
            if (!c.TryGetProperty(name, out var s) || s.ValueKind != JsonValueKind.Object) return new List<AggSource>();
            var list = ParseSourcesFrom(s, "sources");
            if (list.Count == 0)
            {
                var f = Str(s, "field");
                if (!string.IsNullOrWhiteSpace(f)) list.Add(new AggSource(Int(s, "productId"), Int(s, "subProductTabId"), f!.Trim()));
            }
            return list;
        }
        var left = Side("left"); var right = Side("right");
        return (left.Count == 0 || right.Count == 0) ? null : new CompareSpec(left, right);
    }

    private static void CollectFrom(List<AggSource> srcs, int productId, int? tabId,
        IReadOnlyDictionary<string, List<string>> flat, List<string> into)
    {
        foreach (var s in srcs)
        {
            if (s.ProductId is int sp && productId != sp) continue;
            if (s.SubProductTabId is int st && tabId != st) continue;
            if (flat.TryGetValue(s.Field, out var vals)) into.AddRange(vals);
        }
    }

    /// <summary>Reduce a set of string values to a single aggregate scalar (invariant-formatted),
    /// or null when a value function has no numeric input.</summary>
    private static string? Reduce(string function, List<string> values)
    {
        switch (function)
        {
            case "Count":         return values.Count.ToString(CultureInfo.InvariantCulture);
            case "CountDistinct": return values.Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(CultureInfo.InvariantCulture);
        }
        var nums = values
            .Select(v => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (decimal?)d : null)
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        if (nums.Count == 0) return null;
        decimal result = function switch { "Sum" => nums.Sum(), "Avg" => nums.Average(), "Min" => nums.Min(), "Max" => nums.Max(), _ => 0m };
        return result.ToString(CultureInfo.InvariantCulture);
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

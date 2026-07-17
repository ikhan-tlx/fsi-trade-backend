using System.Globalization;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Flags;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Flags;
using FSI.Trade.Compliance.Domain.Entities.Transaction;
using FSI.Trade.Compliance.Infrastructure.Flags.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FSI.Trade.Compliance.Infrastructure.Flags;

/// <summary>
/// P1 configuration-driven auto-flag engine. Generalises the legacy country/HS
/// sanction screening into rule type #1 (field ∈/∉ reference list), plus
/// field-vs-static and field-vs-field comparisons. Rules within a Rule_Group
/// are ANDed; groups are ORed. Reference lists resolve to the existing
/// REPO_SANCTIONED_COUNTRIES / REPO_SCREENING_GOODS or to TmX_Reference_List.
///
/// Aggregate / derived archetypes (P2/P3) are out of scope here — rules whose
/// operands need them evaluate to "not satisfied" and are skipped.
/// </summary>
public sealed class AutoFlagEvaluator : IAutoFlagEvaluator
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<AutoFlagEvaluator> _log;
    private readonly MetricHandlerRegistry _metrics;
    private readonly IRuleDataProvider _data;

    public AutoFlagEvaluator(
        IApplicationDbContext db, ILogger<AutoFlagEvaluator> log,
        MetricHandlerRegistry metrics, IRuleDataProvider data)
    {
        _db      = db;
        _log     = log;
        _metrics = metrics;
        _data    = data;
    }

    /// <summary>Aggregate/Derived left-operands are computed by a metric handler, not read
    /// from the UDF. This test routes them to <see cref="ComputeMetricsAsync"/>.</summary>
    private static bool IsMetricSource(string? src)
        => string.Equals(src, "Aggregate", StringComparison.OrdinalIgnoreCase)
        || string.Equals(src, "Derived", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pre-compute every aggregate/derived rule's scalar (async, may hit the DB)
    /// keyed by Rule_ID, so the synchronous rule evaluation can read them. Never throws.</summary>
    private async Task<Dictionary<int, MetricResult>> ComputeMetricsAsync(
        IReadOnlyList<FlagRule> rules, Transaction tx,
        IReadOnlyDictionary<string, List<string>> udf, CancellationToken ct)
    {
        var result = new Dictionary<int, MetricResult>();
        var metricRules = rules.Where(r => IsMetricSource(r.LeftSource)).ToList();
        if (metricRules.Count == 0) return result;

        var customerCode = await _data.ResolveCustomerCodeAsync(tx.TransactionId, ct);
        foreach (var r in metricRules)
        {
            if (!_metrics.TryGet(r.HandlerKey, out var handler))
            {
                result[r.RuleId] = MetricResult.NotComputable(new { reason = "unknown handler", handler = r.HandlerKey });
                continue;
            }
            var ctx = new MetricContext
            {
                Tx = tx, Udf = udf, SpecJson = r.AggregateSpec, CustomerCode = customerCode, Data = _data,
            };
            try { result[r.RuleId] = await handler.ComputeAsync(ctx, ct); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Metric '{Key}' failed for rule {Rule}.", r.HandlerKey, r.RuleId);
                result[r.RuleId] = MetricResult.NotComputable(new { reason = "error", message = ex.Message });
            }
        }
        return result;
    }

    /// <summary>Save path: stage the synchronous (non-aggregate) flags, then enqueue an async
    /// job if any aggregate flag is in scope. The caller's SaveChanges commits both.</summary>
    public Task<IReadOnlyList<AutoFlagHit>> EvaluateAndStageAsync(
        int transactionId, string userId, DateTime now, CancellationToken ct)
        => StageAsync(transactionId, userId, now, aggregatePass: false, ct);

    /// <summary>Worker path: stage only the aggregate flags for a transaction.</summary>
    public Task<IReadOnlyList<AutoFlagHit>> EvaluateAndStageAggregateAsync(
        int transactionId, string userId, DateTime now, CancellationToken ct)
        => StageAsync(transactionId, userId, now, aggregatePass: true, ct);

    private async Task<IReadOnlyList<AutoFlagHit>> StageAsync(
        int transactionId, string userId, DateTime now, bool aggregatePass, CancellationToken ct)
    {
        var hits = new List<AutoFlagHit>();

        var tx = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);
        if (tx is null) return hits;
        var txnDate = tx.TransactionDate ?? tx.CreatedDate;   // base for "txn" relative dates

        var udf = await MergeKycUdfAsync(transactionId, await LoadUdfAsync(transactionId, ct), ct);
        var autoTypeId = await ResolveAutoFlagTypeIdAsync(ct);
        if (autoTypeId is null) return hits;

        // Auto flags whose scope matches this product / sub-product tab (or global).
        var inScopeFlagIds = await _db.FlagScopes.AsNoTracking()
            .Where(s => s.ActiveFlag
                && (s.ProductId == null
                    || (s.ProductId == tx.ProductId
                        && (s.TabId == null || s.TabId == tx.SubProductTabId))))
            .Select(s => s.FlagId)
            .Distinct()
            .ToListAsync(ct);

        var flags = await _db.FlagCatalogues.AsNoTracking()
            .Where(f => f.FlagTypeLkpId == autoTypeId && f.ActiveFlag && !f.IsDeleted
                        && inScopeFlagIds.Contains(f.FlagId))
            .Select(f => new { f.FlagId, f.RaisePerMatch })
            .ToListAsync(ct);
        if (flags.Count == 0) return hits;

        var flagIds = flags.Select(f => f.FlagId).ToList();
        var rules = await _db.FlagRules.AsNoTracking()
            .Where(r => flagIds.Contains(r.FlagId) && r.ActiveFlag)
            .ToListAsync(ct);
        var rulesByFlag = rules.GroupBy(r => r.FlagId).ToDictionary(g => g.Key, g => g.ToList());

        static bool FlagIsAggregate(List<FlagRule> fr) => fr.Any(r => IsMetricSource(r.LeftSource));

        // This pass owns only the flags whose aggregate-ness matches aggregatePass:
        // save path (false) → non-aggregate flags; worker (true) → aggregate flags.
        var passFlagIds = flags
            .Where(f => rulesByFlag.TryGetValue(f.FlagId, out var fr) && FlagIsAggregate(fr) == aggregatePass)
            .Select(f => f.FlagId).ToHashSet();

        // On the save path, note whether any aggregate flag is in scope → enqueue a job.
        var anyAggregateInScope = flags.Any(f => rulesByFlag.TryGetValue(f.FlagId, out var fr) && FlagIsAggregate(fr));

        // Metrics only for the rules of flags this pass processes (avoids wasted DB work).
        var passRules = rules.Where(r => passFlagIds.Contains(r.FlagId)).ToList();
        var metrics = await ComputeMetricsAsync(passRules, tx, udf, ct);

        // Existing rows for this txn. Auto rows are engine-owned and rebuilt every
        // run (so removed/changed matches don't leave stale flags); manual rows are
        // never touched here.
        var existing = await _db.TransactionFlags
            .Where(tf => tf.TransactionId == transactionId)
            .ToListAsync(ct);

        foreach (var f in flags)
        {
            if (!passFlagIds.Contains(f.FlagId)) continue;
            if (!rulesByFlag.TryGetValue(f.FlagId, out var flagRules) || flagRules.Count == 0)
                continue;

            var eval = EvaluateFlag(flagRules, udf, tx.ProductId, f.RaisePerMatch, metrics, now, txnDate);

            // The desired Auto rows for this flag: PER-MATCH → one per matched value;
            // AGGREGATE → a single summary row. Empty when the flag didn't raise.
            var desired = eval.Hits;

            // Existing Auto rows for THIS flag, indexed by their matched value so we
            // can reuse a row in place (no insert+delete of the same key → no unique
            // -index collision within one SaveChanges).
            var existingAuto = existing.Where(tf => tf.FlagId == f.FlagId && tf.Source == "Auto").ToList();
            var byValue = new Dictionary<string, TransactionFlag>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in existingAuto)
                byValue.TryAdd(r.MatchedValue ?? "", r);
            var reused = new HashSet<TransactionFlag>();

            foreach (var hit in desired)
            {
                var value = Trunc(hit.MatchedValue, 400);
                if (byValue.TryGetValue(value, out var row) && reused.Add(row))
                {
                    row.IsFlagged       = true;
                    row.Source          = "Auto";
                    row.RuleId          = hit.RuleId;
                    row.MatchedValue    = value;
                    row.MatchProof      = eval.Proof;
                    row.SetBy           = userId;
                    row.SetDate         = now;
                    row.LastUpdatedBy   = userId;
                    row.LastUpdatedDate = now;
                }
                else
                {
                    _db.TransactionFlags.Add(new TransactionFlag
                    {
                        TransactionId = transactionId,
                        FlagId        = f.FlagId,
                        IsFlagged     = true,
                        Source        = "Auto",
                        RuleId        = hit.RuleId,
                        MatchedValue  = value,
                        MatchProof    = eval.Proof,
                        SetBy         = userId,
                        SetDate       = now,
                        // Audit columns are NOT NULL on TmX_Transaction_Flag — the
                        // manual differ sets these too. Omitting them made
                        // SaveChanges throw (the insert ran outside the caller's
                        // best-effort try/catch), 500-ing the whole transaction save.
                        CreatedBy     = userId,
                        CreatedDate   = now,
                    });
                }
                hits.Add(new AutoFlagHit { FlagId = f.FlagId, RuleId = hit.RuleId, MatchedValue = value });
            }

            // Drop any prior Auto rows for this flag we didn't reuse — covers
            // no-longer-matching flags and values removed from a per-match flag.
            foreach (var r in existingAuto)
                if (!reused.Contains(r))
                    _db.TransactionFlags.Remove(r);
        }

        // Save path: if any aggregate flag is in scope, enqueue one async job (coalesced —
        // at most one pending per transaction). The caller's SaveChanges commits it.
        if (!aggregatePass && anyAggregateInScope)
        {
            var alreadyPending = await _db.AutoFlagJobs
                .AnyAsync(j => j.TransactionId == transactionId && j.Status == "Pending", ct);
            if (!alreadyPending)
            {
                var code = await _data.ResolveCustomerCodeAsync(transactionId, ct);
                _db.AutoFlagJobs.Add(new AutoFlagJob
                {
                    TransactionId = transactionId,
                    CustomerCode  = code,
                    TriggerType   = "Save",
                    Status        = "Pending",
                    EnqueuedDate  = now,
                });
            }
        }

        if (hits.Count > 0)
            _log.LogInformation("Auto-flag engine raised {Count} flag-row(s) on transaction {Txn}.", hits.Count, transactionId);

        return hits;
    }

    public async Task<AutoFlagDryRun> DryRunAsync(int flagId, int transactionId, CancellationToken ct)
    {
        var tx = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);
        if (tx is null) return new AutoFlagDryRun { WouldRaise = false, Detail = "Transaction not found." };

        var perMatch = await _db.FlagCatalogues.AsNoTracking()
            .Where(f => f.FlagId == flagId).Select(f => f.RaisePerMatch).FirstOrDefaultAsync(ct);

        var udf   = await MergeKycUdfAsync(transactionId, await LoadUdfAsync(transactionId, ct), ct);
        var rules = await _db.FlagRules.AsNoTracking()
            .Where(r => r.FlagId == flagId && r.ActiveFlag)
            .ToListAsync(ct);
        if (rules.Count == 0) return new AutoFlagDryRun { WouldRaise = false, Detail = "No active rules configured for this flag." };

        var metrics = await ComputeMetricsAsync(rules, tx, udf, ct);
        var now = DateTime.UtcNow;   // reference "today" for date operators (dry-run)
        var txnDate = tx.TransactionDate ?? tx.CreatedDate;   // base for "txn" relative dates
        var eval = EvaluateFlag(rules, udf, tx.ProductId, perMatch, metrics, now, txnDate);
        var matchedRuleIds = rules.Where(r => EvaluateRule(r, udf, tx.ProductId, metrics, now, txnDate).ok).Select(r => r.RuleId).ToList();
        var detail = eval.Hits.Count > 0
            ? string.Join(perMatch ? "; " : " · ", eval.Hits.Select(h => h.MatchedValue))
            : "No rule group satisfied.";
        return new AutoFlagDryRun
        {
            WouldRaise    = eval.Raised,
            MatchedRules  = matchedRuleIds,
            Detail        = detail,
            PerMatch      = perMatch,
            MatchedValues = eval.Hits.Select(h => h.MatchedValue).ToList(),
            Proof         = eval.Proof,
        };
    }

    // ---- evaluation grammar: AND within Rule_Group, OR across groups ----
    // A flag's "hits" are the rows it should raise: PER-MATCH yields one hit per
    // matching (field × value); AGGREGATE yields a single summary hit. Either way
    // a structured JSON "proof" of the whole evaluation is produced for developers.
    private sealed record MatchHit(int? RuleId, string MatchedValue);
    private sealed record FlagEval(bool Raised, List<MatchHit> Hits, string Proof);

    private FlagEval EvaluateFlag(
        List<FlagRule> rules, IReadOnlyDictionary<string, List<string>> udf, int productId, bool perMatch,
        IReadOnlyDictionary<int, MetricResult> metrics, DateTime now, DateTime txnDate)
    {
        var hits        = new List<MatchHit>();
        var seenValues  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proofGroups = new List<object>();
        var aggParts    = new List<string>();
        int? aggRuleId  = null;

        foreach (var group in rules.GroupBy(r => r.RuleGroup).OrderBy(g => g.Key))
        {
            var ordered  = group.OrderBy(r => r.EvaluationOrder).ToList();
            var results  = ordered.Select(r => (rule: r, res: EvaluateRule(r, udf, productId, metrics, now, txnDate))).ToList();
            var satisfied = results.All(x => x.res.ok);

            proofGroups.Add(new
            {
                group      = group.Key,
                satisfied,
                conditions = results.Select(x => new
                {
                    field      = x.rule.LeftFieldCode,
                    source     = x.rule.LeftSource,
                    op         = x.rule.Operator,
                    rhsType    = x.rule.RightOperandType,
                    rhs        = x.rule.ReferenceListCode ?? x.rule.StaticValue ?? x.rule.RightFieldCode,
                    candidates = x.res.candidates,
                    matched    = x.res.matchedValues,
                    ok         = x.res.ok,
                }).ToList(),
            });

            if (!satisfied) continue;

            // This group fired. Its matched values are the union of its conditions'
            // matched values (each is "field=value"); fall back to the detail string.
            aggRuleId ??= results.First(x => x.res.ok).rule.RuleId;
            var groupRuleId = results.First(x => x.res.ok).rule.RuleId;
            var groupValues = results.SelectMany(x => x.res.matchedValues).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (groupValues.Count == 0)
                groupValues = new List<string> { string.Join(" AND ", results.Select(x => x.res.detail)) };

            aggParts.AddRange(results.Select(x => x.res.detail));

            if (perMatch)
            {
                foreach (var v in groupValues)
                    if (seenValues.Add(v))
                        hits.Add(new MatchHit(groupRuleId, v));
            }
        }

        var raised = aggParts.Count > 0 || hits.Count > 0;
        if (!perMatch && raised)
            hits.Add(new MatchHit(aggRuleId, string.Join(" · ", aggParts.Distinct())));

        var proof = JsonSerializer.Serialize(new
        {
            mode    = perMatch ? "PerMatch" : "Aggregate",
            verdict = raised ? "RAISED" : "NOT_RAISED",
            hits    = hits.Select(h => h.MatchedValue).ToList(),
            groups  = proofGroups,
        });

        return new FlagEval(raised, hits, proof);
    }

    private (bool ok, string detail, List<string> candidates, List<string> matchedValues) EvaluateRule(
        FlagRule r, IReadOnlyDictionary<string, List<string>> udf, int productId,
        IReadOnlyDictionary<int, MetricResult> metrics, DateTime now, DateTime txnDate)
    {
        var op = (r.Operator ?? "").Trim();
        var none = new List<string>();

        // Aggregate/Derived left operands are supplied by a metric handler (pre-computed
        // in ComputeMetricsAsync), not read from the UDF. A non-computable metric yields
        // no candidates → numeric/set comparisons resolve to not-satisfied (never throws).
        List<string> leftCandidates;
        string field;
        if (IsMetricSource(r.LeftSource))
        {
            field = r.HandlerKey ?? "metric";
            leftCandidates = (metrics.TryGetValue(r.RuleId, out var m) && m.Computable && m.Value is not null)
                ? new List<string> { m.Value } : new List<string>();
        }
        else
        {
            field = r.LeftFieldCode ?? "field";
            leftCandidates = ResolveCandidates(r.LeftSource, r.LeftFieldCode, udf);
        }
        var leftShown = leftCandidates.FirstOrDefault() ?? "∅";

        switch ((r.RightOperandType ?? "").Trim())
        {
            case "ReferenceList":
            {
                var listCode = r.ReferenceListCode ?? "";
                var matches = AllListMatches(listCode, leftCandidates);   // every value that hit the list
                var inList = matches.Count > 0;
                var ok = op switch
                {
                    "In"        => inList,
                    "NotIn"     => leftCandidates.Count > 0 && !inList,
                    "Equals"    => inList,
                    "NotEquals" => leftCandidates.Count > 0 && !inList,
                    _           => false,
                };
                // Positive operators name the matching value(s) as "field=value"
                // (legacy-style evidence). Negative operators have no matched value.
                var positive = op is "In" or "Equals";
                var matchedValues = ok && positive
                    ? matches.Select(m => $"{field}={m}").ToList()
                    : none;
                var detail = positive
                    ? $"{field}({(matches.FirstOrDefault() ?? leftShown)}) {op} {listCode}"
                    : $"{field}({leftShown}) {op} {listCode}";
                return (ok, detail, leftCandidates, matchedValues);
            }

            case "StaticValue":
            case "Threshold":
            {
                var rv = (r.StaticValue ?? "").Trim();
                var rvScaled = ApplyFactor(rv, r.RightFactor, r.RightFactorUnit);
                var ok = op switch
                {
                    "Equals"      => leftCandidates.Any(c => string.Equals(c, rv, StringComparison.OrdinalIgnoreCase)),
                    "NotEquals"   => leftCandidates.Count > 0 && !leftCandidates.Any(c => string.Equals(c, rv, StringComparison.OrdinalIgnoreCase)),
                    "In"          => SplitList(rv).Intersect(leftCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "NotIn"       => leftCandidates.Count > 0 && !SplitList(rv).Intersect(leftCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "Contains"    => leftCandidates.Any(c => c.Contains(rv, StringComparison.OrdinalIgnoreCase)),
                    "GreaterThan" => NumCompare(leftShown, rvScaled, (a, b) => a > b),
                    "LessThan"    => NumCompare(leftShown, rvScaled, (a, b) => a < b),
                    // Date operators: left field is a date; RHS is a relative expression
                    // (today|txn|field(Code) ± n D|M|Y) or a literal date. WithinDays: n D|M|Y.
                    "DateBefore"  => DateCompare(leftShown, rv, now, txnDate, udf, (l, refd) => l <  refd),
                    "DateAfter"   => DateCompare(leftShown, rv, now, txnDate, udf, (l, refd) => l >  refd),
                    "WithinDays"  => WithinDays(leftShown, rv, now),
                    _             => false,
                };
                var matchedValues = ok ? new List<string> { $"{field}={leftShown}" } : none;
                return (ok, $"{field}({leftShown}) {op} '{rvScaled}'", leftCandidates, matchedValues);
            }

            case "Field":
            {
                var rightCandidates = ResolveCandidates(r.RightSource ?? r.LeftSource, r.RightFieldCode, udf);
                var rightShown = rightCandidates.FirstOrDefault() ?? "∅";
                // Numeric operators compare against the (optionally factored) right field value —
                // e.g. aggregate volume > (Declared Turnover x 1.5 / x 60%).
                var rightScaled = ApplyFactor(rightShown, r.RightFactor, r.RightFactorUnit);
                var ok = op switch
                {
                    "Equals"      => leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "NotEquals"   => leftCandidates.Count > 0 && !leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "In"          => leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "NotIn"       => leftCandidates.Count > 0 && !leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "GreaterThan" => NumCompare(leftShown, rightScaled, (a, b) => a > b),
                    "LessThan"    => NumCompare(leftShown, rightScaled, (a, b) => a < b),
                    // Date field vs date field (e.g. BL date is before the LC expiry date).
                    "DateBefore"  => DateFieldCompare(leftShown, rightShown, (l, rr) => l <  rr),
                    "DateAfter"   => DateFieldCompare(leftShown, rightShown, (l, rr) => l >  rr),
                    _             => false,
                };
                var matchedValues = ok && op is "Equals" or "In" or "GreaterThan" or "LessThan" or "DateBefore" or "DateAfter"
                    ? new List<string> { $"{field}={leftShown}" } : none;
                var rhsShown = op is "GreaterThan" or "LessThan" ? rightScaled : rightShown;
                return (ok, $"{field}({leftShown}) {op} {r.RightFieldCode}({rhsShown})", leftCandidates, matchedValues);
            }

            default:
                // Aggregate / Derived right-operands are P2/P3 — not evaluated here.
                return (false, $"{field} {op} (unsupported operand type '{r.RightOperandType}')", leftCandidates, none);
        }
    }

    // ---- reference-list membership (normalized TmX_Reference_Type/_Value/_Map) ----
    /// <summary>Returns EVERY candidate value that is a member of the list (so per-match
    /// flags can fan out one row per matching value). Empty when none match.</summary>
    private List<string> AllListMatches(string listCode, List<string> candidates)
    {
        if (candidates.Count == 0 || string.IsNullOrWhiteSpace(listCode)) return new List<string>();
        var code = listCode.Trim().ToUpperInvariant();

        // Reference matching is FULLY DYNAMIC and type-agnostic: every reference list —
        // country, goods, port, or any custom category — is resolved the same way,
        // through the normalized TmX_Reference_Type/_Value/_Map model, keyed only by the
        // rule's code (== TmX_Reference_Type.Type_Code). Match is by Code == the lookup
        // Hidden_Value (design D1-a); ISO3 / synonyms / risk are Udf_Data, not matched.
        // There are no list- or domain-specific branches: adding a new category is pure
        // data (a new type + values), never code. An unknown code resolves to no members.
        var listValues = (from ty in _db.ReferenceTypes.AsNoTracking()
                          join m in _db.ReferenceValueTypeMaps.AsNoTracking() on ty.ReferenceTypeId equals m.ReferenceTypeId
                          join v in _db.ReferenceValues.AsNoTracking() on m.ReferenceValueId equals v.ReferenceValueId
                          where ty.TypeCode == code && ty.ActiveFlag && m.ActiveFlag && v.ActiveFlag
                                && candidates.Contains(v.Code)
                          select v.Code).ToList();

        // Prefer the candidate string (what the transaction carried) when it equals
        // the list's stored value case-insensitively; otherwise fall back to the list value.
        var result = new List<string>();
        foreach (var lv in listValues)
        {
            var shown = candidates.FirstOrDefault(c => string.Equals(c, lv, StringComparison.OrdinalIgnoreCase)) ?? lv;
            if (!result.Contains(shown, StringComparer.OrdinalIgnoreCase)) result.Add(shown);
        }
        return result;
    }

    // ---- operand resolution (Trade/Transaction udf field → candidate codes) ----
    private List<string> ResolveCandidates(string? source, string? fieldCode, IReadOnlyDictionary<string, List<string>> udf)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(fieldCode)) return result;

        // Trade/Transaction and KYC are read from the same transaction UDF (customer /
        // KYC sections are flattened into the same blob). Aggregate / Derived are P2/P3.
        var src = (source ?? "Trade").Trim();
        if (src is not ("Trade" or "Transaction" or "KYC")) return result;

        // A field code can carry MANY values — one per row of a repeating grid
        // section (HS-code lines, multiple countries/beneficiaries). Collect them
        // all; the rule evaluators use set membership / intersection, so "raise if
        // ANY row matches" falls out naturally.
        if (!udf.TryGetValue(fieldCode, out var raws) || raws is null || raws.Count == 0) return result;
        var rawVals = new List<string>();
        foreach (var raw in raws)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var rawVal = raw.Trim();
            Add(result, rawVal);
            if (!rawVals.Contains(rawVal, StringComparer.OrdinalIgnoreCase)) rawVals.Add(rawVal);
        }
        if (rawVals.Count == 0) return result;

        // Normalise via the field's lookup type. A dropdown stores the lookup ROW ID
        // (TmX_Lookup.Lookup_ID — the FE's getLookupId = option.id), e.g. "36402" for
        // Afghanistan, NOT the ISO code. So resolve the stored value against the
        // lookup's Id AND its Visible/Hidden value, then emit the Hidden (ISO code) +
        // Visible (name) so the value can be compared to the code/name reference lists.
        var lookupType = _db.TenantFieldSetups.AsNoTracking()
            .Where(f => f.FieldName == fieldCode && f.FieldLookupType != null)
            .Select(f => f.FieldLookupType)
            .FirstOrDefault();

        // Config-driven resolution: the field's Field_Lookup_Type names the TmX_Lookup
        // group this field draws from. Resolve the stored value WITHIN that group —
        // matched by row id (dropdowns store Lookup_ID, e.g. "36402"), or by its
        // visible/hidden value — and emit the row's HIDDEN value (the canonical code,
        // e.g. ISO "AF") plus its VISIBLE value (name) so it can be compared to the
        // code/name reference lists. No hardcoded field knowledge: a field with no
        // Field_Lookup_Type is treated as carrying a literal (its raw value stands).
        if (!string.IsNullOrWhiteSpace(lookupType))
        {
            var rawIds = new List<int>();
            foreach (var rv in rawVals) if (int.TryParse(rv, out var iv)) rawIds.Add(iv);

            var matched = _db.Lookups.AsNoTracking()
                .Where(l => l.LookupType == lookupType
                            && (rawIds.Contains(l.Id)
                                || rawVals.Contains(l.VisibleValue!)
                                || rawVals.Contains(l.HiddenValue!)))
                .Select(l => new { l.VisibleValue, l.HiddenValue })
                .ToList();
            foreach (var row in matched) { Add(result, row.HiddenValue); Add(result, row.VisibleValue); }
        }
        return result;

        static void Add(List<string> list, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v) && !list.Contains(v!.Trim(), StringComparer.OrdinalIgnoreCase))
                list.Add(v!.Trim());
        }
    }

    private async Task<IReadOnlyDictionary<string, List<string>>> LoadUdfAsync(int transactionId, CancellationToken ct)
    {
        var json = await _db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .Select(d => d.UdfData)
            .FirstOrDefaultAsync(ct);
        return FlattenUdf(json);
    }

    /// <summary>Overlay the customer's LATEST KYC record's UDF onto the transaction's own
    /// UDF, so a rule can compare a KYC operand (e.g. declared Line of Business) against a
    /// trade operand (e.g. Derived LOB) in the same evaluation (design O-2, option B).
    /// Trade fields win on any code collision; KYC-only fields (LineOfBus) are added.
    /// No KYC record → the transaction UDF is returned unchanged.</summary>
    private async Task<IReadOnlyDictionary<string, List<string>>> MergeKycUdfAsync(
        int transactionId, IReadOnlyDictionary<string, List<string>> txnUdf, CancellationToken ct)
    {
        var customerCode = await _data.ResolveCustomerCodeAsync(transactionId, ct);
        if (string.IsNullOrWhiteSpace(customerCode)) return txnUdf;
        var kycJson = await _data.GetLatestKycUdfAsync(customerCode!, ct);
        if (string.IsNullOrWhiteSpace(kycJson)) return txnUdf;
        var kycMap = FlattenUdf(kycJson);
        if (kycMap.Count == 0) return txnUdf;

        var merged = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in txnUdf) merged[kv.Key] = kv.Value;
        foreach (var kv in kycMap) if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;
        return merged;
    }

    /// <summary>Flatten a (possibly section-nested) UDF JSON object into field→values.
    /// Descends into BOTH nested objects AND arrays: repeating grid sections (HS-code
    /// lines, multiple countries/beneficiaries) store each row as an object inside an
    /// array, so every row's field values are collected under that field's code. A
    /// code therefore maps to the LIST of all values seen for it across the document.</summary>
    private static IReadOnlyDictionary<string, List<string>> FlattenUdf(string? json)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, map);
        }
        catch { /* tolerant: malformed udf → empty map */ }
        return map;

        static void Add(Dictionary<string, List<string>> map, string name, string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return;
            if (!map.TryGetValue(name, out var list)) { list = new List<string>(); map[name] = list; }
            var t = val.Trim();
            if (!list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);
        }

        static void Walk(JsonElement el, Dictionary<string, List<string>> map)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    {
                        switch (p.Value.ValueKind)
                        {
                            case JsonValueKind.Object: Walk(p.Value, map); break;
                            case JsonValueKind.Array:  Walk(p.Value, map); break; // descend into grid rows
                            case JsonValueKind.String: Add(map, p.Name, p.Value.GetString()); break;
                            case JsonValueKind.Number: Add(map, p.Name, p.Value.GetRawText()); break;
                            case JsonValueKind.True:
                            case JsonValueKind.False:  Add(map, p.Name, p.Value.GetRawText()); break;
                            default: break;
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray()) Walk(item, map); // each element (row object)
                    break;
                default: break;
            }
        }
    }

    private async Task<int?> ResolveAutoFlagTypeIdAsync(CancellationToken ct)
        => await _db.Lookups.AsNoTracking()
            .Where(l => l.LookupType == "FLAG_TYPE" && l.HiddenValue == "AUTOMATED")
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

    private static IEnumerable<string> SplitList(string csv)
        => csv.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool NumCompare(string a, string b, Func<decimal, decimal, bool> cmp)
        => decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var x)
           && decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var y)
           && cmp(x, y);

    /// <summary>Scale a numeric RHS by the rule's Right_Factor:
    /// value * (unit == "Percent" ? factor/100 : factor). Null factor or a non-numeric
    /// value returns the input unchanged (so existing rules are unaffected).</summary>
    private static string ApplyFactor(string value, decimal? factor, string? unit)
    {
        if (factor is null) return value;
        if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return value;
        var mult = string.Equals(unit, "Percent", StringComparison.OrdinalIgnoreCase) ? factor.Value / 100m : factor.Value;
        return (v * mult).ToString(CultureInfo.InvariantCulture);
    }

    // ---- date operators (WithinDays / DateBefore / DateAfter) ----
    /// <summary>Compare a left date value to a reference date resolved from the RHS
    /// expression (see <see cref="TryResolveRefDate"/>). Date-only granularity.</summary>
    private static bool DateCompare(string leftVal, string rhs, DateTime now, DateTime txnDate,
        IReadOnlyDictionary<string, List<string>> udf, Func<DateTime, DateTime, bool> cmp)
        => TryParseDate(leftVal, out var l)
           && TryResolveRefDate(rhs, now, txnDate, udf, out var refd)
           && cmp(l.Date, refd.Date);

    /// <summary>Compare two date field values (left vs right). Date-only granularity.</summary>
    private static bool DateFieldCompare(string leftVal, string rightVal, Func<DateTime, DateTime, bool> cmp)
        => TryParseDate(leftVal, out var l) && TryParseDate(rightVal, out var r) && cmp(l.Date, r.Date);

    /// <summary>TRUE when the left date is within N units of Today (either direction). RHS is
    /// "&lt;n&gt;&lt;unit&gt;" (unit D|M|Y, default D) — e.g. "7D", "6M"; a bare integer is days
    /// (back-compat, e.g. "7").</summary>
    private static bool WithinDays(string leftVal, string rhs, DateTime now)
    {
        if (!TryParseDate(leftVal, out var l)) return false;
        if (!TryParseOffset((rhs ?? "").Trim(), out var signed, out var unit)) return false;
        var n = Math.Abs(signed);
        var lo = ApplyDateOffset(now.Date, -n, unit);
        var hi = ApplyDateOffset(now.Date,  n, unit);
        return l.Date >= lo && l.Date <= hi;
    }

    /// <summary>Tolerant date parse (ISO + common formats; invariant then current culture).</summary>
    private static bool TryParseDate(string s, out DateTime d)
    {
        s = (s ?? "").Trim();
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d)
            || DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out d);
    }

    /// <summary>Resolve a date-operator RHS to a concrete reference date. Grammar:
    /// <c>&lt;base&gt;[(+|-)&lt;n&gt;&lt;unit&gt;]</c> — base = today|now|system|sysdate|systemdate
    /// (evaluation date), txn|transaction (transaction value date), field(&lt;Code&gt;) (a date field
    /// on this transaction), or a literal date; unit = D|M|Y (default D). Empty ⇒ today. The bare
    /// legacy form "today-180" (no unit ⇒ days) still parses.</summary>
    private static bool TryResolveRefDate(string rhs, DateTime now, DateTime txnDate,
        IReadOnlyDictionary<string, List<string>> udf, out DateTime refDate)
    {
        refDate = default;
        var s = (rhs ?? "").Trim();
        if (s.Length == 0) { refDate = now.Date; return true; }
        var norm = s.Replace(" ", "");

        // Peel base from optional offset. field(CODE) is peeled up to its ')'; a keyword base is
        // the leading run of letters; anything else is treated as a literal date.
        string basePart, offPart;
        if (norm.ToLowerInvariant().StartsWith("field(") && norm.Contains(')'))
        {
            var close = norm.IndexOf(')');
            basePart = norm[..(close + 1)];
            offPart  = norm[(close + 1)..];
        }
        else
        {
            int i = 0; while (i < norm.Length && char.IsLetter(norm[i])) i++;
            basePart = norm[..i];
            offPart  = norm[i..];
        }

        DateTime baseDate;
        var b = basePart.ToLowerInvariant();
        if (b is "today" or "now" or "system" or "sysdate" or "systemdate")
            baseDate = now.Date;
        else if (b is "txn" or "transaction")
            baseDate = txnDate.Date;
        else if (b.StartsWith("field(") && b.EndsWith(")"))
        {
            var code = basePart[6..^1];                     // preserve original case for the udf key
            if (!TryResolveFieldDate(code, udf, out baseDate)) return false;
        }
        else if (TryParseDate(s, out var lit)) { refDate = lit.Date; return true; }   // whole string is a literal date
        else return false;

        if (offPart.Length == 0) { refDate = baseDate; return true; }
        if (!TryParseOffset(offPart, out var signed, out var unit)) return false;
        refDate = ApplyDateOffset(baseDate, signed, unit);
        return true;
    }

    /// <summary>First parseable date value of a UDF field (case-insensitive key).</summary>
    private static bool TryResolveFieldDate(string code, IReadOnlyDictionary<string, List<string>> udf, out DateTime d)
    {
        d = default;
        if (!string.IsNullOrWhiteSpace(code) && udf.TryGetValue(code, out var vals) && vals != null)
            foreach (var v in vals)
                if (TryParseDate(v, out d)) return true;
        return false;
    }

    /// <summary>Parse an offset "+30" / "-180" / "+6M" / "-1Y" / bare "30" (days). Sign optional
    /// (default +). Returns the SIGNED count and the unit char (D|M|Y, default D).</summary>
    private static bool TryParseOffset(string s, out int signed, out char unit)
    {
        signed = 0; unit = 'D';
        s = (s ?? "").Trim();
        if (s.Length == 0) return false;
        int sign = 1, i = 0;
        if (s[0] is '+' or '-') { sign = s[0] == '-' ? -1 : 1; i = 1; }
        int start = i; while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i == start || !int.TryParse(s[start..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return false;
        if (i < s.Length)
        {
            var u = char.ToUpperInvariant(s[i]);
            if (u is not ('D' or 'M' or 'Y') || i + 1 != s.Length) return false;
            unit = u;
        }
        signed = sign * n;
        return true;
    }

    private static DateTime ApplyDateOffset(DateTime d, int signed, char unit) => unit switch
    {
        'M' => d.AddMonths(signed),
        'Y' => d.AddYears(signed),
        _   => d.AddDays(signed),
    };

    private static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;
}

using System.Globalization;
using System.Text.Json;
using FSI.Trade.Compliance.Application.Contracts.Flags;
using FSI.Trade.Compliance.Application.Contracts.Persistence;
using FSI.Trade.Compliance.Domain.Entities.Flags;
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

    public AutoFlagEvaluator(IApplicationDbContext db, ILogger<AutoFlagEvaluator> log)
    {
        _db  = db;
        _log = log;
    }

    public async Task<IReadOnlyList<AutoFlagHit>> EvaluateAndStageAsync(
        int transactionId, string userId, DateTime now, CancellationToken ct)
    {
        var hits = new List<AutoFlagHit>();

        var tx = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);
        if (tx is null) return hits;

        var udf = await LoadUdfAsync(transactionId, ct);
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

        // Existing rows for this txn. Auto rows are engine-owned and rebuilt every
        // run (so removed/changed matches don't leave stale flags); manual rows are
        // never touched here.
        var existing = await _db.TransactionFlags
            .Where(tf => tf.TransactionId == transactionId)
            .ToListAsync(ct);

        foreach (var f in flags)
        {
            if (!rulesByFlag.TryGetValue(f.FlagId, out var flagRules) || flagRules.Count == 0)
                continue;

            var eval = EvaluateFlag(flagRules, udf, tx.ProductId, f.RaisePerMatch);

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

        var udf   = await LoadUdfAsync(transactionId, ct);
        var rules = await _db.FlagRules.AsNoTracking()
            .Where(r => r.FlagId == flagId && r.ActiveFlag)
            .ToListAsync(ct);
        if (rules.Count == 0) return new AutoFlagDryRun { WouldRaise = false, Detail = "No active rules configured for this flag." };

        var eval = EvaluateFlag(rules, udf, tx.ProductId, perMatch);
        var matchedRuleIds = rules.Where(r => EvaluateRule(r, udf, tx.ProductId).ok).Select(r => r.RuleId).ToList();
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
        List<FlagRule> rules, IReadOnlyDictionary<string, List<string>> udf, int productId, bool perMatch)
    {
        var hits        = new List<MatchHit>();
        var seenValues  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proofGroups = new List<object>();
        var aggParts    = new List<string>();
        int? aggRuleId  = null;

        foreach (var group in rules.GroupBy(r => r.RuleGroup).OrderBy(g => g.Key))
        {
            var ordered  = group.OrderBy(r => r.EvaluationOrder).ToList();
            var results  = ordered.Select(r => (rule: r, res: EvaluateRule(r, udf, productId))).ToList();
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
        FlagRule r, IReadOnlyDictionary<string, List<string>> udf, int productId)
    {
        var op = (r.Operator ?? "").Trim();
        var leftCandidates = ResolveCandidates(r.LeftSource, r.LeftFieldCode, udf);
        var leftShown = leftCandidates.FirstOrDefault() ?? "∅";
        var field = r.LeftFieldCode ?? "field";
        var none = new List<string>();

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
                var ok = op switch
                {
                    "Equals"      => leftCandidates.Any(c => string.Equals(c, rv, StringComparison.OrdinalIgnoreCase)),
                    "NotEquals"   => leftCandidates.Count > 0 && !leftCandidates.Any(c => string.Equals(c, rv, StringComparison.OrdinalIgnoreCase)),
                    "In"          => SplitList(rv).Intersect(leftCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "NotIn"       => leftCandidates.Count > 0 && !SplitList(rv).Intersect(leftCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "Contains"    => leftCandidates.Any(c => c.Contains(rv, StringComparison.OrdinalIgnoreCase)),
                    "GreaterThan" => NumCompare(leftShown, rv, (a, b) => a > b),
                    "LessThan"    => NumCompare(leftShown, rv, (a, b) => a < b),
                    _             => false,
                };
                var matchedValues = ok ? new List<string> { $"{field}={leftShown}" } : none;
                return (ok, $"{field}({leftShown}) {op} '{rv}'", leftCandidates, matchedValues);
            }

            case "Field":
            {
                var rightCandidates = ResolveCandidates(r.RightSource ?? r.LeftSource, r.RightFieldCode, udf);
                var rightShown = rightCandidates.FirstOrDefault() ?? "∅";
                var ok = op switch
                {
                    "Equals"    => leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "NotEquals" => leftCandidates.Count > 0 && !leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "In"        => leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    "NotIn"     => leftCandidates.Count > 0 && !leftCandidates.Intersect(rightCandidates, StringComparer.OrdinalIgnoreCase).Any(),
                    _           => false,
                };
                var matchedValues = ok && op is "Equals" or "In"
                    ? new List<string> { $"{field}={leftShown}" } : none;
                return (ok, $"{field}({leftShown}) {op} {r.RightFieldCode}({rightShown})", leftCandidates, matchedValues);
            }

            default:
                // Aggregate / Derived right-operands are P2/P3 — not evaluated here.
                return (false, $"{field} {op} (unsupported operand type '{r.RightOperandType}')", leftCandidates, none);
        }
    }

    // ---- reference-list membership (existing REPO_* lists + custom lists) ----
    /// <summary>Returns EVERY candidate value that is a member of the list (so per-match
    /// flags can fan out one row per matching value). Empty when none match.</summary>
    private List<string> AllListMatches(string listCode, List<string> candidates)
    {
        if (candidates.Count == 0 || string.IsNullOrWhiteSpace(listCode)) return new List<string>();
        var code = listCode.Trim().ToUpperInvariant();

        List<string> listValues;
        if (IsCountryList(code))
        {
            listValues = _db.SanctionedCountries.AsNoTracking()
                .Where(c => candidates.Contains(c.IsoCountryCode)
                         || candidates.Contains(c.Iso3CountryCode)
                         || candidates.Contains(c.CountryName))
                .Select(c => c.IsoCountryCode)
                .ToList();
        }
        else if (IsGoodsList(code))
        {
            listValues = _db.ScreeningGoods.AsNoTracking()
                .Where(g => candidates.Contains(g.GoodCode))
                .Select(g => g.GoodCode)
                .ToList();
        }
        else
        {
            // Custom reference list (TmX_Reference_List / _Value).
            listValues = (from rl in _db.ReferenceLists.AsNoTracking()
                          join rv in _db.ReferenceListValues.AsNoTracking() on rl.ReferenceListId equals rv.ReferenceListId
                          where rl.ActiveFlag && rv.ActiveFlag && rl.ListCode == listCode && candidates.Contains(rv.Value)
                          select rv.Value).ToList();
        }

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

    private static bool IsCountryList(string code) =>
        code is "SANCTIONED_COUNTRIES" or "HIGH_RISK_COUNTRIES" or "HIGH_RISK_COUNTRY"
             or "COUNTRY_SANCTIONS" or "REPO_SANCTIONED_COUNTRIES";

    private static bool IsGoodsList(string code) =>
        code is "SCREENING_GOODS" or "HIGH_RISK_GOODS" or "HS_SCREENING"
             or "REPO_SCREENING_GOODS";

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

    private static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;
}

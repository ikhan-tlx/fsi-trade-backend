# Auto-Flag Rule Engine — Aggregate & Derived operands (design plan)

Status: **PLAN — for review before execution.** No code yet.

P1 (shipped) covers three rule archetypes on a single transaction's fields:
reference-list membership (`In`/`NotIn`), static/threshold compare, and field-vs-field
compare. The evaluator resolves a left field to candidate values (id → hidden/visible via
`Field_Lookup_Type`) and applies the operator. `Aggregate` and `Derived` are already
reserved as operand sources but currently evaluate to "not satisfied".

This plan adds the **Aggregate** and **Derived** archetypes.

---

## 1. Definitions (what each archetype is)

- **Derived** — a *single computed value* obtained from one or more fields of the **same
  transaction** via a fixed function. No collection/iteration.
  - Tenor (days) = `dateDiffDays(MaturityDate, IssueDate)`
  - Country-of-origin ≠ LC issuing country (boolean)
  - Third party ≠ beneficiary (boolean)
  - Amount ratio = `A / B`

- **Aggregate** — a *value computed over a collection* (the rows of a repeating grid on
  this transaction, or — later — a set of related transactions): count / sum / avg / min /
  max / count-distinct / duplicate-count / percent-variance.
  - Count of HS-code lines; Sum of invoice line amounts; Count distinct countries-of-origin
  - Duplicate invoice count (same invoice no. appears > 1×)
  - % variance between invoice total and LC amount

Both produce a scalar (number / date / boolean / string) that the **existing operators**
then compare (`>`, `<`, `is equal to`, `In`, …). So Aggregate/Derived change only how the
**left operand is produced** — the comparison grammar is unchanged.

---

## 2. How it plugs into the current model (no new tables)

`TmX_Flag_Rule` already has the columns we need:

- `Left_Source` = `'Aggregate'` or `'Derived'` (vs `'Trade'`/`'KYC'`).
- `Handler_Key` (nvarchar 60) — the **named metric** to compute, e.g. `TENOR_DAYS`,
  `PCT_VARIANCE`, `DUP_INVOICE_COUNT`, `DISTINCT_COUNTRY_COUNT`, `ORIGIN_NE_LC`.
- `Aggregate_Spec` (nvarchar max) — JSON parameters for that metric (which fields/grid,
  filters, window).
- `Operator` + `Static_Value` / `Right_*` — the comparison applied to the computed value.

So a rule like "Tenor > 180 days" is:
`Left_Source=Derived, Handler_Key=TENOR_DAYS, Aggregate_Spec={"from":"IssueDate","to":"MaturityDate"}, Operator=GreaterThan, Static_Value=180`.

"≥ 2 sanctioned countries on the doc" is:
`Left_Source=Aggregate, Handler_Key=COUNT_IN_LIST, Aggregate_Spec={"fields":["*_CntryOfOrigin","*_AppCountry"],"list":"SANCTIONED_COUNTRIES"}, Operator=GreaterThan, Static_Value=1`.

---

## 3. Architecture — a handler registry, NOT an expression DSL

**Recommendation: named-handler registry, not a free-form formula parser.** Reasons:
auditability (compliance wants deterministic, reviewable metrics), safety (no arbitrary
expression execution), and it matches the existing `Handler_Key` column. A formula DSL can
come later if real demand appears.

New contract:

```csharp
public interface IRuleMetricHandler
{
    string Key { get; }                 // e.g. "TENOR_DAYS"
    MetricKind Kind { get; }            // Derived | Aggregate
    string DataType { get; }           // number | date | boolean | code
    // Compute the left-operand value(s) for this rule on this transaction.
    MetricResult Compute(MetricContext ctx);   // ctx: udf map, txn, spec(JSON), services
}
```

- A `MetricHandlerRegistry` (DI, keyed by `Handler_Key`) is consulted in
  `AutoFlagEvaluator.EvaluateRule` when `Left_Source` is `Aggregate`/`Derived`: resolve the
  handler, `Compute` the value, then feed it into the **same** operator switch already used
  for static/number comparisons.
- `MetricContext` exposes: the flattened UDF (already built), the `Transaction`, the parsed
  `Aggregate_Spec`, and a `IRuleDataProvider` for anything beyond the current txn.
- Handlers that need only this transaction's data (grids/fields) are **intra-transaction**
  and run with no extra DB round-trips beyond the UDF we already load. That's Phase 3a.
- Handlers that need **other transactions / history** (duplicate-across-txns, customer
  velocity) use `IRuleDataProvider` (scoped queries). That's Phase 3b.

---

## 4. Handler catalogue — v1 (mapped to legacy flags)

Intra-transaction (Phase 3a):

| Handler_Key | Kind | Returns | Spec params | Legacy origin |
|---|---|---|---|---|
| `TENOR_DAYS` | Derived | number | `{from, to}` (date field codes) | tenor / maturity checks |
| `DATE_DIFF_DAYS` | Derived | number | `{from, to}` | generic |
| `FIELD_NOT_EQUAL` | Derived | boolean | `{left, right}` (field codes) | 3rd-party ≠ beneficiary |
| `ORIGIN_NE_LC` | Derived | boolean | `{originField(s), lcCountryField}` | "country of origin differs from LC" |
| `RATIO` / `PCT_VARIANCE` | Derived | number | `{numerator, denominator}` | "% variance >" |
| `COUNT_ROWS` | Aggregate | number | `{grid}` | count of HS lines |
| `SUM` / `AVG` / `MIN` / `MAX` | Aggregate | number | `{grid, field}` | total line amount |
| `COUNT_DISTINCT` | Aggregate | number | `{grid, field}` | distinct countries/goods |
| `COUNT_IN_LIST` | Aggregate | number | `{fields[], list}` | # sanctioned countries on doc |
| `DUPLICATE_COUNT` | Aggregate | number | `{grid, field}` | duplicate invoice no. (within txn) |

Cross-transaction (Phase 3b — needs `IRuleDataProvider`):

| Handler_Key | Returns | Spec params |
|---|---|---|
| `DUP_INVOICE_GLOBAL` | number | `{field, window, scope:customer\|global}` |
| `TXN_COUNT_WINDOW` | number | `{filter, window}` (customer velocity) |
| `SUM_AMOUNT_WINDOW` | number | `{amountField, window, scope}` |

The catalogue is data-light: adding a metric = add one handler class + register it. The FE
discovers available metrics from a `GET /AutoFlag/Metrics` endpoint (see §6) — nothing
hardcoded in the UI.

---

## 5. Operators

Reuse existing numeric/date/boolean operators on the computed value:
`GreaterThan`, `LessThan`, `Equals`, `NotEquals`, `WithinDays`, `DateBefore`, `DateAfter`,
and for booleans `Equals true/false`. Keep `PercentVarianceGt` as sugar that maps to
`Handler_Key=PCT_VARIANCE` + `GreaterThan`. The `AUTO_FLAG_OPERATORS` `dataTypes` already
carry an `aggregate` bucket; we extend the FE operator-filtering to key off the metric's
declared `DataType` instead.

---

## 6. FE authoring (the "Aggregate" / "Derived" SOON tabs)

- New BE read: `GET /api/v1/AutoFlag/Metrics` → the registered handlers
  `{ key, label, kind, dataType, params:[{name,label,type,fieldFilter}], description }`.
- The field-picker's **Aggregate** and **Derived** tabs become active and list these metrics
  (grouped by kind) instead of raw fields.
- Selecting a metric opens a small **param form** (driven by `params`) — e.g. pick the two
  date fields for `TENOR_DAYS`, or the grid + amount field for `SUM`. Field params reuse the
  existing field picker (filtered by `fieldFilter`, e.g. only date fields / only a grid).
- The chosen metric + params serialize to `Left_Source`, `Handler_Key`, `Aggregate_Spec`.
- Operator dropdown filters by the metric's `dataType` (number → `>`/`<`/`=`; boolean → is
  true/false; date → within/before/after).
- Plain-language preview + Test rows render the metric label (e.g. "Tenor (days) is greater
  than 180", "matched 210") — the existing proof/row renderer already supports this once the
  metric label is the "field" name.

---

## 7. Proof / traceability

Extend the `Match_Proof` per-condition node for metric operands to include the **inputs and
the computed value**, so the developer/QA proof shows *why* the metric had that value:

```json
{ "metric":"TENOR_DAYS", "inputs":{"IssueDate":"2026-01-01","MaturityDate":"2026-08-01"},
  "computed":212, "op":"GreaterThan", "rhs":"180", "ok":true }
```

This keeps the dynamic, self-explaining behaviour we built for P1.

---

## 8. Performance, safety, edge cases

- **Intra-txn metrics** run on the already-loaded UDF — negligible cost.
- **Cross-txn metrics** must use indexed, scoped queries with a bounded time window; cap
  result sets; cache per (customer, window) within one evaluation pass. Gate behind the
  per-flag scope so they only run where configured.
- **Null/again-missing inputs** → metric returns "not computable" → condition is `ok:false`
  (never throws), mirroring P1's tolerant resolution.
- **Per-match semantics**: aggregates are inherently one value → one hit (aggregate mode);
  `Raise_Per_Match` is ignored for aggregate metrics (documented). Derived booleans → one hit.
- **Number parsing/culture**: reuse the evaluator's invariant `NumCompare`.

---

## 9. Phasing

- **3a — Intra-transaction Derived + Aggregate** (no schema change): registry + context +
  the §4 intra-txn handlers + evaluator wiring + `GET /Metrics` + FE Aggregate/Derived tabs +
  proof. Delivers tenor, % variance, origin≠LC, count/sum/distinct, within-txn duplicate.
- **3b — Cross-transaction aggregates**: `IRuleDataProvider` + windowed queries + the
  velocity/duplicate-global handlers + any supporting indexes (new migration only here).
- **3c — (optional) formula DSL** if free-form expressions are ever needed; not planned now.

---

## 10. Decisions needed before execution

1. **Handler registry vs expression DSL** — recommend the registry (auditable, safe). Confirm.
2. **Cross-transaction scope now or later** — recommend 3a first (intra-txn), 3b as a
   follow-up; confirm whether duplicate-invoice / customer-velocity are needed in the first cut.
3. **Priority metric set** — which of the §4 handlers are must-have for the first release
   (suggest: `TENOR_DAYS`, `PCT_VARIANCE`, `ORIGIN_NE_LC`, `COUNT_IN_LIST`, `DUPLICATE_COUNT`).
4. **Does any aggregate need to fan out** (per-match) or is one-flag-per-aggregate always
   correct? (Recommend: aggregates are always single-hit.)

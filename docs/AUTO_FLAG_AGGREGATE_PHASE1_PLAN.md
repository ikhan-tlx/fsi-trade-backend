# Auto Red Flags — Phase 1 (Aggregates): Analysis & Implementation Plan

Status: **ANALYSIS / PLAN ONLY — no code to be written yet.**
Scope: **aggregate-based** auto flags. Derived single-value fields (tenor, date-diff,
field≠field, ratios) are *out of scope for build* here, but the shared machinery is designed
so Derived drops in later with no rework.

This document supersedes and consolidates the earlier notes
`AUTO_FLAG_AGGREGATIONS_ANALYSIS.md` and `AUTO_FLAG_AGGREGATE_DERIVED_PLAN.md`, updated to the
**current, verified** state of the code.

---

## 0. Where the implementation stands today (verified in code)

The engine already has the hooks for aggregates — they are inert, not missing:

- **Schema is ready.** `TmX_Flag_Rule` (migration `2026_06_040`) already carries
  `Aggregate_Spec` (nvarchar max, JSON) and `Handler_Key` (nvarchar 60). The `FlagRule`
  entity and `AutoFlagRuleDto` expose `aggregateSpec`; `HandlerKey` exists on the entity.
  **No migration is required for intra-transaction aggregates.**
- **Evaluator stubs are clean.** In `AutoFlagEvaluator.EvaluateRule`, `ResolveCandidates`
  early-returns an empty list for any `Left_Source` that is not `Trade`/`Transaction`/`KYC`,
  and the right-operand `switch` falls to a default that returns
  `false` with the message `"unsupported operand type"`. So Aggregate/Derived rules exist,
  save, and evaluate to *not-satisfied* — they never throw. We extend these two seams.
- **UDF is already flattened.** `LoadUdfAsync` → `FlattenUdf` produces
  `IReadOnlyDictionary<string, List<string>>` (`fieldCode → [values]`), where a repeating
  grid column already collapses to a multi-value list. **Count / distinct / duplicate over
  grid rows need no new data loading** — the collection is in hand.
- **Value normalisation is config-driven.** `ResolveCandidates` resolves dropdown row-ids to
  the lookup's hidden (code) + visible (name) via `Field_Lookup_Type`. Aggregates that filter
  by a reference list (e.g. count of sanctioned countries) reuse this + `AllListMatches`.
- **Per-match vs aggregate output already exists.** `EvaluateFlag(perMatch)` fans out one hit
  per matched value (per-match) or one summary hit (aggregate), and writes a JSON
  `Match_Proof`. Aggregate metrics are single-scalar → always single-hit.
- **FE is pre-wired for this.** The rule-builder field picker already renders **Trade / KYC /
  Aggregate / Derived** tabs with Aggregate & Derived **disabled + "SOON"**; `NOT_EVALUABLE_
  SOURCES = ["Aggregate","Derived"]` gates them; `AUTO_FLAG_OPERATORS` already has an
  `aggregate` dataType bucket and a `PercentVarianceGt` operator. Turning the feature on is
  largely *enabling and populating* these existing surfaces, not inventing them.

The single biggest architectural fact: **the values aggregates need (amount, counterparty,
invoice no., dates, countries) are not columns on `TmX_Transaction`** — they live inside
`TmX_Transaction_Detail.UDF_Data` (per-product JSON + repeating grids). The header only offers
`Product_Id`, `Company_Branch_Id`, `Currency_Id`, `Created_By`, `Transaction_Date`,
`Transaction_Status_Lkp`, `Sub_Product_Tab_Id`. Customer identity lives in the customer
snapshot/master, not on the transaction row. This forces the two-class split below.

---

## 1. Aggregate types / conditions referenced by the auto flags

Reading every `Auto` row's Logic column in `List of RedFlags.xlsx`, the distinct aggregate
operations required are:

| Aggregate operation | Meaning | Example flags (Red Flag #) |
|---|---|---|
| **Count (rows/events)** | number of items in a collection | #13 intermediaries, #15 amendments / tenor extensions, #16 resubmissions / urgent requests, #71 discount cycle |
| **Count over time window** | events of a type within N days/months | #15 (…within 6 months), #16 (…in 3 months), #40 (≥3 deviations / 90d), #57 repeated deviations |
| **Distinct count** | number of *distinct* values | # distinct sanctioned countries on a document, distinct counterparties/goods |
| **Filtered count (count-where)** | count of items matching a list/predicate | # HS lines in screening list, # sub-threshold txns (structuring) |
| **Sum** | total of a numeric column / over a window | #43 monthly & 90-day volume, #18 deposit inflow, line-item totals, cumulative exposure |
| **Average (historical)** | mean of prior values (basis for variance) | #21, #42, #53 "vs historical average" |
| **Min / Max** | extreme of a column | max single line amount; earliest/latest date |
| **% variance** | `abs(x − ref)/ref`, ref = benchmark or historical avg | #21, #42, #53 (>20%) |
| **Duplicate count** | max group size where `count(value) > 1` | duplicate invoice no. (within doc / across txns) |
| **Recency / gap (temporal)** | days since last transaction; dormancy | #39, #43, #56 dormancy then surge |
| **Absence-within-window** | expected event did **not** occur in N days | #68 no shipment vs advance /365d, #69 /120d |
| **Ratio vs KYC baseline** | aggregate ÷ declared turnover/expected | #18 (>40% turnover), #43 (>60% annual, >1.5× expected), #58 ESI vs volume |
| **First-time / existence** | historical count == 0 for a key | new counterparty / new corridor (implied by #40, #46, #57) |

Window vocabulary seen: rolling **60 / 90 / 120 / 180 / 365-day** windows, **3 / 6-month**
windows, and pattern **cycles** ("Cycle = 3"). All are *relative to the current transaction's
value date*.

---

## 2. Which auto flags depend on aggregates — and which do not

Two orthogonal splits matter: **(a) aggregate vs not**, and for the aggregate ones **(b)
intra-transaction (Class A) vs cross-transaction/historical (Class B)**. Class B is far more
expensive and gated on new storage (see §4).

### 2a. NOT aggregate-dependent (already covered by P1, or purely *derived* single-value)

These evaluate against a single transaction's fields / a reference list / another field, or a
single computed value — **no collection or window**. They are either live today or belong to
the (separate) Derived workstream, not Phase 1.

- Reference-list / field-vs-declared (P1-style, live or trivially configurable):
  **#12, #38, #41, #44, #45, #46, #47, #48, #50, #51, #60, #66, #70**, and No-Change
  **#25, #26, #27**.
- Derived single-value (deferred to the Derived workstream, not aggregates): **#14** (country-
  commodity score < threshold), **#49** (text-similarity score), **#52** (single amount vs
  declared turnover), **#62** (BL date future/pre-dated), **#63** (buyer country = seller,
  origin differs), **#64** (utilisation > LC×120%), **#65** (applicant ≠ issuing-bank country),
  **#22 / #25-Add** (post-transaction document comparison).

### 2b. Aggregate-dependent

**Class A — intra-transaction (aggregate over the current document's grid rows).** Cheap,
no new storage, first to build:

| Flag | Aggregate needed |
|---|---|
| **#13** | Count (or distinct count) of intermediary rows > 2 |
| "N distinct sanctioned countries on a document" (generalises seed `062`) | Distinct filtered count |
| line-count / duplicate-invoice / distinct-currency variants (support future flags) | Count rows / duplicate count / distinct count |

**Class B — cross-transaction / historical (aggregate over prior transactions grouped by
customer / counterparty / corridor over a window).** Needs the fact layer in §4:

| Flag | Aggregate needed |
|---|---|
| **#15** | Count over 6-month window (product-type changes, amendments, tenor extensions) |
| **#16** | Count over 3-month window (resubmissions, urgent requests) |
| **#18** | Sum of deposit inflow + ratio vs declared turnover |
| **#21** | % variance vs **historical average** of comparable transactions |
| **#39** | Recency/gap (≥180d dormant) **then** ratio vs turnover |
| **#40** | Count of geography-deviation events ≥3 within 90 days |
| **#42** | % variance vs historical average (>20%) |
| **#43** | Sum over monthly & 90-day windows vs declared turnover; 60-day dormancy |
| **#53** | Average / historical pricing comparison |
| **#56** | Dormancy then surge (recency + sum/count) |
| **#57** | Count of deviations across transaction history (pattern) |
| **#58** | Aggregate trade volume feeding ESI comparison |
| **#59** | Count of performance events (overdue bills, dishonoured payments) |
| **#68** | Absence-within-window: no shipment within 365 days of advance |
| **#69** | Absence-within-window: no shipment/repatriation within 120 days |
| **#71** | Count of repeated discount cycles (same exporter+importer, cycle=3) |

Rough split: ~**3 Class A** (plus reusable variants) and ~**15 Class B** aggregate flags; the
remainder are non-aggregate or derived.

---

## 3. Frontend changes required

The FE already has the shell; Phase 1 fills it in. Backward compatibility is inherent — every
change is additive to the existing rule-builder.

**3.1 Metric discovery (config-driven, nothing hardcoded).** Add an RTK Query
`getAutoFlagMetrics` hitting a new `GET /api/v1/AutoFlag/Metrics`, returning the registered
aggregate handlers: `{ key, label, kind, dataType, description, params:[{name,label,type,
fieldFilter,required}] }`. The picker renders from this — new metrics appear with zero FE
edits.

**3.2 Activate the Aggregate tab.** Remove the `disabled`/`SOON` gating for `Aggregate` in the
field-picker tab list (`AutoFlagEditorPage.tsx`, ~lines 651-654) and drop `Aggregate` from
`NOT_EVALUABLE_SOURCES`/`NOT_EVALUABLE_RIGHT`. Leave `Derived` on SOON until its workstream.
The tab lists metrics grouped by kind instead of raw fields.

**3.3 Metric parameter form.** Selecting a metric opens a small param panel driven by
`params`: field params reuse the **existing field picker** filtered by `fieldFilter` (e.g.
only numeric grid columns for `SUM`, only a grid root for `COUNT_ROWS`, a reference list for
`COUNT_IN_LIST`); window params render as a number + unit (days/months); list params reuse the
reference-list dropdown. The panel serialises to `Left_Source='Aggregate'`,
`Handler_Key=<metric>`, `Aggregate_Spec=<JSON>`. No free-text JSON is ever shown to the user.

**3.4 Operator filtering.** The condition's operator list already filters by dataType
(`AUTO_FLAG_OPERATORS.filter(o => o.dataTypes.includes(dt))`). Drive `dt` off the **metric's
declared `dataType`** (number → `>`,`<`,`=`; date → within/before/after; boolean → is
true/false). Keep `PercentVarianceGt` as sugar for the `PCT_VARIANCE` metric + `>`.

**3.5 Plain-language preview + Test panel.** The existing preview and dry-run proof renderer
key off the "field" label; feed the metric's label so a rule reads e.g. *"Count of
intermediaries is greater than 2 → matched 4"*. The Test panel already shows `proof`; extend
it to show the metric inputs + computed value (see §4.7).

**3.6 Validation (client-side).** Required params present; window > 0; numeric threshold
parses; operator compatible with metric dataType; a Class B metric may show an informational
note ("evaluated across this customer's history"). Block save on missing required params with
inline errors, matching current condition validation.

**3.7 Authoring guardrails.** Surface metric `kind` (Intra-transaction vs Historical) and,
for historical, a hint that scope + status filters apply. Disable `Raise_Per_Match` for rules
whose left source is an aggregate (aggregates are single-hit — see §4.6) and explain why.

FE files touched: `auto-flags-api.ts` (new query + `MetricDef` type + operator tweaks),
`AutoFlagEditorPage.tsx` (tab enable, param form, operator wiring, preview), `index.css`
(param-form styles). All logged via `FE_CHANGES_REQUIRED.md` per the repo workflow.

---

## 4. Backend changes required

### 4.1 Handler registry (the core abstraction) — recommended over an expression DSL

Introduce a **named-metric handler registry**, not a free-form formula parser. Rationale:
compliance needs deterministic, auditable, reviewable metrics; no arbitrary code/expression
execution; and it matches the `Handler_Key` column already present.

```csharp
public interface IRuleMetricHandler {
    string     Key      { get; }        // "COUNT_ROWS", "PCT_VARIANCE", "TXN_COUNT_WINDOW"…
    MetricKind Kind     { get; }        // Aggregate | Derived
    string     DataType { get; }        // number | date | boolean | code
    MetricParamDef[] Params { get; }    // for GET /Metrics + validation
    Task<MetricResult> ComputeAsync(MetricContext ctx, CancellationToken ct);
}
```

- `MetricContext` exposes the already-loaded flattened UDF, the `Transaction` header, the
  parsed `Aggregate_Spec` (JSON), and — for Class B — an `IRuleDataProvider` for scoped
  historical queries. `MetricResult` = `{ Computed (scalar), Inputs (for proof),
  Computable (bool) }`.
- A `MetricHandlerRegistry` (DI, keyed by `Handler_Key`) is consulted from
  `EvaluateRule` when `Left_Source` is `Aggregate`/`Derived`: resolve handler → `ComputeAsync`
  → feed the scalar into the **same operator switch** already used for Static/Threshold
  comparisons. This is a *small, contained* change to the two existing seams (§0).
- Registration is one line per handler; `GET /Metrics` reflects the registry. **Adding a
  metric never touches the evaluator core or the FE.**

### 4.2 Data model

- **Class A: no schema change.** Handlers read `MetricContext.Udf`. Ships on the existing
  migration `040` columns.
- **Class B: a materialised fact/extract layer** — `TmX_Transaction_Fact`, one row per
  transaction, populated at transaction-save (where the UDF is already in hand):
  `Transaction_Id`, `Customer_Id`, `Counterparty_Key`, `Company_Branch_Id`, `Product_Id`,
  `Value_Date`, `Status_Lkp`, `Amount`, `Currency_Id`, `Amount_Base_Ccy`, `Invoice_No_Norm`,
  `Touches_Sanctioned_Country` (bit), `Distinct_Country_Count`, plus extension columns as new
  Class B flags arrive. Indexes: `(Customer_Id, Value_Date)`, `(Counterparty_Key, Value_Date)`,
  `(Invoice_No_Norm)`. Idempotent upsert per `Transaction_Id`; one-time backfill from existing
  transactions. This isolates all "JSON → columns" complexity to **one extractor** instead of
  every rule, and turns each Class B metric into a single indexed `COUNT/SUM/AVG` query.

### 4.3 APIs

- `GET /api/v1/AutoFlag/Metrics` — registry catalogue for the FE (`AutoFlags.View`).
- Create/Update already accept `aggregateSpec` + rules; extend server-side validation to
  check `Handler_Key` exists in the registry and `Aggregate_Spec` matches the handler's
  `Params` (types/required). Reject unknown handler keys with a clear error.
- `POST /{id}/Test` (dry-run) already read-only — it will now also exercise metric handlers
  (Class B in "as of now" mode; see §4.5) and surface the computed value in `proof`.

### 4.4 Aggregation logic

- **Class A** handlers operate on the in-memory UDF (`O(rows)`), reusing `FlattenUdf`,
  `AllListMatches`, and the id→code normalisation. Examples: `COUNT_ROWS`, `COUNT_DISTINCT`,
  `COUNT_IN_LIST`, `DUPLICATE_COUNT`, `SUM/AVG/MIN/MAX` over a grid column.
- **Class B** handlers issue **one scoped SQL aggregate** against `TmX_Transaction_Fact`
  filtered by grouping key + `Value_Date >= @from` + status filter. Examples:
  `TXN_COUNT_WINDOW`, `SUM_AMOUNT_WINDOW`, `AVG_AMOUNT_WINDOW`, `DUP_INVOICE_GLOBAL`,
  `DAYS_SINCE_LAST_TXN`, `EVENT_ABSENT_WITHIN` (advance-vs-shipment).

### 4.5 Scheduler / jobs — is a background job needed?

**Mostly no.** The engine runs **synchronously at transaction save** (the existing
`EvaluateAndStageAsync` call in `UpdateTransactionCommandHandler`) and on demand via Test.
Class A and query-based Class B both fit that synchronous path — a windowed indexed aggregate
is milliseconds. Two cases *do* need scheduling and should be called out as sub-scope:

1. **Time-passing / absence flags (#68, #69, #39 dormancy).** These fire because *nothing*
   happened for N days — there is no transaction-save event to trigger on. They need a **daily
   scheduled sweep** that re-evaluates open advances / dormant customers. The repo already
   runs hosted background services (e.g. `FccmCaseIdPoller`, `LogArchiverService`), so add a
   `AutoFlagTemporalSweep` `IHostedService` on the same pattern. Recommend deferring these to
   **Phase 1c**.
2. **Fact-layer backfill** — a one-time job, not recurring.

### 4.6 Per-match semantics

An aggregate yields a **single scalar** → **one hit per (transaction, flag)**. `Raise_Per_
Match` is not applicable to aggregate rules; document it and have the evaluator force
aggregate-mode for aggregate left-sources (FE also disables the toggle — §3.7).

### 4.7 Proof / traceability

Extend the per-condition `Match_Proof` node for metric operands to include inputs + computed
value, preserving the self-explaining behaviour built for P1:

```json
{ "metric":"TXN_COUNT_WINDOW", "inputs":{"customerId":123,"windowDays":90,"status":"Active"},
  "computed":4, "op":"GreaterThan", "rhs":"2", "ok":true }
```

### 4.8 Performance considerations

- Class A: negligible (in-memory over already-loaded UDF).
- Class B: one indexed range-aggregate per rule; **cache per (grouping-key, window) within a
  single evaluation pass** so multiple rules over the same customer/window don't re-query;
  cap/bounded windows; only run where the flag's product/sub-product **scope** matches (scope
  filter already exists). Evaluate Class B rules **after** cheap Class A/reference rules within
  a group so an already-failing AND-group short-circuits before hitting the DB.
- Backfill and the daily temporal sweep run off-peak and are batched.

### 4.9 Validation (backend)

Unknown `Handler_Key` → reject at save. `Aggregate_Spec` schema-checked against handler
`Params`. Non-computable inputs at evaluation (nulls, mixed currency without FX, missing
grid) → `Computable=false` → condition `ok:false` (never throws), mirroring P1's tolerant
resolution, with the reason captured in proof.

---

## 5. Additional aggregate capabilities to build in now (scalability / reuse)

Not all are demanded by a single flag, but adding them to the registry/spec shape now avoids
rework and covers likely future flags:

1. **Generic window unit** in the spec (`days|months|txns`) and a **status filter** default
   (exclude Draft/Cancelled/Discarded) — needed by nearly every Class B flag; make it a shared
   spec fragment, not per-handler.
2. **Grouping-key selector** (`customer | counterparty | corridor(origin+dest) | branch |
   global`) so one `TXN_COUNT_WINDOW` / `SUM_AMOUNT_WINDOW` handler serves many flags via spec,
   not many near-duplicate handlers.
3. **Filtered aggregate (predicate)** — `COUNT/SUM WHERE field {op} value/list` — powers
   structuring (#18/#43 sub-threshold), screened-line counts, and "count of high-risk
   corridor" without new handlers.
4. **Percent-of-total / concentration** (`share of window sum to one counterparty`) — reused
   by counterparty-concentration patterns.
5. **Distinct-count and duplicate-count as first-class** (not just count) — many flags hinge
   on distinctness/repetition.
6. **Recency & absence primitives** (`DAYS_SINCE_LAST`, `EVENT_ABSENT_WITHIN`) — reused by all
   dormancy/advance-shipment flags; pairs with the temporal sweep.
7. **Baseline-ratio primitive** (`aggregate ÷ KYC-declared value`) — turnover/expected-volume
   comparisons (#18/#43/#52/#58) share one shape.
8. **Point-in-time flag** on the spec (`asOf: txnDate | now`) so saved evaluations are
   reproducible while Test uses "now".
9. **Min-sample guard** (`minPriorTxns`) for average/deviation/first-time metrics to avoid
   cold-start false positives.
10. **Composite metric key** so a Class A boolean (e.g. `Touches_Sanctioned_Country`) can be
    persisted to the fact layer and reused as a Class B aggregate input (#40, repeated
    sanctioned-country exposure) — design the extractor to write these derived bits.

Deliberately deferred: a free-form **expression DSL** (only if real demand appears — the
registry covers the sheet), and ML/NLP scores (#49 text similarity, #14 country-commodity
score) which are Derived+external, not aggregates.

---

## 6. Dependencies, assumptions, risks

**Dependencies**
- **Stable `Customer_Id`** resolved into the fact layer — the header has none; identity is in
  the customer snapshot/master. This is the single biggest prerequisite for all of Class B.
- **Base currency + FX-rate source** for any Sum/Avg/Max across mixed currencies (#18, #21,
  #42, #43, #53) — needs an `Amount_Base_Ccy` and a rate provider.
- **Normalised `Counterparty_Key` / `Invoice_No_Norm`** (trim/upper/strip punctuation) for
  concentration / duplicate-across-txns / first-time flags.
- **Field/grid identification per product** — which UDF grid/column is "amount",
  "intermediary", "invoice no." differs per product; the metric spec must name field codes
  (authored via the field picker), not assume fixed names.
- Fact-layer **backfill** from existing transactions; **daily temporal sweep** hosted service
  for absence/dormancy flags.

**Assumptions**
- Aggregates are single-hit (no per-match) — confirmed by the scalar nature of the metrics.
- Windows are relative to the current transaction's value date; saved evaluations are
  "as-of-txn", Test is "as-of-now" (make configurable via spec — §5.8).
- The synchronous save-time path is acceptable latency for query-based Class B (indexed
  aggregates); temporal-absence flags move to the scheduled sweep.
- Class A intra-transaction ships with **no schema change**; only Class B adds
  `TmX_Transaction_Fact`.

**Risks / mitigations**
- **Currency mixing** silently wrong sums → gate value-aggregates on `Amount_Base_Ccy`; mark
  non-computable when FX missing (proof shows why). *Defer Sum/Avg value flags until FX is in.*
- **Point-in-time reproducibility** — Test vs saved divergence confuses reviewers → make
  `asOf` explicit and label it in the UI/proof.
- **Cold start / thin history** → false positives on avg/first-time → `minPriorTxns` guard.
- **Counterparty/invoice free-text** → mis-grouping → invest in normalisation; start with
  count/velocity flags that need the least normalisation (#15, #16, #40, #71) before
  concentration/duplicate-global.
- **Fact-layer drift** vs source of truth → idempotent upsert at save + periodic reconcile;
  the fact row is a projection, never authoritative.
- **Performance under many Class B rules** → per-pass caching by (key, window), scope-gating,
  and ordering Class B after cheap conditions in an AND-group.
- **Backfill correctness** on legacy JSON variants → validate extractor against a sample per
  product before enabling Class B flags.

---

## 7. Recommended phasing (aggregates)

- **Phase 1a — Registry + Class A (no schema change).** `IRuleMetricHandler` +
  `MetricHandlerRegistry` + `MetricContext`, evaluator wiring at the two seams,
  `GET /Metrics`, FE Aggregate tab + param form + proof. Ships `COUNT_ROWS`,
  `COUNT_DISTINCT`, `COUNT_IN_LIST`, `DUPLICATE_COUNT`, `MAX` (defer value `SUM/AVG` pending
  FX). Delivers **#13** and generalises the seed sanctioned-country flag; immediate value,
  reuses all P1 plumbing.
- **Phase 1b — Fact layer + query-based Class B.** `TmX_Transaction_Fact` + extractor +
  backfill + indexes + `IRuleDataProvider` + windowed count/sum/avg/duplicate handlers +
  grouping-key/window/status/predicate spec fragments. Start with count/velocity (#15, #16,
  #40, #57, #71), then FX-dependent value flags (#18, #43, #21, #42, #53).
- **Phase 1c — Temporal sweep** hosted service for absence/dormancy (#68, #69, #39, #56).
- **Deferred (separate workstream):** Derived single-value metrics; expression DSL; ML/NLP
  scores.

**Decisions needed before build:** (1) confirm handler-registry over DSL; (2) canonical
`Customer_Id` source; (3) base currency + FX source (or ship Phase 1a count-only first); (4)
which Class B flags are in the first cut; (5) as-of-txn vs as-of-now default for saved
evaluations.

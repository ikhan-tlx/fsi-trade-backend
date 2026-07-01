# Auto Red Flags — Phase 1 (Aggregate Engine): Implementation Plan

Status: **DRAFT FOR APPROVAL.** No code until sign-off.

This plan supersedes the architecture sections of `AUTO_FLAG_AGGREGATE_PHASE1_PLAN.md`
where they differ — it reflects the decisions taken in review: **no fact table**,
**compute-on-read**, **async job queue**, **event-driven only (no scheduler)**, **numeric
window**, and **FCCM owns pure-absence flags**.

---

## 1. Objective

Turn the inert Aggregate operand into a working, **generic** aggregate engine so compliance
authors can build aggregate-based auto flags (volume, velocity, counts, recency) entirely
through configuration — no per-flag code. Prove it end-to-end with a starter set of flags.

## 2. Locked design decisions (from review)

1. **Generic metric-handler registry**, not an expression DSL. One configurable `AGGREGATE`
   handler parametrised by function — adding a function/flag is config, not code.
2. **Compute-on-read. No fact table.** Aggregates are computed at runtime from the source
   transactions; nothing denormalised is persisted, so nothing can drift out of sync.
3. **Async job queue.** On transaction save, if an aggregate flag applies to the product/
   sub-product, enqueue a job; a background worker computes the aggregate and evaluates the
   aggregate flags. Non-aggregate flags stay **synchronous at save** (unchanged).
4. **Event-driven only — no daily scheduler in Phase 1.** Dormancy is detected on the
   customer's *next* transaction via a recency metric. Pure "never returns" absence (#68/#69)
   is **owned by Oracle FCCM** and deliberately out of scope here (avoids duplicate/closed-case
   noise).
5. **Numeric window input** (positive integer days), not a dropdown.
6. **Window anchored to the transaction's value date**, so results are deterministic and
   reproducible regardless of when the worker runs.
7. **Grouping defaults to Customer** (implicit: "this transaction's customer"); the grouping
   control is hidden/advanced and only surfaced for the few metrics that group differently.
8. **Right operand supports `field × factor`**, where the factor is a **multiplier _or_ a
   percentage** — "> KYC field × 1.5" or "> KYC field × 60%". Stored as a value + unit,
   normalised to a decimal multiplier at evaluation.
9. **Grouping key = `Customer_Code`** resolved via `TmX_Customer_Master` (one snapshot row per
   `Transaction_Id`; the same `Customer_Code` repeats across a customer's transactions). A
   customer's window = all transactions whose snapshot shares that `Customer_Code`.
10. **Aggregate field picker is function-driven and numeric-first.** For value functions
    (**Sum / Avg / Min / Max**) only **number / amount** fields are selectable. **Count / Count
    Distinct** may target a non-numeric field or "rows" (e.g. #13 counts intermediary rows),
    so they are the deliberate exception — see the confirmation in §11.

## 3. Scope

**In scope (Phase 1)**
- Generic `AGGREGATE` operand: functions **Count, Count Distinct, Sum, Avg, Min, Max**, in two
  modes — *intra-transaction* (over the current document's grid rows) and *cross-transaction*
  (over the customer's window), both compute-on-read.
- **Recency** metric (`DAYS_SINCE_LAST_ACTIVITY`) for dormancy-then-activity flags.
- Right operand `field × factor` (new `Right_Factor`).
- Async job queue + worker; per-flag sync/async classification; event-driven enqueue with a
  cached product gate.
- FE authoring for aggregate conditions (activate the existing Aggregate tab).
- `GET /api/v1/AutoFlag/Metrics` discovery endpoint.
- Per-product **amount-field mapping** config (which UDF field is "amount"), needed for
  Sum/Avg value flags.
- **Customer key** resolution for grouping.
- Prove with flags: **#13** (intra-txn count of intermediaries), **#43** (cumulative volume
  vs turnover — the canonical example), and one **recency** dormancy flag (#39/#56 shape).

**Out of scope (deferred / owned elsewhere)**
- Fact table / materialisation (revisit only if compute-on-read latency proves inadequate; add
  as an optional lazy per-customer cache inside the worker, never a synchronised store).
- Daily scheduler and pure-absence flags **#68/#69** → FCCM.
- Workflow-event counts **#15/#16** (amendments, resubmissions, urgent requests) → pending a
  queryable event source; not transaction data today.
- Derived single-value workstream (tenor, date compares, ratios) → separate track.
- Cross-currency FX normalisation beyond booking/base currency (documented limitation; count/
  recency flags are unaffected).
- ML/NLP scores (#14 country-commodity, #49 text similarity).

## 4. Data model changes

- **`TmX_Flag_Rule`** — add nullable **`Right_Factor` (decimal(18,4), null)** and
  **`Right_Factor_Unit` (nvarchar 12, null: `Multiplier`|`Percent`)**. Null ⇒ ×1 (backward
  compatible). At evaluation the RHS = `fieldValue × (unit == Percent ? factor/100 : factor)`
  — so `60`/`Percent` ⇒ ×0.60 and `1.5`/`Multiplier` ⇒ ×1.5, and the UI can round-trip the
  author's original entry faithfully. `Aggregate_Spec` and `Handler_Key` already exist
  (migration 040) — no change. Aggregate config lives in `Aggregate_Spec` JSON:
  ```json
  { "function":"Sum", "field":"<UDF field code>", "windowDays":90,
    "mode":"CrossTransaction", "groupBy":"Customer", "statusFilter":"Active",
    "asOf":"ValueDate", "includeSelf":true }
  ```
  Intra-transaction count example: `{ "function":"Count", "field":"<grid field>", "mode":"IntraTransaction" }`.
- **`TmX_AutoFlag_Job`** (new) — the work queue:
  `Job_Id`, `Transaction_Id`, `Customer_Id` (nullable, resolved), `Trigger_Type` (Save),
  `Status` (Pending|Processing|Done|Failed|DeadLetter), `Attempts`, `Enqueued_Date`,
  `Claimed_Date`, `Processed_Date`, `Error`, plus a **unique index on `(Transaction_Id)` for
  pending rows** (coalesce duplicates). Index `(Status, Enqueued_Date)` for the worker claim.
- **`TmX_Product_Aggregate_Field_Map`** (new, small config) — `Product_Id`, `Role`
  (`Amount`|`Counterparty`|…), `Field_Code`. Tells the engine which UDF field is the amount
  (etc.) per product. Seed for the products used by the Phase-1 flags.
- **Customer key** — resolve `Customer_Code` for the transaction from `TmX_Customer_Master`
  (`Transaction_Id → Customer_Code`) at enqueue time and store it on the job row. The worker's
  cross-transaction window = all `TmX_Customer_Master` rows sharing that `Customer_Code`,
  joined to `TmX_Transaction` (value date, status) and `TmX_Transaction_Detail` (amount UDF).
  No new customer table needed.

## 5. Backend components

1. **Metric registry** — `IRuleMetricHandler { Key; Kind; DataType; Params; ComputeAsync(ctx) }`
   + `MetricHandlerRegistry` (DI, keyed by `Handler_Key`). `MetricContext` exposes the loaded
   UDF, the transaction header, the parsed `Aggregate_Spec`, and an `IRuleDataProvider` for
   scoped historical queries.
2. **`AggregateMetricHandler`** (`Handler_Key="AGGREGATE"`) — reads `function`/`mode`:
   - *IntraTransaction*: computes over the flattened UDF (reuse `FlattenUdf`, list/id
     normalisation). O(rows).
   - *CrossTransaction*: resolve `Customer_Code`, gather the customer's transactions in
     `[valueDate−windowDays, valueDate]` (join `TmX_Customer_Master` on `Customer_Code` →
     `TmX_Transaction` for date/status) filtered by status; for value functions, extract the
     amount from each `TmX_Transaction_Detail.UDF_Data` via the per-product field map
     (compute-on-read); aggregate. Cache the windowed set per `(customerCode, window)` within
     one worker pass.
3. **`RecencyMetricHandler`** (`DAYS_SINCE_LAST_ACTIVITY`) — max prior value-date for the
   customer vs the current transaction's value date.
4. **Evaluator wiring** — in `EvaluateRule`, when `Left_Source ∈ {Aggregate}`, resolve the
   handler → `ComputeAsync` → feed the scalar into the existing operator switch. Extend the
   right-operand builder to apply `Right_Factor` to a KYC/field value. Extend `Match_Proof`
   with `{ metric, inputs, computed, op, rhs, ok }`.
5. **Per-flag classification** — a flag is **async** if any of its rules has an aggregate/
   cross-transaction source; otherwise **sync** (evaluated at save as today). Mixed flags are
   evaluated entirely in the async pass.
6. **Enqueue at save** — after the existing synchronous pass, if a cached lookup says the
   transaction's product/sub-product has ≥1 active **async** flag, insert a `TmX_AutoFlag_Job`
   (coalesced). The cached lookup is invalidated on flag config changes.
7. **Background worker** — an `IHostedService` (same pattern as `FccmCaseIdPoller`) that:
   claims a pending job with a lease (`UPDATE … OUTPUT` + `READPAST`), runs the async flags for
   that transaction through the evaluator, stages `TmX_Transaction_Flag` (idempotent rebuild —
   existing staging logic), marks Done; on error increments `Attempts` with backoff and
   dead-letters after N.
8. **Endpoints** — `GET /api/v1/AutoFlag/Metrics` (registry catalogue for the FE); Create/
   Update validation extended to check `Handler_Key` exists and `Aggregate_Spec` matches the
   handler params; dry-run/Test exercises handlers (as-of value date) and returns the computed
   value in proof. All under existing `AutoFlags.*` privileges.

## 6. Frontend changes

- `auto-flags-api.ts`: `getAutoFlagMetrics` query + `MetricDef` type; `Right_Factor` on the
  rule DTO; keep `AUTO_FLAG_OPERATORS` (operators filtered by the metric's dataType).
- `AutoFlagEditorPage.tsx`: enable the **Aggregate** tab (remove `disabled`/`SOON` for it, drop
  from `NOT_EVALUABLE_*`); metric param form (function → **field picker filtered by type: number/
  amount only for Sum/Avg/Min/Max; any field or "rows" for Count/Count Distinct** → numeric
  window input → advanced grouping (default Customer, hidden) → status filter); right side =
  KYC/field **× factor** with a **× / % unit toggle**; operator list keyed off metric dataType;
  plain-English preview; Test panel shows computed value. Field-type comes from the existing
  operand catalogue (`dataType`/`fieldType` already returned by `GET /Operands`).
- `index.css`: styles for the metric param form.
- Log the entry in `FE_CHANGES_REQUIRED.md`; update `PROJECT.md`.

## 7. Prerequisites & assumptions

- **Customer key resolved** ✓ — `Customer_Code` via `TmX_Customer_Master`
  (`Transaction_Id → Customer_Code`); a customer's transactions are all snapshot rows sharing
  that code. No open question here.
- **Amount-field map** seeded for the Phase-1 products (Sum/Avg flags). Count/recency flags
  don't need it.
- **Currency**: value aggregates use the transaction's booking/base currency; mixed-currency
  customers are a documented limitation for Phase 1 (mark non-computable rather than sum
  naively). *Confirm whether any Phase-1 value flag must handle FX now.*
- Aggregates are single-hit (no per-match); the FE disables per-match for aggregate rules.

## 8. Backward compatibility & non-goals

- Purely additive: existing flags, the synchronous save path, and P1 reference/field rules are
  untouched. `Right_Factor` null and empty `Aggregate_Spec` behave exactly as today.
- No change to `TmX_Flag_Scope`, the scope tree, or the FCCM submission path.

## 9. Task sequencing

1. Migration: `Right_Factor` on `TmX_Flag_Rule`; create `TmX_AutoFlag_Job` +
   `TmX_Product_Aggregate_Field_Map`; seed the amount map for Phase-1 products.
2. Registry + `MetricContext` + `IRuleDataProvider`; `AggregateMetricHandler` (intra first,
   then cross) + `RecencyMetricHandler`.
3. Evaluator wiring (Aggregate left source, `Right_Factor`, proof).
4. Per-flag sync/async classification + cached product gate + enqueue at save.
5. Background worker hosted service (claim/lease, idempotent stage, retry, dead-letter).
6. `GET /Metrics` + create/update validation + Test proof.
7. FE: metric discovery, Aggregate tab, param form, factor, operator wiring, preview, Test.
8. Seed & prove flags **#13**, **#43**, one recency dormancy flag; live round-trip.
9. Build (BE via terminal, kill apphost/free 5081), FE tsc/lint, `FE_CHANGES_REQUIRED` +
   `PROJECT.md`, doc update.

## 10. Validation

- Unit: each function (count/distinct/sum/avg/min/max), value-date window boundaries,
  `includeSelf`, `Right_Factor`, non-computable inputs (never throws).
- Integration: enqueue on save only when an async flag applies; worker idempotency (re-run =
  same result); coalescing; retry/dead-letter.
- Live: author #43 in the UI, save a qualifying transaction, confirm the flag appears after the
  worker runs with correct proof; author #13 and confirm synchronous vs async behaviour;
  recency flag fires on a gap.

## 11. Open decisions for sign-off

1. **Customer key** ✓ answered — `Customer_Code` via `TmX_Customer_Master`.
2. **Count field type** — confirm the §2.10 rule: numeric/amount only for Sum/Avg/Min/Max, but
   **Count / Count Distinct allowed on non-numeric fields / rows** (required for #13, count of
   intermediaries). If you want *everything* restricted to numeric, #13 would need a different
   modelling.
3. **#68/#69** — confirmed out of scope (FCCM owns)? Or add an *opportunistic* next-transaction
   check as a safety net?
4. **FX** — is any Phase-1 value flag required to handle mixed currency now, or is
   booking-currency + documented limitation acceptable for the first cut?
5. **Worker SLA** — acceptable lag from save to aggregate-flag appearance (e.g. a few seconds)?

# Auto-Flag Rule Engine — Aggregation layer analysis

Scope: **aggregation-based auto flags only.** Derived fields are intentionally out of scope
(handled separately later). This document identifies the candidate aggregation flags and,
for each, the data, aggregation type, grouping key, efficient implementation, and edge cases.

## 0. The central constraint (read first)

The values most aggregations need — **amount, counterparty/beneficiary, invoice number,
goods value, dates** — are **not columns** on `TmX_Transaction`. They live inside
`TmX_Transaction_Detail.UDF_Data` (per-product JSON, different field codes per product) and
the repeating grids inside it. The header table only gives us hard columns to group by:
`Product_Id`, `Company_Branch_Id`, `Currency_Id`, `User_Id`/`Created_By`, `Transaction_Date`,
`Transaction_Status_Lkp`, `Sub_Product_Tab_Id`. Customer identity lives in
`TmX_Transaction_Customer_Snapshot` / `TmX_Customer_Master`, not on the transaction row.

This gives **two fundamentally different aggregation classes**:

- **Class A — Intra-transaction**: aggregate over the *repeating grid rows of the current
  transaction's UDF* (HS-code lines, invoice lines, country/beneficiary lists). Cheap, no new
  storage — the engine already flattens the UDF (`FlattenUdf`) into `field → [values]`.
- **Class B — Cross-transaction / historical**: aggregate over a *set of related
  transactions* grouped by customer/branch/counterparty over a time window. Expensive if done
  by parsing every candidate transaction's JSON at evaluation time → needs a **materialised
  fact/extract layer** (see §3) to be efficient.

---

## 1. Class A — Intra-transaction aggregations (the first cut)

These run on the already-loaded UDF; grouping key is always "this transaction".

| # | Flag (example) | Data to aggregate | Aggregation type | Grouping / key | Efficient implementation | Dependencies / edge cases |
|---|---|---|---|---|---|---|
| A1 | **N+ sanctioned countries on the document** | all country fields + grid origin/port country rows | **Distinct Count**, filtered by membership in `SANCTIONED_COUNTRIES` | per transaction | reuse `FlattenUdf` + `AllListMatches`; count distinct matched ISO codes | dedupe same country across many fields (distinct by ISO, not by field); blank rows ignored |
| A2 | **N+ screened HS-code lines** | HS-code grid column values | **Count** (or Distinct Count) filtered by membership in `SCREENING_GOODS` | per transaction (per HS grid) | from UDF grid array; membership via existing goods list | duplicate HS lines (count vs distinct must be a spec option); 7k-list lookup already optimised |
| A3 | **Goods-line amount vs LC amount % variance** | Σ(invoice/goods line amounts) vs header LC amount | **Sum** (then % variance compare) | per transaction | sum grid amount column from UDF; LC amount also from UDF | **currency**: line ccy vs LC ccy mismatch → can't sum naively; missing/text amounts; the *compare* is derived, the *Sum* is the aggregate |
| A4 | **Unusually many line items** | invoice/goods/beneficiary grid rows | **Count(rows)** > N | per transaction (per grid) | array length from UDF | empty trailing rows; which grid (spec must name it) |
| A5 | **Duplicate value within the document** | a grid column (invoice no., HS code, beneficiary) | **Duplicate Count** = max group size where `count(value) > 1` | per transaction, grouped by the column value | group grid rows by value in memory | blanks excluded; case/whitespace normalisation |
| A6 | **Many distinct currencies / countries / beneficiaries** | a grid column | **Distinct Count** > N | per transaction | distinct over array | normalise codes (id→hidden) before distinct, same as P1 |
| A7 | **Max single line amount over threshold** | invoice/goods amount column | **Max** > threshold | per transaction | max over numeric column | non-numeric/null cells; currency (per A3) |
| A8 | **Total quantity / weight over threshold** | quantity/weight grid column | **Sum** > threshold | per transaction | sum over column | units consistency; nulls |

**Class A summary:** all computable from the in-memory UDF with no new tables. Main shared
dependency is **currency/units normalisation** for Sum/Max/variance (A3, A7, A8); count/
distinct ones (A1, A2, A4, A5, A6) have none beyond value normalisation we already do.

---

## 2. Class B — Cross-transaction / historical aggregations

Grouping key + time window over many prior transactions. These are the classic AML/TBML
velocity & structuring patterns.

| # | Flag (example) | Data to aggregate | Aggregation type | Grouping / key | Efficient implementation | Dependencies / edge cases |
|---|---|---|---|---|---|---|
| B1 | **Customer velocity** — too many txns in a window | transaction rows | **Count** > N | **Customer × rolling window (e.g. 30d)** | fact table indexed `(CustomerId, ValueDate)`; range scan + count | needs a stable CustomerId; window relative to txn date; exclude drafts/cancelled by status |
| B2 | **Aggregate exposure** — total value in a window | transaction amount | **Sum** > threshold | Customer × window | fact table with `AmountBaseCcy` precomputed | **FX normalisation to a base ccy**; status filter; partial periods |
| B3 | **Structuring / smurfing** — many sub-threshold txns summing high | amount + count | **Count where amount < T** AND **Sum ≥ T2** | Customer × window | two aggregates over the same windowed set | threshold/ccy; defining "just below"; window choice |
| B4 | **Duplicate invoice across transactions** | invoice number (from UDF) | **Count(distinct txns sharing invoice no.)** > 1 | InvoiceNo × (customer or global) × window | requires invoice no. **extracted to the fact layer** (it's in UDF today) | invoice-no normalisation; scope (customer vs global); back-dated dups |
| B5 | **Beneficiary/counterparty concentration** | amount or count to one counterparty | **Sum** or **Count** + **% of total** | Customer × Counterparty × window | fact layer keyed by counterparty | counterparty identity normalisation (free-text names); needs total for the % |
| B6 | **First-time / new counterparty** | historical txns to this counterparty | **Count == 0** historically | Customer × Counterparty | fact layer existence check | name matching/normalisation; "first time" within all-time vs window |
| B7 | **Same-amount repetition** | amount | **Count(value)** > N | Customer × Amount × window | fact layer group-by amount | exact vs rounded amounts; ccy |
| B8 | **Repeated sanctioned-country exposure** | count of prior txns touching sanctioned country | **Count** > N | Customer × window | fact layer needs a precomputed "touched sanctioned country" boolean per txn | depends on A1 being computed & stored per txn |
| B9 | **Average size deviation** (avg is the aggregate; the compare is derived) | historical amounts | **Avg** (+ optionally StdDev) | Customer × window | fact layer rolling avg | cold-start (few prior txns); ccy; the comparison itself is a derived step |

**Class B summary:** every one of these needs (a) a **stable grouping key** (CustomerId,
CounterpartyKey) and (b) the **aggregated value available as a queryable column** — neither of
which exists today because the data is in JSON. So Class B is gated on the fact layer in §3.

---

## 3. Recommended efficient implementation foundation

### Class A
No new storage. Add aggregation handlers that run over `FlattenUdf` output (already produced
once per evaluation). O(rows) in memory. Reuse existing list-membership + id→code
normalisation. This is the cheap first deliverable.

### Class B — the materialised fact/extract layer
Parsing UDF JSON across hundreds/thousands of historical transactions at flag-evaluation
time is too slow and fragile. Instead, maintain a **normalised, indexed projection** written
at transaction save (the same point auto-flags already run):

`TmX_Transaction_Fact` (one row per transaction), columns extracted/normalised at save:
`Transaction_Id`, `Customer_Id`, `Company_Branch_Id`, `Product_Id`, `Value_Date`,
`Status_Lkp`, `Amount`, `Currency_Id`, `Amount_Base_Ccy`, `Counterparty_Key`,
`Invoice_No_Norm`, `Touches_Sanctioned_Country` (bit), `Distinct_Country_Count`, …

- Populated by an extractor in the transaction-save pipeline (where the UDF is already in
  hand). Idempotent upsert per `Transaction_Id`.
- Indexes: `(Customer_Id, Value_Date)`, `(Counterparty_Key, Value_Date)`,
  `(Invoice_No_Norm)`. These make all B-flags simple, fast `WHERE key=… AND Value_Date >=
  @from` range aggregates.
- Aggregation handlers issue **one scoped SQL aggregate** (`COUNT`/`SUM`/`AVG`/`COUNT
  DISTINCT`) against the fact table — no JSON parsing at evaluation time.
- Backfill once from existing transactions; thereafter incremental at save.

This keeps the engine's per-evaluation cost to a single indexed query per B-rule, and isolates
the "JSON → columns" complexity to one extractor instead of every rule.

---

## 4. Cross-cutting dependencies & edge cases (apply to many flags)

1. **Customer key.** Header has no `Customer_Id`; identity is in the customer snapshot /
   master. A stable `Customer_Id` (and a normalised `Counterparty_Key`) must be resolved into
   the fact layer — the single biggest prerequisite for all of Class B.
2. **Currency / FX.** Any Sum/Avg/Max across mixed currencies (A3, A7, B2, B3, B7, B9) needs a
   base-currency conversion → dependency on an FX-rate source and an `Amount_Base_Ccy` column.
3. **Status filtering.** Aggregates must count only meaningful transactions (submitted/active),
   excluding drafts, cancelled, and discarded — drive off `Transaction_Status_Lkp`.
4. **Time window semantics.** Rolling window relative to the *current* transaction's value
   date; define inclusive/exclusive bounds and whether the current txn is included.
5. **Point-in-time vs now.** A saved evaluation should aggregate "as of" the transaction's
   time; the Test/dry-run aggregates "as of now". Decide and document (affects reproducibility).
6. **Counterparty/invoice normalisation.** Free-text names and invoice numbers need
   canonicalisation (trim/upper/strip punctuation) before grouping, or B4/B5/B6 misfire.
7. **Cold start.** Avg/deviation and "first-time" flags behave oddly with few prior records —
   need a minimum-sample guard.
8. **Self-exclusion.** A transaction shouldn't count itself in "duplicate"/"velocity" unless
   intended; define inclusion explicitly.
9. **Per-match semantics.** Aggregates yield a single scalar → one flag per (txn, flag);
   `Raise_Per_Match` is not applicable to aggregate rules.

---

## 5. Suggested sequencing for the aggregation layer

1. **A-flags first** (intra-transaction): A1, A2 (count in sanctioned/goods lists), A4/A5/A6
   (count/duplicate/distinct), A7 (max). No schema change, immediate value, reuses P1 plumbing.
   Defer A3/A8 until currency normalisation is decided.
2. **Fact layer** (`TmX_Transaction_Fact` + extractor + backfill + indexes) — the enabler.
3. **B-flags** on top: start with B1 (velocity, count) and B4 (duplicate invoice) since they
   need the fewest normalisations (count, invoice-no), then B2/B3 (need FX), then B5/B6/B9
   (need counterparty key + stats).

> Open inputs needed to finalise: (a) the canonical **Customer_Id** source, (b) base currency
> + FX-rate source for value aggregates, (c) which specific B-flags are in the first release,
> (d) point-in-time vs now for saved evaluations.

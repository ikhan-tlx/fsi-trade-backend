# Transaction Auto Flags — sub-product scope analysis & FE proposal

Source: `List of RedFlags.xlsx` (sheets **Add**, **No Change**). Analysis is limited to
**Auto** flags that evaluate against a **transaction** (KYC-profile-only and pure
customer-level flags are excluded, e.g. Add#38 "customer registered in weak-AML
jurisdiction", #58 ESI, #59 performance).

---

## 1. Transaction Auto Flags (inventory)

Grouped by the rule archetype so it ties into the engine roadmap (P1 reference-list is live;
Aggregate/Derived are the planned phases).

### 1a. Reference-list / field-compare (P1 — already supported today)
| # | Flag | Note |
|---|---|---|
| Add#41 | Shipment origin / transit / destination in High-Risk list | country fields → list |
| Add#44 | Geographical risk (high-risk countries) | country fields → list |
| Add#70 | High-risk / dual-use goods (HS code) | HS grid → SCREENING_GOODS |
| Add#65 | LC applicant country ≠ issuing-bank country | field-vs-field |
| Add#63 | Buyer country = seller country but shipment elsewhere | field compares |
| NoChange#25 | Counterparty in high-risk countries | list |
| NoChange#26 | Transaction involving tax-haven countries | list (needs TAX_HAVEN list) |
| NoChange#27 | Land-locked / near-sanctioned countries | list (needs LAND_LOCKED list) |

### 1b. Aggregate (planned — see AUTO_FLAG_AGGREGATIONS_ANALYSIS.md)
| # | Flag | Aggregation |
|---|---|---|
| Add#13 | > 2 third-party intermediaries | Count (intra-txn) |
| Add#64 | LC overdrawn > 120% of LC value | Sum utilisation vs LC (intra/cross) |
| Add#15 | Product-change freq / LC amendments / tenor extensions | cross-txn Counts |
| Add#16 | Document-resubmission / urgent-request counts | cross-txn Counts |
| Add#40 | Geography deviation ≥ 3 in 90 days | cross-txn Count (window) |
| Add#18/#39/#43/#56 | Dormancy → sudden surge (volume/value) | cross-txn Sum/Count (window) |
| Add#68/#69 | Advance payment with no shipment in N days | cross-txn temporal |
| Add#71 | Consecutive discounts, same exporter/importer (cycle 3) | cross-txn pattern |
| Add#21/#42/#53 | Price variance vs benchmark / historical avg | Avg (cross-txn) + compare |

### 1c. Derived / KYC-declaration comparison (planned — derived phase)
| # | Flag |
|---|---|
| Add#12,#46,#47,#48,#50,#51,#54,#55,#66 | Transaction attribute (HS/counterparty/product/channel/mode/payment/third-party) ∉ declared Trade-KYC list |
| Add#45 | Import/export/transit geographies ≠ declared KYC geographies |
| Add#52 | Single transaction value > declared expected turnover |
| Add#60 | Tenor not commensurate with perishable goods |
| Add#62 | Future / 6-month pre-dated Bill of Lading |
| Add#14 | Goods vs supplying-country profile score |
| Add#22,#49 | Goods description materially deviates (text similarity) |

> The **scope model below is archetype-agnostic** — it applies to every row above regardless
> of whether the rule is reference-list, aggregate, or derived.

---

## 2. Validation — only the scoped product/sub-product is evaluated

Confirmed in `AutoFlagEvaluator.EvaluateAndStageAsync`. The in-scope flag set is selected as:

```csharp
_db.FlagScopes.Where(s => s.ActiveFlag
    && (s.ProductId == null
        || (s.ProductId == tx.ProductId
            && (s.TabId == null || s.TabId == tx.SubProductTabId))))
```

So a flag evaluates against a transaction **only if** it has an active scope row that is
either (a) global (`Product_ID = NULL`), (b) product-level for the txn's product
(`Product_ID = tx.ProductId`, `Tab_ID = NULL` → all sub-products), or (c) sub-product-exact
(`Product_ID = tx.ProductId AND Tab_ID = tx.SubProductTabId`). ✔️ Correct and already
sub-product-aware at the **evaluation** layer. (The dry-run **Test** endpoint intentionally
bypasses scope so any flag can be tried against any transaction.)

**The gap:** the DB schema (`TmX_Flag_Scope.Tab_ID`) and the evaluator already support
sub-product scope, but the **write/read path never uses it**:
- `AutoFlagWriteModel` carries only `scopeProductIds: int[]`.
- `CreateAutoFlagCommandHandler.WriteScopes` inserts `FlagScope { ProductId = pid }` with
  `Tab_ID` left **NULL** → every flag is product-level (all sub-products).
- `GetAutoFlagByIdQueryHandler` returns only `scopeProductIds` (rows where `ProductId != null`).

So today you can only scope a flag to whole products (or global) — never to a chosen set of
sub-products, even though a single flag legitimately applies to several specific sub-products.

---

## 3. Which flags need multiple sub-products

The sheet's "Product Type" column maps each flag to one or more **products** (LC, Collection,
Open Account, Merchanting, Advance, SBLC/BG, Bank Contract, FX Remittance…). Each of those
products expands to **several sub-product tabs** in this system (e.g. LC → ILF, ILA, ILL, ID;
Collection → ICR, ICA, ICD, ICL, IOA; etc.). Therefore:

- **Multi-sub-product (the majority)** — any flag whose Product Type lists more than one
  product, e.g. Add#13, #14, #15, #16, #18, #21, #40, #42 (LC/Collection/Open Account/
  Merchanting/Bank Contract combinations), and every **"All Products"** flag (Add#12, #41,
  #44). These must be scopable to a chosen set of sub-products spanning one or more products.
- **Single-product, still multi-sub-product** — LC-only flags (Add#64 overdrawn, #65
  applicant≠issuing bank) still cover LC's several sub-products (issuance/amendment/…).
- **Genuinely narrow** — Add#68 (export advance) and #69 (import advance) target a single
  side; still typically 1–2 sub-product tabs.

Net: **nearly all transaction auto flags are multi-sub-product**; single-tab flags are the
exception. So multi-select at sub-product granularity is the required scoping model, with
"whole product = all its sub-products" as a convenience.

---

## 4. Proposed frontend changes (compatible with existing BE/DB)

**Goal:** let an author scope a flag to any set of **sub-products** (tabs), across one or
more products, mapping cleanly to `TmX_Flag_Scope (Flag_ID, Product_ID, Tab_ID)` rows — with
"all sub-products of a product" = a single row with `Tab_ID = NULL` (which the evaluator
already treats as product-wide). No DB change; a small additive contract change.

### 4.1 Scope editor UX (replaces the current product-only multi-select)
- A **grouped multi-select tree**: Product → its Sub-products (tabs). Each product node has a
  tri-state checkbox:
  - **All sub-products** (product node checked) → emit one scope `{ productId, tabId: null }`.
  - **Some sub-products** (specific tab leaves checked) → emit one scope per checked tab
    `{ productId, tabId }`.
  - **Global (all products)** → a top-level "Applies to all products" toggle → single
    `{ productId: null, tabId: null }` (unchanged behaviour).
- Reuse the existing themed `SelectDropdown`/multi-select popover; sub-product lists load per
  product from the existing **`GET /api/v1/Product/{id}/Tabs`** (no new endpoint needed).
- Selected scope summarised as chips ("Import LC · Import Amendment · Shipping Guarantee",
  "All Export sub-products", "All products").

### 4.2 API contract (additive, backward compatible)
Change the scope payload/response from a flat product-id list to an explicit scope-entry list:

```ts
// create/update body + detail response
scopes?: Array<{ productId: number | null; tabId: number | null }>
```
- Keep accepting the legacy `scopeProductIds: number[]` (map each to `{productId, tabId:null}`)
  so nothing breaks during rollout.
- FE sends `scopes`; a product checked "all" → `{productId, tabId:null}`; each chosen
  sub-product → `{productId, tabId}`; global → `{productId:null, tabId:null}`.

### 4.3 Backend (minimal, additive — DB unchanged)
- `AutoFlagWriteModel`: add `scopes: List<ScopeEntryDto>` (keep `scopeProductIds`).
- `WriteScopes` (create + update): if `scopes` present, insert one `FlagScope` per entry with
  its `Product_ID` **and `Tab_ID`**; else fall back to the current product-only behaviour.
- `GetAutoFlagByIdQueryHandler`: return `scopes` (productId + tabId) in addition to
  `scopeProductIds` for back-compat.
- The unique indexes on `TmX_Flag_Scope` already permit one row per `(Flag_ID, Product_ID,
  Tab_ID)` and a separate product-level (`Tab_ID NULL`) row, so this maps 1:1 with no schema
  change. The evaluator already honours `Tab_ID` (§2) — no evaluator change.

### 4.4 Display touch-ups
- Auto Flags **list** grid "Scope" column: show sub-product chips / "N sub-products" instead
  of only product names.
- Plain-language preview header: "Applies to: Import LC, Import Amendment (+2)".

### 4.5 Migration/compatibility notes
- Existing seeded flags (product-level, `Tab_ID NULL`) keep working — they read back as
  "all sub-products" of each product; no data migration required.
- Round-trip safe: editing a legacy product-level flag and saving without touching scope
  re-writes the same `{productId, tabId:null}` rows.

**Effort shape:** FE = new scope tree component + api types + list/preview chips; BE = one DTO
field + `WriteScopes`/detail read tweak. DB = none. Evaluator = none.

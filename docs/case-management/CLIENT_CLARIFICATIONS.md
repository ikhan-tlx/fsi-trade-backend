# CMS Implementation Clarifications (client feedback round 2)

**Date**: 2026-11-04 · answers the seven numbered points raised on the feedback-disposition review. Source of truth: `ECM_MASTER_PLAN.md` (§8.5 field mapping, §12 correlation, §11 workflow).

---

## 1. How Correlation works — and how dynamic fields are handled

### The confusion, stated plainly

Every product/sub-product stores the "same" business fact under a **different dynamic field code**: port of loading is `ILF_PortLoadingCtry` on an Import LC but `ICR_PortLoadingCtry` on a Contract Registration; a drawee name has a different field code per sub-product. If correlation tried to read raw UDF fields, every rule would need to know every product's field names. It does not.

### The design: correlate on canonical columns, never on raw UDF

Correlation runs **after** a one-time translation step. Two layers:

```text
LAYER 1 — MATERIALISATION (runs once, at case creation)
  Transaction finalises with flags
     |
     v
  EntityMaterializer
     reads CMS.Field_Mapping rows for THIS transaction's (Product, Sub-product tab)
     e.g.  (Import, ILF):  ILF_PortLoadingCtry  -> Trade_Detail.Port_Of_Loading
           (Import, ICR):  ICR_PortLoadingCtry  -> Trade_Detail.Port_Of_Loading   <- SAME target
           (Export, EC):   EC_DraweeName        -> Trade_Party(role=Drawee).Name
     resolves each UDF value, applies the transform (lookup code, trim, cast)
     |
     v
  Typed CMS rows:  CMS.Trade_Detail · CMS.Trade_Party · CMS.Trade_Goods (HS lines)

LAYER 2 — CORRELATION (runs on the typed rows only)
  CMS.Correlation_Rule:  "same Counterparty within 90 days -> link as duplicate"
     matches Trade_Party.Name / Customer_Code / Port / HS family / Branch ...
     — ONE canonical attribute per concept, whatever product the case came from.
```

So a rule says **"Counterparty"**, never "ILF_DraweeName or EC_DraweeName or ...". Products and sub-products differ only in the mapping rows; the correlation vocabulary is fixed and product-independent.

### Where "customer code" and "branch" come from (your exact example)

Not every attribute needs a UDF mapping. The mapping table supports several **source kinds**:

| Attribute | Source kind | Where it actually lives | Needs per-product mapping? |
|---|---|---|---|
| Customer code / name | `snapshot.column` | `TmX_Customer_Master` columns (same for every product) | **No** — one row, all products |
| Branch | `transaction.column` | `TmX_Transaction.Company_Branch_Id` | **No** — one row, all products |
| Transaction number, product, date | `transaction.column` | transaction columns | **No** |
| Port of loading, drawee, vessel, bank, amounts | `udf.detail` | per-sub-product UDF field codes | **Yes** — one row per (product, tab) |
| HS code lines | `udf.grid` (strategy `AllRows`) | repeating grid per sub-product | **Yes** |
| KYC parameters (country of residence, industry…) | `udf.customer` / `udf.detail` | `KycParam*` fields | **Yes** — per customer type |

**Rule of thumb**: identity fields (customer, branch, transaction) are already typed TMX columns — zero mapping effort. Only **product-specific trade facts** need mapping rows, and those are seeded once from the existing dynamic-form catalogue, then maintained on the Mappings admin screen.

### What happens when a mapping is missing

If a product has no mapping for, say, `Vessel`, cases from that product simply have an empty `Vessel` column — a correlation rule on vessel cannot match them (it never errors). The Mappings screen shows coverage per product so the gap is visible, and the rule editor warns when a rule uses an attribute that some active products don't map.

---

## 2. (no item raised)

---

## 3. Customer 360 — yes, a straight customer table in the CMS schema

Your reading is correct, and it is exactly what is planned:

- **`CMS.Customer`** — a plain, flat table, one row per customer, holding the **minimum details**: `Customer_Code (unique)`, `Customer_Name`, `Customer_Type`, `Status`, `Current_Risk_Band`, review dates, and a pointer `Current_Snapshot_Id` to the active snapshot. Onboarding **creates** the row; reassessment never duplicates it.
- **`CMS.Case_Customer_Snapshot`** — the **full profile**, versioned: country of residence/incorporation, industry, legal structure, identifiers, and further fields via mapping. One snapshot per assessment; **exactly one is active** (the one the registry points at) and that is what the 360 screen displays. Older snapshots stay frozen so a closed case always shows the facts as they were at decision time. (Detailed in §9.3.)

**How the columns get filled**: the same `Field_Mapping` mechanism as point 1 — e.g. `KycParam34 → Country_Of_Residence` with transform `lookupHidden('Country')`, scoped per customer type (Individual / Legal Entity / FI can use different KYC fields). TMX keeps capturing data in its dynamic forms; the CMS fills its flat table from them at controlled moments.

**How it is visible**:
1. The **Customer 360 / Scorecards screen** reads `CMS.Customer` directly — profile header, current band, review dates, scorecard history, case history (via `Customer_Code` links).
2. The **Mappings admin screen** shows every column's source field — and on the 360 screen each value can show a drill-back tooltip: *"from KycParam34 (Section A — Country of Residence)"*, so it is always clear where a value came from.
3. Rules, correlation and reports all query this flat table — no JSON parsing at run time.

---

## 4. RFI — agreed: out of this release

Decision recorded: the **structured RFI** (own entity, recipient, due date, its own SLA, reminders, status, history) is a **separate feature, not part of this release**. The current release keeps the simple `Request info` workflow command with mandatory remarks (already in the prototype's Take Action menu). Structured RFI moves to the phase-2 backlog with no impact on the core build.

---

## 5. CaseWF — the exact default workflows to build

Agreed that the designer makes schemes dynamic — but we seed **two concrete schemes**. These are the exact flows to incorporate first (editable in the designer afterwards):

### 5.1 `CaseWF_KYC` (lighter: one analyst tier + authorizer)

```text
 [Created]                                            actors: rule CheckRole
    | auto (on case creation)                          - KYC Case Analyst
    v                                                  - KYC Case Authorizer
 (L1 Pool)  action: FillAllUsersBucket("KYC Case Analyst")
    | AssignToMe            (claim — removes from others' pool)
    v
 (L1 Assigned)
    | RecommendClosure(disposition: Approved|Rejected|Returned; remarks required)
    |----- ReturnToPool ----> back to (L1 Pool)
    v
 (Authorizer Pool)  action: FillAllUsersBucket("KYC Case Authorizer")
    | AssignToMe
    v
 (Authorizer Assigned)
    | ApproveClosure  --> [Closed - disposition]   (final; fires decision applier:
    |                                               txn status + review schedule)
    | RejectRecommendation(remarks) --> back to (L1 Pool)
```
Guards: approver ≠ recommender ≠ anyone who worked the case. Status sync per command via the existing `Command_Application_Status_Mapping` idiom.

### 5.2 `CaseWF_TBML` (three analyst tiers + authorizer; SLA auto-escalation)

```text
 [Created] --auto--> (L1 Pool) --AssignToMe--> (L1 Assigned)
                         |  auto-escalate: condition AppDelay > SLA threshold
                         v
     EscalateToL2 --> (L2 Pool) --AssignToMe--> (L2 Assigned) --SendBackToL1--> (L1 Pool)
                         |  auto-escalate (AppDelay)
                         v
     EscalateToL3 --> (L3 Pool) --AssignToMe--> (L3 Assigned) --SendBackToL2--> (L2 Pool)

 From ANY Assigned level:
     RecommendClosure(disposition: Confirmed-STR | False Positive | No Action | Returned)
        v
 (Authorizer Pool) --AssignToMe--> (Authorizer Assigned)
     | ApproveClosure --> [Closed - disposition]
     | RejectRecommendation --> back to the recommending level's Pool
```
Optional (config, off by default): parallel approval on `Confirmed-STR` dispositions — `FillApprovers` + each `Approve` + `IsApproveComplete` condition before closure (the same mechanism the legacy schemes already use).

Both schemes reuse only **existing engine building blocks**: `CheckRole` actor rule, `FillAllUsersBucket`, `AssignToMe` claim-narrowing, `AppDelay` conditions, status-sync mapping. Nothing new is required from the engine.

---

## 6. Role segregation — agreed, closed

Role Management already covers it: separate roles (Analyst tiers, Authorizer, Configuration Administrator via `RiskConfig.Manage`, System Administrator), CMS privileges surfaced only in TMX CMS mode, and the no-self-approval guard. No action.

---

## 7. Configurable case workflows — exactly which workflows, on which engine

**One engine, no second workflow system.** Case workflows run on the platform's existing workflow engine — the same one running `ImportWF`, `ExportWF`, `KYC_Workflow` today. Transaction schemes are untouched.

What the engine is expected to accommodate:

| Scheme | Status | Purpose |
|---|---|---|
| `ImportWF`, `ExportWF`, `KYC_Workflow`, + 5 existing | Unchanged | Transaction maker-checker (today's behaviour) |
| **`CaseWF_KYC`** | **New — this release** | KYC case investigation (§5.1) |
| **`CaseWF_TBML`** | **New — this release** | Trade case investigation (§5.2) |
| Per-type variants (PEP, dual-use goods, sanctions, high-risk jurisdiction, price manipulation…) | Config, later as needed | Copy a seed scheme in the designer, adjust levels/SLA/approvals, map it via **Case type → Scheme** configuration. No code. |

How a variant comes to exist, end to end: (1) the case-generation policy classifies the case type (e.g. dual-use flag codes → case type "Dual-use goods"); (2) the **Case type → Scheme** config says which scheme that type runs; (3) SLA configuration provides that type's deadlines. So "different workflows/SLA/approvals per case type" = three existing configuration tables, zero engine change.

Capacity note: the engine already runs 8 schemes in production; adding case schemes is adding rows, not load of a new kind. The schemes are authored in the same XML dialect, so they remain valid when the engine is replaced by the in-house one per the workflow replacement plan.

---

## 8. (no item raised)

---

## 9. Addendum (follow-up questions, 2026-11-04)

### 9.1 What "KycParam" fields are

They are the **technical field codes of the KYC dynamic form**. When a maker fills KYC Sections A–F, each answer is stored in the transaction's UDF JSON under a generated code — `KycParam1`, `KycParam4`, `KycParam34`, and so on — not under a business name. The business label ("Country of Residence", "Legal structure") lives in the field-setup table (`TmX_Tenant_Field_Setup`) and is shown on screen; the JSON only carries the code.

Examples from the live data: `KycParam34` = citizenship status, `KycParam28` = organization legal structure, `KycParam4` = annual income. Dropdown answers are stored as lookup IDs, resolved through `TmX_Lookup`; repeating grids (e.g. countries of operation, `TmxKYCSPOR`) hold one `KycParamN` per grid column. This is why the field-mapping layer exists: `KycParam34 → CMS.Customer.Country_Of_Residence` turns an opaque code into a named, queryable column — exactly the same pattern as `ILF_PortLoadingCtry` on the trade side.

### 9.2 Which tables live where — precisely

| Table | Schema | New? | What it holds |
|---|---|---|---|
| `TmX_Transaction` | dbo (existing TMX) | No | The transaction itself — **stays where it is; no transaction table is created in CMS** |
| `TmX_Transaction_Detail` | dbo | No | The dynamic-field (UDF) values, incl. `KycParam*` and trade field codes |
| `TmX_Customer_Master` | dbo | No | TMX's existing **per-transaction** customer snapshot (despite its name, one row per transaction) |
| `CMS.Customer` | CMS | **Yes** | The flat customer registry — one row per customer, typed columns, updated on reassessment |
| `CMS.Case_Customer_Snapshot` | CMS | **Yes** | Frozen customer facts **as at** an assessment/case — written once, never updated |
| `CMS.Trade_Detail` / `Trade_Party` / `Trade_Goods` | CMS | **Yes** | Typed trade facts **extracted for a case only** — created when a case opens, not for every transaction |
| `CMS.Transaction_Map` | CMS | **Yes** | The join spine: which transaction produced which CMS case / trade rows / scorecard (by `Transaction_Id` value, no cross-schema foreign key) |

So: the **snapshot — yes**, it is a new CMS table (plus the customer registry). A **transaction table — no**: transactions remain solely in `dbo.TmX_Transaction`; the CMS references them by ID and copies only the handful of trade facts a case needs, at the moment the case is created. There is never a second store of all transactions to keep in sync.

### 9.3 One ACTIVE snapshot + scorecard reference (final model, per FSI 2026-11-04)

Final shape, as you stated it:

- **`CMS.Customer` holds minimum details only** — code, name, type, status, current band, review dates, and two pointers: `Current_Snapshot_Id` and `Current_Scorecard_Id`. No profile columns here.
- **The full profile lives on the snapshots**, and **exactly one snapshot is active per customer** — the one `Current_Snapshot_Id` points at. That active snapshot is what the 360/display screens show.
- **Every scorecard references its snapshot** (`Risk_Scorecard.Snapshot_Id`) — always the snapshot that was current at scoring time.

Lifecycle on each assessment: capture a **new** snapshot from the TMX dynamic fields → score it → write the scorecard (pointing at that snapshot) → move `Current_Snapshot_Id` to the new snapshot. The old snapshot is never edited or re-activated — it remains history that its scorecard and cases still point to.

```text
CMS.Customer (minimum details + Current_Snapshot_Id)
   |                                  \
   | points to                          older snapshots = immutable history
   v                                      ^
CMS.Case_Customer_Snapshot (ACTIVE) ------+   each referenced by its own scorecard
   |  scored from
   v
CMS.Risk_Scorecard (Snapshot_Id + model version + breakdown)
   |  policy fires?
   v
CMS.Case (Scorecard_Id)
```

Why one pointer instead of an "active" flag on the snapshot rows: a pointer makes two-snapshots-active impossible by construction, and switching it is one atomic update. The display result is the same — the screen shows the active snapshot's profile with the registry's minimums.

### 9.4 Hybrid matching storage — decision (FSI 2026-11-04)

Debated whether correlation should read dynamic fields directly (no mapping) or mapped static columns. Decision: **hybrid**.

- **Few static columns** for the stable core: customer, transaction, product, **branch, bank**, amount, currency, dates.
- **Everything extensible** (vessel, UBO/director, drawee, ports, future needs) lives in a keyed attribute store (`Attribute_Def` registry + `Matching_Attribute` rows, one shared index), filled by the same mapping mechanism.
- **A screen supports it end-to-end** (Mappings → Attributes tab): register the new field → map it per product/sub-product → it appears in the correlation rule builder at once. No schema change, no developer, no runtime DDL utility.
- Why not pure dynamic matching: the field-code equivalence knowledge must live somewhere central anyway (precedent: the sanctioned-country flag needed 94 enumerated field codes); JSON matching can't be indexed across products and normalizes at every comparison — missed links at volume. The hybrid keeps Option B's self-service exactly where it was wanted, without its query-time costs.\n- Caveat shown to admins: a newly added attribute matches prospectively; historical backfill is an optional phase-2 job.

### 9.5 Final alignment — flag-based active snapshot, no registry pointers (FSI 2026-11-04)

Superseding the pointer note above, per FSI review — the assumption is **correct** and is now the model:

- **`CMS.Customer` carries no snapshot or scorecard references.** The relation is plain **1 → N**: one customer, many snapshots. The scorecard already hangs off the snapshot, so the registry needs no scorecard link either. (It keeps `Current_Risk_Band` purely as a denormalised convenience for grids — derivable, optional.)
- **The current snapshot is marked by an `Is_Active` flag** on the snapshot row. Safeguard so two rows can never both be active: a filtered unique index — `UNIQUE (Customer_Id) WHERE Is_Active = 1` — and the switch (clear old, set new) runs in one transaction.
- **Snapshot provenance added**: each CMS snapshot records where it came from in TMX — `Source_Transaction_Id` and `Source_Customer_Master_Id` (the exact `dbo.TmX_Customer_Master` row it was built from). Value keys only, no cross-schema foreign key, per the module-isolation rule.

One nuance to be aware of: snapshot → scorecard is also **1 → N**, because *Refresh* and *Override* re-score the **same facts** — a new scorecard version against the same snapshot. So "the current scorecard" is derived, not stored: the newest scorecard of the active snapshot.

```text
dbo.TmX_Customer_Master (per-transaction, TMX side)
        | Source_Customer_Master_Id / Source_Transaction_Id (provenance)
        v
CMS.Case_Customer_Snapshot  (1 customer -> N; exactly one Is_Active = 1)
        ^ 1..N                      | 1..N (Refresh/Override re-score same facts)
        |                           v
CMS.Customer (minimums only)   CMS.Risk_Scorecard (Snapshot_Id, model version, breakdown)
                                    | policy fires?
                                    v
                               CMS.Case (Scorecard_Id)
```

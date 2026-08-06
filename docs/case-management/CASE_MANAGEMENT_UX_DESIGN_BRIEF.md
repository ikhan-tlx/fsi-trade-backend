# Case Management UX — Design Brief for Variations

**Audience**: the designer agent. Produce **design variations only** — standalone HTML mockups + rationale. Do **not** modify application source.
**Date**: 2026-07-14
**Backend/architecture source of truth**: `FSI.Trade.Compliance/docs/FCCM_ANALYSIS_AND_DUAL_MODE_ARCHITECTURE.md` (§7 Standalone module design, §13 audit gates). The Case module ships in **Standalone TMX mode**; in Oracle-FCCM mode the module is hidden. Everything below is the approved functional model — design freedom is in layout/interaction, not in the model.

---

## 1. What is being designed

A new **Cases module** for the ICBC Trade Compliance React app (`tmx-finance-frontend-revamp`), filling the existing `/cases` placeholder route:

1. **Case Queue page** (`/cases`) — the investigator's work list across pools and personal claims.
2. **Case Detail / Investigation Workspace** (`/cases/:caseNumber`) — everything needed to investigate and decide a case.
3. **Decision dialogs** — Recommend Closure (disposition + mandatory remarks), Approve/Reject Recommendation, Escalate.

Deliver **two variations of each screen (Option A: conservative/grid-first evolution of existing app patterns; Option B: bolder investigator-workspace concept)**, each as a self-contained standalone HTML file in the established `Design Html/` style:

- `Case Management - Queue - Option A (standalone).html`, `… Option B (standalone).html`
- `Case Management - Detail - Option A (standalone).html`, `… Option B (standalone).html`
- Decision dialogs may live inside the detail mockups (open state shown) or as separate files if clearer.

Each file: desktop-first 1440px, light + dark (`[data-mode="dark"]`), realistic seeded data (§6), all states (§7), inline annotation comments marking **data binding** (`<!-- bind: case.priority -->`) and **BE gaps** if you need anything not listed in §6.

Also deliver a short `Case Management - UX Options Rationale.md`: per-screen comparison of A vs B scored on simplicity / scalability / dev-effort / learnability (same rubric as `AUTO_FLAGS_CONDITION_BUILDER_DESIGN_VARIATIONS.md`), with a recommendation.

---

## 2. Design system constraints (hard)

- **Primary design seed**: the "CMS Unified Compliance Workspace" prototype at `D:\ICBC - Latest\New theme\index.html` (module-switcher chip, case queue with SLA bars + re-review dates + saved views, fullscreen KYC case detail with Comparison tab and Close-as-cleared / File-SAR-STR / Escalate actions). Variations evolve this language, not replace it.
- Reuse the app's existing tokens and idioms — **no new palette**. Reference the approved standalones in `Design Html/` for tone and craft: `Transaction View (standalone).html` (detail-page idiom: dark hero band, tab rail, cards, People rail), `Trade Repository - Option A (standalone).html` (operations grid + KPI strip idiom), `Manual Red Flags (standalone).html` (admin list idiom). The Cases module is an **operations** surface (like Inbox/Trade Repository), not an admin screen.
- Existing shared components will implement whatever you draw: `SelectDropdown`, `DatePicker`, data grid with Kendo-style filters, `CommentsDialog` (used for workflow command remarks), toast system, `useHasPrivilege` gating. Don't invent exotic controls that have no implementation path (React 19 + hand-rolled CSS + Tailwind utilities).
- Action buttons on the detail page are **workflow-command-driven**: labels/availability come from `GET /Workflow/Process/{id}/Commands` for the case's process instance — same as the transaction detail page today. Design button slots + overflow behaviour, not hardcoded button sets.
- Privilege gating: `Cases.View` (module), `Cases.Operate` (claim/investigate/recommend/escalate/link/notes), `Cases.Authorize` (approve/reject closure). Show what each persona sees.
- No localStorage-dependent behaviour in the design; state is server-driven.

---

## 3. Functional model (approved — do not alter)

### 3.1 Case types

| Type | Created when | Distinct content |
|---|---|---|
| `KYC_ONBOARDING` | KYC risk assessment completes | Risk assessment panel is the centrepiece (dual scores + terms, reconciliation vs BRAINS), customer snapshot, prior-case links |
| `TBML_SURV` | Trade transaction finalises with ≥1 red flag | Event list is the centrepiece (1 customer event + N flag events with matched values + proof), linked transaction |

### 3.2 Lifecycle & workflow (CaseWF — multi-level, dual control)

`Created → Assigned → Under Investigation → (Escalated) → Closed(disposition)`.

Pools and claims: **L1 → L2 → L3 analyst tiers**, each tier is a pool (`AssignToMe` claims and removes from everyone else's pool — same claim behaviour as the transaction Inbox), plus a **Case Authorizer** pool for recommended closures. Commands per state (indicative): `AssignToMe`, `ReturnToPool`, `EscalateToL2/L3`, `SendBackToL1/L2`, `RecommendClosure(disposition, remarks)`, `ApproveClosure`, `RejectRecommendation(remarks)`. SLA auto-escalation exists (stale cases move up tiers automatically) — **aging must be visible**.

Dispositions: KYC → `Approved | Rejected | Returned`; TBML → `Confirmed (STR-candidate) | False Positive | No Action | Returned`.

**Dual control + no-self-approval (must be legible in the UI)**: analysts recommend; only a Case Authorizer approves; the approver cannot be the recommender nor anyone who worked the case — design the disabled/blocked state with its reason ("You worked this case — another authorizer must approve").

### 3.3 Risk explainability (KYC cases; summary chip on TBML too)

The assessment record carries **both** scores: `Algorithm_Score` (weighted per-parameter arithmetic, expandable breakdown: parameter / value / score / weight / contribution) and `Rule_Based` result with **the terms that fired** (rule name, predicate, matched values, reason), plus which one **prevailed** (`Superseded_By_Rule`), manual override (score + by/at/reason) if any, band (Low/Medium/High vocabulary), BRAINS reconciliation outcome ("higher wins / PEP wins", prevailing-source label), and `Rcmnd_To_Onbrd`. **The examiner question the panel must answer at a glance: "why is this High, on what terms."**

### 3.4 Correlation (rule-driven)

Cases carry auto-created links (`related | duplicate | reopens | merged_into`), each recording **which correlation rule fired and the matched context values** (e.g. "Rule 1 — same customer · CIF-000234"). Manual link/unlink with reason exists (S2). Design: a related-cases surface that shows link type + why, and a subtle banner when a probable-duplicate link exists.

### 3.5 Audit & evidence

Every action lands in the case audit trail (workflow transitions + notes/priority/reassign/link actions). An **"Audit pack"** export exists (one artifact: case → events/proofs → assessment → decisions → history). Give it a home (button placement), don't design the export itself.

### 3.6 Mixed-era history

Customers can have FCCM-era cases (`Case_Source = FCCM`, read-only, numbered `CA…`) alongside TMX cases (`TMX-CA…`). FCCM-era cases render with a source badge and reduced detail (no events/assessment internals — only what TMX holds).

---

## 4. Personas & journeys (design against these)

1. **L1 Case Analyst** — high volume triage. Journey: open queue → filter "My pool · TBML · High" → scan aging/priority → claim → skim events + matched values → either `RecommendClosure(False Positive)` with remarks or `EscalateToL2`. Needs: fast scanning, keyboard-friendly claim, evident SLA pressure.
2. **L2/L3 Analyst** — depth over volume. Journey: claimed escalated case → read L1's remarks + audit trail → inspect flag proofs, related cases, customer's case history → decide up or out. Needs: context density, escalation trail clarity.
3. **Case Authorizer** — dual-control decision maker. Journey: authorizer pool → open recommended case → review recommendation (disposition + remarks + who) → check assessment/events summary → `ApproveClosure` or `RejectRecommendation`. Needs: recommendation front-and-centre, self-approval block state, confidence to sign.
4. **Admin / MLRO oversight** (view-only + audit pack) — queue KPIs, aging distribution, drill into any case, export audit pack.

---

## 5. Screen requirements

### 5.1 Case Queue (`/cases`)

- **Scope switcher**: My cases · My pool (per my tier) · All (privilege-gated) — plus case-type and status filters, search by case number / customer / transaction.
- **KPI strip** (Trade-Repository-Option-A idiom): Open cases, In my pool, Aging > SLA, Awaiting authorization, Closed this week — each clickable as a filter.
- **Row content** (grid or cards — your call per option): case number (+source badge for FCCM-era), type chip, customer (code + name), linked transaction number, priority (band colour), status, current tier/queue, assignee or "Pool", age + SLA indicator (approaching / breached with auto-escalation hint), flags/events count (TBML) or band (KYC), last action.
- **Row actions**: Claim (pool rows), Open. Claiming from the list must feel instant (optimistic) with toast.
- States: empty pool, empty filters, loading, >500 rows (server paging).

### 5.2 Case Detail / Investigation Workspace (`/cases/:caseNumber`)

Must contain (grouping/layout is the design exercise):

1. **Hero/header**: case number + source badge, type chip, priority, status + tier, age/SLA, customer, linked transaction (deep link to the existing Transaction View), assignee, workflow command buttons (command-driven slots; primary = claim when unclaimed, recommend when claimed, approve when authorizer).
2. **Events & evidence** (TBML centrepiece): the flag events with description, matched value, severity, auto/manual origin, link to flag proof; the customer event; counts.
3. **Risk panel** (KYC centrepiece; compact summary on TBML): dual-score explainability per §3.3 — this is the hardest design problem in the brief; make "which score prevailed and on what terms" scannable in ≤3 seconds, with progressive disclosure into the per-parameter arithmetic.
4. **Related cases**: linked cases with type + "why linked" (rule + matched context), mixed-era badges, link/unlink affordance (privileged).
5. **Recommendation card** (when awaiting authorization): disposition, remarks, recommender, when — the authorizer's primary read.
6. **Notes** (add note = existing CommentsDialog idiom) and **Audit trail** (transitions + actions timeline, same visual idiom as the Transaction View audit tab).
7. **Audit pack** export button (oversight-gated).

### 5.3 Decision dialogs

- `RecommendClosure`: disposition select (per case type vocabulary) + mandatory remarks + summary of what's being recommended; destructive-adjacent styling per app convention.
- `ApproveClosure`: shows the recommendation being approved + mandatory remarks; the **blocked self-approval state** with explanatory copy.
- `RejectRecommendation` and `EscalateToL2/L3`: remarks-required variants of the same dialog family (design once, parameterise).

---

## 6. Seed data for mockups (realistic, use these shapes)

CamelCase DTO-ish bindings; invent plausible values in the same shapes.

```jsonc
// queue row
{ "caseNumber": "TMX-CA000418", "caseSource": "TMX", "caseType": "TBML_SURV",
  "customerCode": "CIF-000234", "customerName": "Helix Pharma Ltd.",
  "transactionNumber": "TLX00200001017", "priority": "High", "status": "Under Investigation",
  "tier": "L2", "assigneeName": null, "ageHours": 51, "slaState": "approaching",  // ok|approaching|breached
  "eventCount": 6, "riskBand": null, "lastAction": "Escalated by A. Raza · 2h ago" }

// KYC risk panel
{ "algorithmScore": 61.25, "algorithmBand": "Medium",
  "ruleBased": { "band": "High", "ruleName": "PEP declared", "terms": "KycParam9 = 'Yes' (PEP)", "reason": "PEP customers accelerate to High" },
  "supersededByRule": true, "effectiveScore": 75.00, "effectiveBand": "High",
  "override": null,  // or { "score": 50.00, "by": "MLRO", "at": "...", "reason": "..." }
  "brains": { "band": "Medium" }, "reconciled": { "band": "High", "prevails": "TMX" },
  "rcmndToOnbrd": "Review",
  "parameters": [ { "label": "Country of Residence", "value": "AF", "score": 90, "weight": 0.2, "contribution": 18.0 }, /* … 8-12 rows … */ ] }

// event row (TBML)
{ "description": "Transaction involves a sanctioned country", "matchedValue": "IR — Port of Loading",
  "origin": "Auto", "severity": "High", "proofRef": true }

// related case link
{ "caseNumber": "CA2024001881", "caseSource": "FCCM", "linkType": "related",
  "why": "Rule 1 — same customer · CIF-000234", "status": "Closed — Confirmed", "closedAt": "2024-11-02" }
```

---

## 7. States to show in every relevant mockup

Pool vs claimed vs awaiting-authorization vs closed (immutable, banner) · unprivileged viewer (buttons absent, not disabled) · self-approval blocked (disabled with reason) · SLA breached (escalation imminent/occurred callout) · FCCM-era read-only case · empty (no events / no related cases) · loading skeletons.

---

## 8. Non-goals

No nav/shell redesign; no new palette or typography; no mobile-first pass (responsive down to 1280 is enough); no correlation-rule admin screen, risk-model admin screen, or audit-pack document design (separate briefs); no logic implementation — HTML/CSS with just enough JS for tab/accordion/dialog demonstration, exactly like the existing `Design Html/` standalones.

## 9. Open UX questions the variations should answer

1. Queue: grid-first (A) vs triage-card/kanban-leaning (B) — which survives 500+ open cases?
2. Detail: tabbed (Transaction-View idiom) vs single-scroll workspace with sticky decision rail — where does the authorizer's recommendation card live in each?
3. Dual-score panel: side-by-side scores with a "prevails" connector, or verdict-first with expandable reasoning?
4. How loud should SLA/auto-escalation be before it becomes noise?

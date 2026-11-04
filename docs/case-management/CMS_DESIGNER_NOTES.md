# CMS — Designer Notes (all screens & workflows)

**Audience**: the designer agent producing standalone HTML mockups. These notes are the complete, screen-by-screen guide — follow them without re-interpreting requirements.
**Scope**: every screen of the Case Management (CMS) module + its integration touchpoints, per `ECM_MASTER_PLAN.md` (functional source of truth — field names, policies and vocabularies below come from it; **do not invent fields or rules beyond it**; items marked `OPEN` are awaiting business confirmation and should be mocked with the stated placeholder).
**Relationship to `CASE_MANAGEMENT_UX_DESIGN_BRIEF.md`**: the brief solicited A/B *variations* for the two core screens (Queue, Case Workspace). These notes assume the variation decision is made and lock the baseline for **all** screens so mockups are coherent and connected.
**Tagging**: every screen is tagged **[NEW]** (design from scratch in the baseline language), **[EXISTING+]** (existing screen gains a section/entry — mock only the delta), or **[REUSE]** (existing pattern re-skinned with new data — reference, don't redesign).

---

## 0. Design-system baseline (hard constraints)

**Primary seed**: `D:\ICBC - Latest\New theme\index.html` — the approved "CMS Unified Compliance Workspace" prototype (src in `New theme/src/`). Its shell, density, and components are the visual law for this module. Secondary references: `Design Html/` standalones (Transaction View, Trade Repository Option A, Manual Red Flags) and the live app theme (`tmx-finance-frontend-revamp/src/index.css` tokens).

- **Shell**: dark navy sidebar (prototype `shell.jsx`) with section headers (small caps, muted); white/very-light content canvas; top bar = product mark ("CMS Unified · Compliance Workspace"), **module switcher chip** (`CMS | switch`), global search ("Search transactions, cases, customers…"), dark-mode toggle, help, settings, user chip (name + role + caret). Dark mode via `[data-mode="dark"]` — every mock in both modes.
- **Color/branding**: ICBC red as the single accent (primary buttons, active nav, case-id links, brand roundel); navy/graphite neutrals; semantic greens/ambers/reds reserved for status/severity/SLA only. **No new palette, no new typography.**
- **Component inventory to reuse (do not redesign)**: status pill (dot + label: Open amber, Under Review blue, Escalated red, Closed green); 3-segment severity bar (Low 1 green / Medium 2 amber / High 3 red segments) + label; **SLA cell** = "48h left" mono text over a thin progress bar (green >40%, amber 15–40%, red <15% remaining; "—" when closed); assignee = initial-avatar + name; case id = red mono link (`CASE-88147` style; CMS uses `TMX-CA000418` format); underline tabs; card sections with 13px table typography; `SelectDropdown`/`DatePicker`/Kendo-filter grid/`CommentsDialog`/toasts from the live app; `df-*` form idioms for any form-heavy panel.
- **Buttons**: primary = filled red; secondary = outline; destructive-adjacent confirmations always via dialog with mandatory remarks (CommentsDialog idiom). Disabled ≠ hidden: privilege-hidden actions are **absent**; state-blocked actions are **disabled with a reason tooltip**.
- **Grids**: server-paged, sortable headers with arrows, filter row above (search + dropdown filters + "More filters"), result count right-aligned, "Saved views" + "Export" top-right (prototype pattern).
- **Maker-checker visual language** (reuse from existing config-management screen): pending-approval badge (amber "Pending approval" chip on a setting/row), approval banner with requester/when, and read-only lock while pending.

---

## 1. Navigation architecture

### 1.1 Module picker **[EXISTING+]**
Post-login `/modules` grid gains one tile: **"Case Management"** (folder-open icon, short description "Investigations, risk scorecards & case workflow"). Visible only when `CaseManagement.Enabled` and `Provider = TmxCms`. Mock: picker with Trade Management + Case Management tiles; second mock with tile absent (Fccm mode).

### 1.2 CMS sidebar **[NEW]** (prototype shell, new entries)
```
Dashboard
CMS            Case Management        (badge: open-case count)
               Onboarding             (KYC assessments list)
               Scorecards
CONFIGURATION  Rule Management
               Risk Scores & Weightage
               Correlation Rules
               Workflow & Queues
               SLA Management
               Mappings
               Notifications
INSIGHTS       Reports
               Audit trail
```
Configuration section renders only for users holding `RiskConfig.Manage` (Workflow & Queues needs `Workflow.Manage`). Active item = red left bar + filled row (prototype). Breadcrumbs on every non-root screen: `Case Management › TMX-CA000418`.

### 1.3 Global search **[NEW behaviour, existing control]**
Top-bar search queries cases (number), customers (code/name), transactions (number); grouped results dropdown with type icons; Enter → full results on the Queue with the filter applied. States: typing (spinner in dropdown), no results ("No matches — check the ID or try customer name").

---

## 2. Operations screens

### 2.1 Dashboard **[NEW]**
**Purpose**: investigator/MLRO landing — workload + risk posture at a glance.
**Layout**: KPI strip (5 clickable cards → pre-filtered Queue): Open cases · In my pool · Aging > SLA · Awaiting authorization · Closed this week. Below, 2-column widgets: Cases by status (stacked bar), Cases by priority (donut), SLA heat (list of worst-aging cases, red-first), Scorecard band distribution (Low/Medium/High columns), My recent activity (timeline). Right rail optional: "Pending approvals" (config changes awaiting this checker).
**States**: loading skeleton cards; empty ("No open cases — queues are clear") with illustration-free quiet style.

### 2.2 Case Management — Queue **[NEW]** *(prototype `cases.jsx` is the base — extend, don't reinvent)*
**Purpose**: the work queue across pools and personal claims.
**Header row**: title "Case Management", right: Saved views, Export.
**Tabs**: All cases · Assigned to me · Open · Under review · Escalated · Closed. Add **category filter chips**: All · KYC · TBML (per master plan §10.4 — cases carry `Case_Type`).
**Filter bar**: search (case ID / customer), Severity dropdown, SLA dropdown, **Queue/Tier** dropdown (L1/L2/L3/Authorizer — NEW vs prototype), More filters (type, date range, assignee, disposition), count right.
**Columns** (prototype order, two additions): Case ID (red mono link, `TMX-CA…`) · Customer (name; code on hover tooltip) · Product/Type (KYC | Import | Export… from linked transaction) · Severity bar · Status pill · **Tier/Queue** (NEW: "L2 pool" or "L1 · claimed") · Assignee (avatar+name, or "Pool" ghost chip) · SLA (time-left bar; red "Breached 6h" when over; "—" closed) · Periodic re-review date (KYC rows; "—" TBML).
**Row actions**: hover reveals "Claim" (pool rows, `Cases.Operate`) and "Open". Claim = optimistic: row flashes, assignee becomes current user, toast "TMX-CA000421 assigned to you", with Undo (5 s). Bulk select (checkboxes) enables bulk Export only — **no bulk decisions** (audit gate).
**States**: loading = 6 skeleton rows; empty tab ("No cases in this view. Adjust filters or pick a different tab." — prototype copy); filter-no-match; >25 rows → pager (existing grid pager); error banner with retry.

### 2.3 Case Workspace (detail) **[NEW]** *(prototype `kyc-case-detail.jsx` fullscreen overlay is the base)*
**Purpose**: everything needed to investigate and decide one case. Opens as fullscreen overlay from the Queue (prototype behaviour) with breadcrumb back.
**Hero band** (dark, Transaction-View idiom): case number + type chip (KYC / TBML) + priority chip + status pill + tier ("L2 · Assigned: M. Tariq" or "L1 pool") · customer (name + code, → Scorecards) · linked transaction number (→ existing Transaction View) · age + SLA bar · **action slot top-right** (workflow-command-driven; primary = Claim when pooled / Recommend closure when claimed / Approve when authorizer; overflow ⋮ for Escalate, Reassign, Request info, Return to pool). Actions render from the commands API — design slots, not fixed labels.
**Tab rail** (underline tabs; tab set gated by `Cases.Tab.*` privileges):
- **KYC case tabs**: Overview · KYC Onboarding (assessment context) · **Comparison** (re-review/modification old-vs-new — §2.6 component) · Events · Notes · Parties · Audit.
- **TBML case tabs**: Overview · Events (centrepiece) · Trade details · Notes · Parties · Audit.
**Overview tab**: left column — Risk panel (KYC: the dual-score component §2.5; TBML: compact band chip + flag count); Recommendation card when awaiting authorization (disposition chip, remarks, recommender avatar, when — the authorizer's primary read); Related cases card (linked cases with type chip + "why linked: Rule 1 — same customer · CIF-000234" + source badge `CA…` for FCCM-era rows, read-only). Right rail — case facts (opened/by, jurisdiction, queue history mini-list), attachments (upload via existing Document pattern, SHA-256 note), audit-pack export button (`Cases.View` + oversight).
**Events tab**: table = Description · Matched value (mono, highlighted) · Origin (Auto/Manual chip) · Severity · Proof (link opens auto-flag proof popover) · Created. TBML: 1 customer event + N flag events — **events ARE the raised red flags**; KYC: assessment events. Empty state (KYC case with no events): "No flag events — this case was generated by the risk policy."
**Notes tab**: chronological cards (author, role, timestamp, text), "Add note" (CommentsDialog). **Audit tab**: unified timeline — status changes, assignments, escalations (auto vs manual tagged), decisions, config-relevant stamps; filter by action type.
**Blocked/edge states to mock**: closed case = grey banner "Closed — Confirmed (STR candidate) · by R. Iqbal · 2026-09-22", all actions absent, content read-only; **self-approval block** = Approve disabled + tooltip "You worked this case — another authorizer must approve" (AG3); unprivileged viewer = action slot absent entirely; FCCM-era case (opened from history) = source badge + reduced tabs (Overview + Audit only) + banner "Managed in Oracle FCCM — read-only record".

### 2.4 Decision dialog family **[NEW]** (one component, parameterized)
CommentsDialog idiom: title, context line, disposition select (only where applicable), mandatory remarks (validation: required, ≤1000 chars — inline error "Remarks are required"), cancel/confirm.
- **Recommend closure**: disposition options per case type (KYC: Approved / Rejected / Returned · TBML: Confirmed (STR-candidate) / False Positive / No Action / Returned); summary line "Recommending closure of TMX-CA000418 for Rabia Ahmed Test 05".
- **Approve closure**: shows the recommendation being approved (disposition + recommender + remarks excerpt); confirm = filled red. Mock the blocked variant (§2.3).
- **Reject recommendation / Escalate (to L2/L3) / Reassign / Request info / Return to pool**: remarks-only variants; Reassign adds an assignee SelectDropdown (role-filtered). 
- Success = toast + hero/status refresh; failure = inline dialog error banner (envelope message), dialog stays open.

### 2.5 Dual-score risk panel **[NEW component]** (used in KYC Workspace + Assessment detail)
Three-zone card: **Algorithm** (score 61.25 + band chip Medium + "View breakdown" expander → table Parameter · Value · Score · Weight · Contribution, from `Risk_Scorecard_Detail`) | **Rule-based** (band chip High + fired-rule list: name + terms "KycParam9 = 'Yes' (PEP)" + reason) | **Effective** (large band chip + score + "Superseded by rule" tag when applicable + `Rcmnd_To_Onbrd` chip). A connector arrow highlights which side prevailed ("higher wins"). Below: reconciliation line — "Reconciled vs BRAINS: High (TMX prevails)" or "Single-source assessment" tag when reconciliation disabled; "Reconciliation pending — retry available" (amber) for the degraded state. Override, when present: struck-through original + override score + by/at/reason tooltip. **3-second rule**: effective band + why must read at a glance; arithmetic is progressive disclosure.

### 2.6 Onboarding — KYC Assessments **[NEW]**
**Purpose**: FCCM-parity assessments surface — every onboarding/review/refresh, whether or not a case was generated.
**Layout**: Queue-style grid. Filters: search (customer), band, trigger (Onboarding/Periodic review/Refresh/Override), date range, "Case generated" (Yes/No/All). Columns: Customer (name+code) · Scorecard version (v4) · Trigger chip · Algorithm band · Rule band · **Effective band** (bold chip) · Score (mono) · Reconciliation status chip (Done/Disabled/Pending) · Case (red case-number link, or "— (policy)" with tooltip listing which §10.4 criteria evaluated) · Generated at.
**Row → Assessment detail** (slide-over or page): the §2.5 panel full-width + inputs summary + policy-verdict card ("Policy evaluated: Band ≥ High ✗ · PEP ✗ · Flags ≥ 1 ✗ → no case") + "Compare with previous" button → §2.7.
**States**: empty ("No assessments yet for the selected filters"); Pending reconciliation rows show amber dot.

### 2.7 Scorecards — customer history & comparison **[NEW]**
**Purpose**: 1 customer → many scorecards; trend + factor-level deltas (prototype's Comparison tab generalized).
**Customer header**: name, code, type, current band chip, onboarded date, last reassessed, next review date, status.
**History strip**: horizontal version timeline (v1…vN dots colored by band) + sparkline of effective score; click selects a version.
**Comparison view** (default latest vs previous; two version pickers): top — two band chips with delta arrow ("Low → High ▲"); table — Factor · Before · After · Δ Score · Δ Contribution, sorted by |Δ|, movers highlighted ("Country of Residence 45 → 90 · +18.0 — drove the band change"); rule outcomes row-group with NEW / CLEARED badges; footer — model version note ("Scored by model v3; previous v2") + triggers + dates.
**States**: single-scorecard customer ("No previous assessment to compare — this is the first scorecard"); mixed-era rows (FCCM-sourced scorecards show source badge + "breakdown unavailable" for factor table).

---

## 3. Configuration screens (all maker-checker: every save shows "Submitted for approval" toast + amber pending chip until approved; history icon per row — reuse existing config-management mechanics)

### 3.1 Case Management config category **[EXISTING+]**
The existing `/admin/configuration` screen (categories rail left, settings list right, verify chips, history/rollback) gains the renamed **Case Management** category. Mock three variants: (a) Provider = OracleFccm → Provider section (Enabled toggle, Provider enum select with "applies on restart" warning chip, Reconciliation toggle) + Oracle FCCM section (connection Secret row with reveal, onboarding URL, trade-case defaults, polling/timeout, action-code map JSON row) + "Test FCCM connection" / "Test onboarding call" chips; (b) Provider = TmxCms → Provider section + TMX CMS section (case-number prefix, correlation toggles, scheme selects, SLA enable, cache TTLs) + "Verify CMS readiness" chip with result popover (checklist: score map ✓, active model ✓, schemes ✓, policy ✓); (c) provider change pending approval (banner + locked).

### 3.2 Risk Models / Algorithm Builder **[NEW]**
**List**: grid of models — Jurisdiction · Customer type (IND/LE/FI) · Model type (Real-time/Batch) · Version · Status chip (Draft grey / Active green / Retired muted) · Effective from · actions (Open, Copy). "New model" = Copy-from (Oracle parity).
**Detail**: header (model identity + status + Activate button, `requiresApproval`); **Parameters & weights grid** (Oracle parity): Parameter · Weight (inline numeric edit in Draft; read-only in Active) · Comments; footer Σ-weights indicator; "Add parameter" → picker from Parameter Library. Draft/Active duality is the key visual: Draft = editable + "Publish" primary; Active = locked + "Copy to new version".
**Validation**: weight numeric >0; publish blocked until ≥1 parameter ("A model needs at least one weighted parameter").

### 3.3 Parameter Library **[NEW]**
Grid: Code (mono) · Label · Data type · Source binding (mapping key, mono) · Lookup type chip · Multi-value strategy · Active toggle · Used-in count (models/rules — tooltip lists them). Slide-over editor with those fields; Lookup type = SelectDropdown of `TmX_Lookup` types. Deactivation guard: "Used by 2 active models — deactivate anyway?" confirm dialog.

### 3.4 Score Maps (reference sheets) **[NEW]**
**Purpose**: the value→score "sheet" (AF=10, PK=8…).
**Layout**: parameter picker (left rail list, searchable) → sheet grid right: Code value (picked from the parameter's bound lookup — SelectDropdown, never free text) · Label · Score (inline numeric) · scope chip (Global / Model override). Toggle "Show model overrides" + model picker. Toolbar: "Import from FCCM export" (file dialog → preview diff table: new/changed/unchanged counts → confirm) — reuse validate-file dialog pattern. States: unmapped lookup values hint ("12 lookup values have no score — they resolve to 0"; OPEN: confirm default-score rule, mock with this copy).

### 3.5 Rule Management **[NEW]** (3 tabs)
- **Risk rules**: priority-ordered list (drag handle, Rule no, name, predicate summary chip, outcome chip (Force band High / Accelerate re-review…), stop-on-match toggle, active). Editor slide-over: name; **predicate composer** = Auto-Flags condition-row idiom (parameter select · operator · value/lookup multi-select); outcome select + value; reason template text. "Test rule" dry-run panel: pick a customer/sample payload → verdict + matched terms (reuse Auto-Flags Test pattern).
- **Accelerated re-review rules**: same list idiom; outcome fixed to "Shorten next review to N months".
- **Case-generation policy**: rows = Trigger kind select (Band at least / Score at least / PEP / Not recommended / Rule hit / Flag count at least / Flag severity at least / Flag codes / Expression / Always / Never) · Value input (shape follows kind) · Combine chip (OR / AND-with-next, visually grouped by bracket line) · per-case-type tabs (KYC | TBML) · optional priority/queue override selects. Banner: "Seeded defaults preserve current behaviour".

### 3.6 Thresholds (band-range tuning) **[NEW]** — already specified in detail
Per model: editable grid LOW/MEDIUM/HIGH with Score_From/Score_To numeric inputs + **visual range bar** (colored segments over a 0–100 axis, draggable boundaries optional); inline convention note "Lower bound inclusive · upper exclusive · 40.00 = MEDIUM"; live validation errors under the bar ("Gap between 40 and 45", "Bands overlap at 70"); auto-snap behaviour (editing LOW's To moves MEDIUM's From — animate both fields); **Impact preview** accordion: "Under drafted ranges, of the last 200 assessments: 12 LOW→MEDIUM, 3 MEDIUM→HIGH" + mini table; Recommendation thresholds section (band → Onboard/Review/Don't-onboard select). Publish = maker-checker.

### 3.7 Correlation Rules **[NEW]**
Priority list + slide-over editor: name; source/target case-type multi-selects; **match-context builder** (attribute chips: Customer, Counterparty, National ID, Transaction ref, Product/Sub-product, HS family, Flag codes, Branch — each exact/normalised toggle); lookback days (0 = unlimited); include-closed toggle; action select (Link related / Link duplicate / Attach events / Flag for review); stop-on-match. Row shows a human sentence preview: "Same customer, any case type, unlimited lookback → link as related". Seeded Rule 1 pre-rendered.

### 3.8 SLA Management **[NEW]** (own screen)
Two panes: **Config grid** — rows per (Case type · Priority · Tier): Threshold hours · At-risk % · Auto-escalate toggle (target tier auto-derived) · active; inline edit. **Breach log** tab — read-only grid (Case link · tier · breached at · escalated to · acknowledged). Info banner distinguishing from the global transaction `'sla'` category: "Transaction SLA scanner settings live in Configuration → SLA / Deadlines".

### 3.9 Workflow & Queues **[NEW]**
Simple two-card screen: **Scheme mapping** — Case type → Scheme select (from deployed schemes; link-out "Edit schemes in Process Designer"); **Queue routing** — grid: Tier · Role (SelectDropdown of roles: Case Analyst L1–3, Case Authorizer L1–3) · routing notes (OPEN: routing beyond role-per-tier unconfirmed — mock read-only role mapping only).

### 3.10 Mappings **[NEW]**
Two tabs. **Field mappings** (the `Field_Mapping` editor): filter bar (Target entity select: Customer / Snapshot / Trade detail / Trade party / Trade goods · Product · Sub-product tab · Customer type); grid: Target column (mono) · Source kind chip · Source path (mono, e.g. `ILF_PortLoadingCtry`) · Transform chip · Grid strategy · Required · Active; slide-over editor with those controls (source path picker = searchable field list from the dynamic-form catalogue); provenance note under title: "Changes apply to new materialisations only — captured snapshots stay frozen." **Profiles** tab: JSON profile registry (name, kind chip, version, active) with read-only JSON viewer + upload-new-version (validate-file pattern).

### 3.11 Notifications **[NEW]**
Rules grid (Event select: Assigned / Escalated / SLA at-risk / SLA breach / Recommended / Decided · Recipients (role multi-select / actor chips) · Template select · active) + Log tab (read-only: when, event, recipient, status chip Sent/Logged-only). Banner while transport is pending: "Delivery transport not yet enabled — rules evaluate and log only." (Slice-8 dependency; do not mock email composition.)

---

## 4. Integration touchpoints **[EXISTING+]** (mock only deltas)
- **Transaction View → Risk Assessment tab** (exists): add a "Case" row in the identifiers strip linking `TMX-CA…` when a case exists; scorecard link "View scorecard v4 →".
- **Trade transaction finalise**: existing success toast gains "Case TMX-CA000431 created (3 flags)" line when policy fires; CA-NOHIT unchanged.
- **Module switcher chip** in top bar toggles Trade ↔ CMS (prototype behaviour); deep links across modules open in the owning module.

---

## 5. Cross-cutting states & validation (apply to every screen)
Loading = skeletons (rows/cards), never spinners-only; Empty = one quiet sentence + primary next action; Error = red banner with envelope message + Retry; Forbidden = content absent (nav hidden) — only deep-link hits show the existing NoAccess page; Pending approval = amber chip + locked controls + "requested by/when"; Restart-required settings = persistent info chip "Applies after restart"; Form validation = inline under field, red border, submit disabled until valid; all destructive/decisive actions = dialog + mandatory remarks; toasts for success, banners for failure.

---

## 6. End-to-end journeys (storyboard each as a connected mock sequence)
1. **High-risk onboarding → case → dual-control closure**: analyst authorizes KYC txn → (system) scorecard v1 HIGH (rule: PEP supersedes algorithm Medium) → policy fires → case in KYC queue L1 pool → L1 claims → reviews dual-score panel + comparison (n/a, first scorecard) → Recommend closure (Rejected) → Authorizer L1 opens, sees recommendation card, Approves → case Closed, transaction status Rejected, audit complete. Mock: queue (pool) → workspace (claimed) → recommend dialog → authorizer view → closed state.
2. **TBML flags → case → parallel approval**: trade finalises with 3 flags → case w/ 4 events (3 flags + customer) → correlation banner "2 related cases (same customer)" → L1 escalates to L2 (SLA amber) → L2 recommends Confirmed (STR-candidate) → parallel approver set (2 authorizers) — approval progress chip "1 of 2 approved" → closure. Mock: events tab, escalate dialog, approval-progress state.
3. **Low-risk onboarding, no case**: assessment appears in Onboarding list with "— (policy)" + verdict tooltip; nothing in queue. One mock.
4. **Threshold tuning**: admin edits MEDIUM From 40→45 → auto-snap animation → impact preview → submit → pending-approval chip → checker approves (existing requests screen) → history row. Two mocks (draft+preview, pending).
5. **Provider switch**: config category in Fccm mode → change Provider to TmxCms → approval → restart note → module tile appears. Two mocks (§3.1 variants cover).

---

## 7. Deliverables checklist for the designer agent
Standalone HTMLs (desktop 1440, light+dark, seeded with master-plan-shaped data, binding comments `<!-- bind: … -->`): module picker delta · CMS dashboard · Queue (default + pool/claim + empty) · Case Workspace KYC (claimed, awaiting-auth, closed, self-approval-blocked) · Case Workspace TBML (events + parallel-approval progress) · decision dialogs (open states) · Onboarding list + assessment detail · Scorecards comparison · config category (3 variants) · Risk model detail (draft vs active) · Parameter library · Score maps (+ import preview) · Rule Management (3 tabs) · Thresholds (+ impact preview) · Correlation rules · SLA · Workflow & Queues · Mappings · Notifications. Naming: `CMS - <Screen> - <State> (standalone).html` into `D:\ICBC - Latest\Design Html\`. Plus a 1-page flow map image/HTML linking screens per §6 journeys.

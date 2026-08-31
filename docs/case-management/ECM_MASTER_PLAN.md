# TMX Enterprise Case Management (ECM) — Consolidated Master Plan

**Status**: 📐 Master plan for approval — pre-implementation, no code changed.
**Date**: 2026-07-15
**This is the single source of truth.** It consolidates and **supersedes** `FCCM_ANALYSIS_AND_DUAL_MODE_ARCHITECTURE.md` and `CASE_MANAGEMENT_MODULE_PLAN.md` (kept for history with superseded banners). The workflow-engine foundation remains a separate initiative: `../WORKFLOW_ENGINE_REPLACEMENT_PLAN.md`. UX variations brief: `CASE_MANAGEMENT_UX_DESIGN_BRIEF.md`.

**Decisions locked with FSI (chronological):**
1. BRAINS stays in every mode; TMX reconciles against it ("higher wins / PEP always wins").
2. Case module executes on the FSI workflow engine.
3. Persistence for the workflow engine: drop-in on existing tables (separate plan).
4. Single consolidated master doc (this document).
5. Modes: **binary — Oracle FCCM | TMX CMS** (`OracleFccm` | `TmxCms`) — admin selects exactly one. *(Hybrid dropped per FSI review 2026-07-15; migration overlap is handled by in-flight continuity + shadow scoring, not a routed mode — §5/§19.)*
6. Provider switching = **configuration + restart** with startup validation (no live toggle).
7. Workflow hierarchy: **configurable default** — 3 analyst + 3 authorizer levels shipped as an editable scheme, exactly like today's transaction Analyst/Authorizer schemes.
8. Every onboarding/review produces a **Customer Risk Scorecard** (1 customer → many scorecards) with previous-assessment comparison.
9. Application must clear **all audit gates** (§17).
10. Same repository, logically independent module; dedicated **`CMS`** DB schema (renames the earlier `CaseManagement` schema naming).
11. *(Review 2026-07-16)* **Licensing de-prioritized** — `Enabled` + `Provider` config gates suffice for now; the license-file gate moves to a later phase behind the same interface.
12. *(Review 2026-07-16)* **Conditional configuration & privileges** — selecting a provider surfaces only that provider's settings (UI + backend binding/validation) and only the relevant privilege groups in Role Management.
13. *(Review 2026-07-16)* **Structured data layer in CMS** — customer registry + typed transaction/trade snapshot tables (Oracle-style structured columns) for rule execution, reporting and audit, populated via mapping profiles; `Case_Source` dropped from `CMS.Case` (era lineage lives on the dbo ledger); scorecard gets a queryable per-component breakdown table and drops the BRAINS field.

---

## Contents

1. [Live Oracle FCCM instance analysis](#s1) · 2. [Current system analysis](#s2) · 3. [FCCM feature comparison matrix](#s3) · 4. [Target architecture & module breakdown](#s4) · 5. [Provider abstraction](#s5) · 6. [Configuration management](#s6) · 7. [Licensing](#s7) · 8. [CMS database design + ERD](#s8) · 9. [Rule & algorithm management](#s9) · 10. [Scoring engine & Risk Scorecard](#s10) · 11. [Workflow design](#s11) · 12. [Correlation engine](#s12) · 13. [Mapping module](#s13) · 14. [Notification / Reporting / Search / Dashboard](#s14) · 15. [API design](#s15) · 16. [UI planning](#s16) · 17. [Audit & traceability](#s17) · 18. [Sequence diagrams](#s18) · 19. [Migration strategy](#s19) · 20. [Risks](#s20) · 21. [Assumptions](#s21) · 22. [Recommendations & future enhancements](#s22)

---

<a id="s1"></a>
## 1. Live Oracle FCCM instance analysis (performed 2026-07-15)

`http://172.20.3.73:7001/ofsaa/ojff/index.jsp` **was reachable and analyzed** through an authenticated `KYCADMIN` session (read-only navigation; nothing modified). Findings:

- **Installed OFSAA suite**: Customer Screening, Transactions Filtering, Anti Money Laundering, **Enterprise Case Management**, **Know Your Customer**, Regulatory Reporting, **Risk Override** (Oracle 2023/2025 builds).
- **KYC admin menu** (Behavior Detection – KYC): KYC Assessments · Onboarding KYC Assessments · User Security Administration · Manage KYC Configuration · Manage KYC OB Configuration · **KYC Risk Assessment Configuration** · Reports · **Case Management Configuration** · Questionnaire.
- **KYC Risk Assessment Configuration submenu** — this is Oracle's risk-model machinery, confirming our replication target item-for-item: **Rule Based Risk Assessment · Algorithm Based Risk Assessment · Accelerated Rules · Risk Assessment Category** (band ranges) · **Real Time Account On-boarding Risk Assessment · Account Customer Role · Risk Score for Parameter/Rule Value** (the code→score maps = `FCC_OB_RISK_PRMS_CODE_SCRE_MAP` admin UI) · **Association of Rule/Risk Parameter to Evaluation · Relationship Type Definition**.
- **Model dimensions observed**: Jurisdiction × Model Type (**Real-time Onboarding Algorithm Based Risk Assessment** vs batch **Algorithm Based Assessment**) × **Customer Type** (Individual | Legal Entity/"Other Institution" | Financial Institution). Parameter grid columns: `Risk Parameter | Jurisdiction | Weight | Comments`, with **Save / Copy / History** actions (History = Oracle's own config-change audit — a capability our config maker-checker + versioning matches).
- **Key empirical finding**: with the only selectable jurisdiction ("All"), the algorithm parameter grids are **empty in the UI for both model types** — while the DB score map is demonstrably populated (TMX's live max-score queries return values). ⇒ **The model export for calibration (§10.6) must come from the database tables, not the admin UI.** The UI structure still validates the shape of what we replicate.
- Not exercised (session scope, read-only): ECM investigator screens, batch schedulers, Questionnaire. Their capabilities are covered from integration evidence + product knowledge in §3 and flagged accordingly.

---

<a id="s2"></a>
## 2. Current system analysis (TMX ↔ Oracle FCCM)

Full detail preserved in the superseded analysis doc; the operative inventory:

### 2.1 Every touchpoint

| # | Touchpoint | Direction | Mechanism |
|---|---|---|---|
| T1 | KYC onboarding submit | TMX→FCCM | `POST {FccmOnboardingUrl}/InitiateOnboardingService/OB/Initiate`, Basic auth, JSON payload built by config-driven `OnboardingPayloadMapper` (profiles v1/v2/legacy) or hardcoded fallback resolver |
| T2 | Risk-parameter max-score | TMX→FCCM Oracle | `SELECT … FCC_OB_RISK_PRMS_CODE_SCRE_MAP WHERE SCORE=MAX(...)` during payload build |
| T3 | Case-id + risk poll | TMX→FCCM Oracle | `FccmCaseIdPoller` (5 s): `FCC_OB_RA.CASE_ID`, then `RISK_CATEGORY_KEY`; ledger `dbo.KycCaseRequest` `Submitted→AwaitingCaseId→CaseCreated→RiskAssessed` (+`Failed/Timeout`) |
| T4 | Risk reconciliation | internal | FCCM band vs BRAINS band (0=PEP wins; higher wins) → `Cstm_Brains` + snapshot UDF `$.RiskScore` |
| T5 | FCCM write-backs | TMX→FCCM Oracle | `KDD_CASE_CUSTOMERS.FULL_NM` sync; `KDD_CASES.PRIORITY_CD` from risk |
| T6 | Decision callback | FCCM→TMX | `POST /api/v1/Kyc/Case/Callback` (+2 legacy aliases), `{caseID, actionCode}` → `FccmActionCodeStatusMap` (30004/983 Approve, 30003/984 Reject; legacy 3rd branch = send-back-to-analyst) → transaction status + remark + review schedule |
| T7 | TBML trade case | TMX→FCCM Oracle | 14-op pipeline: truncate `RT_CASE_TRACKING`/`EVENT_STAGING` → `MESSAGE_ID_SEQ` → 3 staging inserts → 1+N event rows → `TMX_TRANS_FROM_REPO` → `PR_CREATE_EVENT_DEMO_CFG` (OUT `RESID`) → `KDD_CASE_CUSTOMERS` + `KDD_CASES` title → stamp `Fccm_Case_Id`. **`redflagsCount==0` ⇒ CA-NOHIT** (status 3059, sentinel case id, no FCCM contact) |
| T8 | Resubmit | TMX→FCCM | `POST /Transaction/{id}/ResubmitToFccm` (`Transactions.Resubmit`) |
| T9 | Retry/Repoll/Refresh | TMX ops | `Kyc.Operate` operations on the ledger row (Risk Assessment tab) |
| T10 | BRAINS↔TBML batch files | FCCM↔BRAINS | `TBML_RISKLEVEL`/`BRAINS_RISKLEVEL` Etran file exchange (interface doc) — FCCM-side obligation to re-home on retirement |

**Seams already clean**: `IKycCaseService`, `IFccmTradeCaseService` (to rename `ITradeCaseService`), `IKycCaseOperationsService`, `IKycScreeningService` — Application layer is vendor-neutral; vendor names only in Infrastructure. **Known defects to fix regardless of mode** (into Phase 0): callback `[AllowAnonymous]` with `FccmCallbackSharedSecret` declared-but-unenforced; silent Oracle stub when connection blank (even in Production); missing legacy send-back callback branch. **Environment reality**: target Oracle env (`OFSAAFCCM` schema) lacks the 8 trade-staging objects since 2026-06 — the TBML write path is effectively stubbed there today.

### 2.2 Config surface (today)

`Integration:` `FccmOnboardingUrl`, `Onboarding.{Username,Password,ApplicationIdFormat,MappingProfile,MappingDirectory,MappingHotReload,Include*}`, `FccmOracleConnection`, `FccmOracleSchema`, `TradeCaseDefaults.*` (14), `FccmActionCodeStatusMap`, `PollingInterval`, `CaseIdTimeout`, `FccmCallbackSharedSecret`, `Brains.{Mode,BaseUrl,ZoneNo,AdjustType,…}`.

---

<a id="s3"></a>
## 3. Oracle FCCM feature comparison matrix

Legend — Oracle impl: **[USED]** exercised by this deployment · **[BYPASSED]** exists but routed around · **[INTERNAL]** used inside FCCM, invisible to TMX · **[PK]** product knowledge, not directly observed. Phase: P0 seams/fixes · P1 core internal CMS · P2 module completion · P3 later.

| # | Feature | Oracle implementation | Proposed TMX implementation | Gap | Enhancement beyond Oracle | Phase |
|---|---|---|---|---|---|---|
| 1 | Enterprise Case Management (container) | `KDD_CASES` (`CA{seq}`, `STATUS_CD`, type/subtype `TBML_SURV`, owner org, jurisdiction) [USED] | `CMS.Case` + `TMX-CA{seq}`, type, status lookups, priority, jurisdiction | New build | Source lineage (FCCM/TMX eras side-by-side) | P1 |
| 2 | Case lifecycle | `CR →` internal statuses `→` closed [INTERNAL] | `Created→Assigned→Under Investigation→(Escalated)→Closed(disposition)` on CaseWF; reopen = linked new case; closed immutable | New build | Explicit disposition taxonomy per type; immutability | P1 |
| 3 | Correlation engine | `FCC_CORRELATION_EVENT_MAP`, `FCC_PRECASE_CASE_MAP`, `CASEIDTOTRANSACTION` — hand-inserted by SP here [BYPASSED] | Rule-driven `CaseCorrelationEngine` + `CMS.Correlation_Rule` (context, case types, lookback, closed-inclusion, action) | New build | Admin-configurable context + link explainability (rule + matched values recorded) | P1 |
| 4 | Accelerated Review / Accelerated Rules | "Accelerated Rules" config screen (verified live §1) — rules that accelerate risk/re-review [USED] | `CMS.Risk_Rule` outcomes incl. `Force_Band`/acceleration + `CMS.Accelerated_ReReview_Rule` → shortens `CustomerReviewSchedule.NextReviewDate` on trigger | New build | Rule hit + terms visible on scorecard | P1/P2 |
| 5 | Rule-based scoring | "Rule Based Risk Assessment" screens [USED] | `CMS.Risk_Rule` (priority, predicate on parameters, outcome, stop-on-match) — always evaluated | New build | Full rule-hit audit JSON | P1 |
| 6 | Algorithm-based scoring | Jurisdiction × ModelType × CustomerType weighted parameters (`Weight` grid, verified live) → `FCC_OB_RA.RISK_SCORE(5,2)` [USED] | `CMS.Risk_Param_Weight` per model; weighted aggregate `Σ(wᵢsᵢ)/Σwᵢ` decimal(5,2); **both stages always compute** | New build; weights must be DB-exported (§10.5) | Per-parameter contribution breakdown exposed | P1 |
| 7 | Risk scoring (bands/override/recommendation) | `RISK_CATEGORY_KEY` via "Risk Assessment Category" ranges; `OVERRIDDEN_RISK_SCORE`; `RCMND_TO_ONBRD`; separate **Risk Override** app [USED] | `CMS.Risk_Band_Config` ranges; override w/ by/at/reason; recommendation config | New build | Dual-score display with supersession terms | P1 |
| 8 | Alert grouping | Events grouped to case (`FCC_EVENTS`→case maps) [BYPASSED — SP fakes it] | `CMS.Case_Event` rows grouped per case; correlation `Attach_Events` action groups cross-transaction | New build | Flag proof links per event | P1/P2 |
| 9 | Investigation workflow | ECM internal workflow [INTERNAL] | `CaseWF` on FSI workflow engine (§11) | New build | Transparent, designer-editable in TMX | P1 |
| 10 | Assignment & routing | ECM owner/assignee (SP defaults id 10004) [INTERNAL] | Pool→claim (`FillAllUsersBucket`/`AssignToMe` narrowing — proven), queue routing per case type/priority via scheme actors | New build | — | P1 |
| 11 | Escalation | ECM escalation [INTERNAL] | Manual `EscalateTo*` + SLA auto-escalation via `AppDelay*` conditions | New build | Timed auto-escalation without timer engine | P1 |
| 12 | Queue management | ECM queues [INTERNAL] | `WorkflowInbox`-backed queues per tier/role + Cases FE queue page (KPIs, filters) | New build | — | P2 |
| 13 | Analyst/Authorizer hierarchy | ECM roles [INTERNAL] | Default 3 analyst + 3 authorizer levels, scheme-configurable (§11) | New build | Levels editable per bank, no code |. P1 |
| 14 | SLA management | ECM SLAs [PK] | `CMS.Sla_Config` per case type/priority/tier: thresholds drive aging states + auto-escalation + dashboard | New build | — | P2 |
| 15 | Case notes | ECM narrative [INTERNAL] | `CMS.Case_Note` (+ existing CommentsDialog idiom) | New build | — | P1 |
| 16 | Attachments | ECM documents [INTERNAL] | Reuse `TmX_Document` infra (upload/hash/storage) + `CMS.Case_Attachment` bridge | Small | SHA-256 evidence hashing already standard | P2 |
| 17 | Audit trail | ECM case audit [INTERNAL]; config History button (live) | Workflow history + `CMS.Case_Audit` + hash-chained `TmX_Audit_Log` + config maker-checker/versions | New build | One-query audit pack (§17 AG11) | P1 |
| 18 | Decision history | Callback tri-state only crosses to TMX [USED] | `CMS.Case_Decision` rows (recommend/approve/reject/send-back, actor, remarks, disposition) | New build | Full in-TMX decision chain incl. recommendation stage | P1 |
| 19 | Watchlist handling | Customer Screening app [PK; separate product] | Out of CMS scope — BRAINS + auto-flags reference lists cover screening; watchlist-class risk rules supported via `Risk_Rule` | By design | — | — |
| 20 | Related entities | `KDD_CASE_TRD_FIN_{CNTRCT,GOOD_SRVC,PARTY}` flat snapshots [USED-lite] | Case references live transaction/customer (no copies) + `CMS.Case_Party` where needed | Small | Live data, not stale staging copies | P1 |
| 21 | Entity relationships (graph) | ECM entity link UI [PK] | Deferred; `Case_Link` + customer case-history covers the investigative need v1 | Accepted gap | Graph visualisation as future premium feature | P3 |
| 22 | Configurable parameters | Parameter/weight admin (verified live) | §9 builders (parameter registry, weights, thresholds, lookups) with maker-checker | New build | `TmX_Request` dual control on every change | P1 |
| 23 | Threshold management | Ranges + thresholds screens [USED] | `Risk_Band_Config`, rule predicates, SLA thresholds, correlation lookbacks — all table-driven | New build | — | P1/P2 |
| 24 | Dynamic rule configuration | Rule screens + Questionnaire [USED/PK] | Safe-expression predicates (Auto-Flags grammar reuse) — no code deploys for new rules | New build | Shared expression engine across flags/risk/correlation | P1 |
| 25 | Case disposition | ECM dispositions [INTERNAL] | Typed per case type: KYC `Approved/Rejected/Returned`; TBML `Confirmed/False Positive/No Action/Returned` | New build | — | P1 |
| 26 | Reporting | OFSAA Reports menu [PK] | Existing TMX report engine (DotLiquid/PDF/Excel) + case SPs; regulatory extracts pending §21 A6 discovery | Partial | — | P2 |
| 27 | Dashboards | ECM dashboards [PK] | Cases KPI strip (P2) + operations dashboard widgets (existing Dashboard module) | Partial | — | P2/P3 |
| 28 | Search | ECM case search [PK] | Queue filters + global case search (number/customer/transaction) on indexed columns | New build | Cross-era search (FCCM+TMX cases) | P2 |
| 29 | Duplicate detection / entity resolution | Onboarding existing-customer match (`EXISTING_CUST_INTRL_ID`) [INTERNAL] | Correlation rule `Link(duplicate)`/`Flag_For_Review`; full entity resolution deferred | Partial | — | P1/P3 |
| 30 | Case merge/link | ECM linking [INTERNAL] | `CMS.Case_Link` typed (`related/duplicate/reopens/merged_into`) + merge with event re-parent | New build | Link provenance (rule + matched values) | P2 |
| 31 | Periodic review scheduling | FCCM re-review + BRAINS `D_NEXT_REVIEW_DATE` [SHARED] | **Already TMX-owned** (`CustomerReviewSchedule`, `KycReviewOrchestrator`, `INextReviewDateCalculator`) + accelerated re-review rules (#4) | None | — | done/P2 |
| 32 | Questionnaire (onboarding Q&A) | KYC Questionnaire module [PK, seen in menu] | Covered by TMX dynamic forms (Section A–F capture) — richer than FCCM's questionnaire for this deployment | By design | — | — |

---

<a id="s4"></a>
## 4. Target architecture & module breakdown

Same repository; logically independent module; extraction-ready (contracts-only coupling, own DbContext/schema/connection key, `IComplianceModule`-shaped registration per `MODULARISATION_PLAN.md`).

```text
+---------------------------------------------------------------------------------+
| FSI.Trade.Compliance HOST (Trade/KYC slices — unchanged)                        |
|   Workflow finalise hook          Risk Assessment tab + ops    Periodic Review  |
|   (IKycCaseService/ITradeCase)             |                    Orchestrator    |
+---------------|----------------------------|--------------------------|---------+
                v                            v                          v
+---------------------------------------------------------------------------------+
| CASE MANAGEMENT MODULE  (Features/CaseManagement + Infrastructure/CaseManagement)|
|                                                                                 |
|   GENERIC API: CaseManagementController (/api/v1/CaseManagement/*)              |
|                       |                                                         |
|                       v                                                         |
|   PROVIDER ROUTER  (mode from config; in-flight continuity by Case_Source)      |
|        |                                   |                                    |
|        v  Mode=Fccm                        v  Mode=TmxCms                     |
|   +-------------------------+       +---------------------------------------+  |
|   | ORACLE FCCM PROVIDER    |       | INTERNAL CMS PROVIDER                 |  |
|   | (existing services      |       |  Case Engine (lifecycle/number/events)|  |
|   |  relocated: onboarding  |       |  Scoring Engine --> Rule Engine       |  |
|   |  HTTP, Oracle reader,   |       |       |          \-> Algorithm Engine |  |
|   |  progress worker,       |       |  Assignment Engine --> CaseWF schemes |  |
|   |  callback handling)     |       |  Correlation Engine                   |  |
|   +-----------|-------------+       +-------------------|-------------------+  |
|               |                                         |                      |
|   Cross-cutting: Mapping module · Notification engine · Audit engine ·         |
|                  Configuration module + License gate · Reporting/Search/Dash   |
+---------------|-----------------------------------------|----------------------+
                v                                         v
        [ Oracle FCCM ]                    [ FSI Workflow Engine (separate plan) ]
                                                          |
   [ BRAINS ] <---- reconciliation (both modes,           v
                    when enabled — §10.3)      [ SQL Server: dbo + CMS schema ]
```

**Module responsibilities**: Case Engine (case CRUD, numbering `CMS.Case_Sequence`, events, status sync); Rule Engine (risk rules + accelerated re-review rules, safe-expression predicates); Algorithm Engine (parameter resolution via mapping profiles + weighted aggregation); Scoring Engine (orchestrates both stages, banding, override, recommendation, **scorecard persistence**); Assignment Engine (CaseWF glue: pools, claim, queue routing, SLA watch); Correlation Engine (§12); Notification Engine (observer on case events → Slice-8 `IDlpNotificationClient` when it lands; template + recipient rules in `CMS.Notification_Rule`); Audit Engine (case audit rows, rule/workflow execution logs, API-call log hooks); Mapping Module (§13); Configuration Module (options, validation, license gate); Reporting/Search/Dashboard (§14).

**Code layout** (per superseded module plan, schema renamed `CMS`): `Domain/Entities/CaseManagement/`, `Application/{Contracts,Features}/CaseManagement/`, `Infrastructure/CaseManagement/{Persistence,Providers/{Fccm,Tmx},Engines,Licensing}`, `API/Controllers/CaseManagement/`, `database/migrations/casemanagement/`, FE `src/features/cases/`. Coupling rules: value-key references only across the boundary, no cross-slice handler imports, `CmsDbContext` maps only `[CMS].*`, single `AddCaseManagementModule()` registration.

---

<a id="s5"></a>
## 5. Provider abstraction

`ICaseManagementProvider` port (SubmitKycCase / SubmitTradeCase (owns CA-NOHIT) / GetProgress / GetRiskAssessment / ApplyExternalDecision / Retry / Repoll / Refresh) with two implementations:

- **`OracleFccmProvider`** — relocation of the proven `FccmKycCaseService`/`FccmTradeCaseService`/`FccmOracleReader`/poller; zero behaviour change; callback endpoint feeds `ApplyExternalDecisionAsync`.
- **`TmxCmsProvider`** — Scoring Engine + Case Engine + CaseWF; decisions arrive from CaseWF command post-hooks.

Shared, provider-agnostic: finalise hook + guards, `dbo.KycCaseRequest` ledger (stays in dbo — pre-existing shared history; `Case_Source` column added), generalised `CaseProgressWorker` (poller → provider-neutral), `RiskReconciler` (BRAINS reconciliation with `SourceLabel`), review scheduling, Risk Assessment tab, resubmit/ops endpoints, `ICaseDecisionApplier` (single implementation of transaction-status/remark/review-schedule effects — called by FCCM callback **and** CaseWF closures).

**Router**: `Mode` (binary: `OracleFccm` | `TmxCms`) picks the provider for **new** cases; **in-flight cases always continue on the provider that created them** (`Case_Source` keyed) — after a switch to TMX CMS, pre-switch FCCM cases keep polling/receiving callbacks until they drain; nothing is orphaned. Dual-write is structurally impossible (one provider per submission).

**Generic controller (per FSI review)**: the API surface is a single provider-agnostic **`CaseManagementController`** (`/api/v1/CaseManagement/*`, §15). Onboarding/trade hooks and the FE always call the generic routes; the decision to invoke external CMS (FCCM) or Internal happens **only inside the router from configuration** — no caller ever knows which provider ran.

---

<a id="s6"></a>
## 6. Configuration management — built on the existing DB-managed Configuration Management (observed 2026-08)

The application now has a first-class **Configuration Management system** (migrations `2026_07_036` → `2026_08_060`): DB-backed categories + typed settings (`Boolean/Integer/Decimal/String/Enum/Json/TimeSpan/Secret/LogModule`), the `/admin/configuration` screen (`features/configuration/`), per-setting maker-checker approval (`requiresApproval` → config change requests), history/rollback, sensitive-value reveal, `appliesOnRestart` flags, per-category **Verify chips** (`Verify_Kind` → `POST Configuration/Verify/FccmConnection|Url|OnboardingCall|ProviderTest` via `IConfigurationProbe`), and section sub-headers (`2026_08_058`). Case Management configuration lives **inside this system** — not in a parallel mechanism.

### 6.1 Category restructure (per FSI direction — generic, not vendor-labelled)

The current `'fccm' — "FCCM / Integration" — "Oracle FCCM case handoff"` category is **renamed/generalised to `case-management` — "Case Management"**, and becomes provider-driven:

| Section (within the category) | Settings | Visible when |
|---|---|---|
| **Provider** | `CaseManagement.Enabled` (Boolean) · **`CaseManagement.Provider`** (Enum: `OracleFccm` \| `TmxCms`; `requiresApproval=1`, `appliesOnRestart=1` per Decision 6) · `CaseManagement.Reconciliation.Enabled` (Boolean) | always |
| **Oracle FCCM** | The existing keys, relocated under this section unchanged: `Integration.FccmOracleConnection` (Secret), `Integration.FccmOnboardingUrl`, `Integration.TradeCaseDefaults.Jurisdiction/CaseType`, `Integration.PollingInterval`, `Integration.CaseIdTimeout`, `Integration.HttpTimeout`, `Integration.FccmActionCodeStatusMap` (Json), onboarding credentials/toggles | `Provider = OracleFccm` |
| **TMX CMS** | `Cms.CaseNumberPrefix` · `Cms.Correlation.Enabled` · `Cms.Correlation.EvaluateOnEventAttach` · `Cms.Scheme.KYC_ONBOARDING` / `Cms.Scheme.TBML_SURV` (Enum from deployed schemes) · `Cms.Sla.Enabled` · cache TTLs | `Provider = TmxCms` |

### 6.2 Conditional visibility mechanism

Additive column **`Provider_Scope`** on the config-settings table (`NULL` = always visible, `'Fccm'`, `'Internal'`): the bundle endpoint (`GET Configuration`) filters settings by the currently-selected `CaseManagement.Provider`, so the admin screen **shows only the relevant options for the selected provider** — the FE already renders the bundle dynamically, so no FE structural change beyond re-fetch-on-provider-save (settings list invalidation already exists). Switching the provider value immediately re-shapes the category; irrelevant settings are absent, not greyed. Backend consistency: the same scope drives which options classes get bound/validated (`CmsFccmOptions` vs `TmxCmsOptions`) — a handler can never read the inactive provider's settings.

### 6.3 Verify chips per provider

`Verify_Kind` becomes provider-aware for this category: `Provider=OracleFccm` surfaces the existing **Test FCCM connection** + **Test onboarding call** probes; `Provider=TmxCms` surfaces a new **`Verify/CmsReadiness`** probe (`IConfigurationProbe` impl: score map non-empty, Active risk model exists per jurisdiction/customer type, CaseWF schemes deployed, case-generation policy seeded) — the same fail-fast checks the startup validation runs, exposed as a one-click chip.

### 6.4 What stays in appsettings

Only bootstrap infrastructure that must exist before the DB is reachable (connection strings, log directory) — consistent with how the config system already splits responsibilities. Everything behavioural above is DB-managed with approval + history (AG4/AG6 evidence comes free from the existing history/rollback machinery).

**Migration**: `2026_MM_xxx_CaseManagementConfigCategory.sql` — idempotent: rename category row (`fccm` → `case-management`, title "Case Management"), add `Provider_Scope` column, tag existing FCCM keys `'Fccm'`, seed Provider/Enabled/Internal settings with sections + help text (following the `2026_08_053` help-text conventions).

Reference shape of the effective configuration (as bound into options after the DB bridge):

```jsonc
"CaseManagement": {
  "Enabled": true,
  "Provider": "Fccm",                          // "OracleFccm" | "TmxCms"  (binary — one provider active)
  "Reconciliation": { "Enabled": true },       // BRAINS reconciliation toggle — §10.3 defines the off-flow
  "License": { "FilePath": "license.lic" },
  "TmxCms": {
    "CaseNumberPrefix": "TMX-CA",
    "Correlation": { "Enabled": true, "EvaluateOnEventAttach": false },
    "Scheme":   { "KYC_ONBOARDING": "CaseWF_KYC", "TBML_SURV": "CaseWF_TBML" },   // workflow config
    "Sla":      { "Enabled": true },
    "Cache":    { "RiskModelSeconds": 300, "CorrelationRulesSeconds": 300, "MappingProfilesSeconds": 300 }  // cache strategy: config read-through w/ TTL + explicit invalidation on maker-checker approval
  },
  "Fccm":     { /* relocated Integration:Fccm* keys; alias kept during transition */ },
  "Mapping":  { "Directory": "Mappings/casemanagement", "HotReload": false },     // §13 profiles
  "Api":      { "CallbackSharedSecretEnforced": true }                            // closes today's gap
}
```

Behaviour matrix (validated at startup): `Enabled=false` → no case creation, flows unaffected, UI hidden, scorecards still generated if enabled; `Fccm` → Oracle conn + URL required (Production); `TmxCms` → non-empty score map + Active risk model + CaseWF schemes deployed; `Reconciliation.Enabled=true` additionally requires a working BRAINS config. Rule/threshold/mapping/correlation **content** is data (DB tables + JSON profiles) maintained through admin screens with maker-checker — *which* rules exist is never appsettings.

**Conditional configuration summary**: provider sections are mutually exclusive end-to-end — the admin screen shows only the selected provider's settings (`Provider_Scope` filter, §6.2), startup binds/validates only the active provider's options class, and DI registers only that class — UI and backend consistent by construction, with the existing config-management approval/history covering every change.

---

<a id="s7"></a>
## 7. Licensing — **de-prioritized (FSI review 2026-07-16)**

Deferred to a later phase: for now `CaseManagement:Enabled` + `Provider` are the only gates (module visibility, endpoint mapping, FE tile). The design below is retained so the deferral costs nothing — `ICaseManagementLicenseGate` ships as an interface with a `ConfigOnlyLicenseGate` default implementation (reads `Enabled`), and the signed-license implementation slots in later without touching consumers.

Rides the platform licensing design (`MODULARISATION_PLAN.md`) — no parallel mechanism. HMAC-SHA256-signed license file: `modules.CaseManagement = { enabled, features: ["FccmProvider","TmxCmsProvider","RiskScorecard","CorrelationEngine","SlaManagement","AuditPack", …future premium] }`. `CaseManagementLicenseGate` validates at startup; consulted by router (mode must be licensed), endpoint mapping (unlicensed ⇒ 404), FE bootstrap (module hidden via app-init payload). **Expiry = read-only** (view/export allowed; no new cases/decisions; external FCCM decisions still land — never a stuck compliance record). Signing key held by FSI release engineering; absent file = unlicensed, not crash.

---

<a id="s8"></a>
## 8. CMS database design

`CREATE SCHEMA [CMS]`; numbered idempotent SQL migrations under `database/migrations/casemanagement/` (project convention — not EF code-first); FKs only inside the schema; cross-boundary references by value (`Transaction_Id`, `Customer_Code`, `Process_Instance_Id`); own `ConnectionStrings:CMS` key (defaults to Default) → future DB split = restore schema + one config change. `dbo.KycCaseRequest` stays in dbo (shared pre-existing ledger; additive `Case_Source`).

### 8.1 Table catalogue (grouped)

| Group | Tables |
|---|---|
| Case core | `Case`, `Case_Sequence`, `Case_Event`, `Case_Party`, `Case_Note`, `Case_Attachment` (bridge to `TmX_Document`), `Case_Link`, `Case_Decision`, `Case_Status_History`, `Case_Audit` |
| **Structured data layer** (§8.5) | `Customer` (registry — one row per customer, updated on reassessment), `Case_Customer_Snapshot` (frozen facts at assessment/case time), `Trade_Detail`, `Trade_Party` (drawee/nego/beneficiary etc.), `Transaction_Map` (TMX transaction ↔ CMS trade rows) |
| Risk model (config) | `Risk_Model` (jurisdiction × customer_type × model_type × version, Draft/Active/Retired), `Risk_Parameter`, `Risk_Param_Weight`, `Risk_Param_Score_Map`, `Risk_Rule`, `Accelerated_ReReview_Rule`, `Risk_Band_Config`, `Risk_Recommendation_Config`, `Case_Generation_Policy` (§10.4) |
| Scoring (history) | `Risk_Scorecard` (immutable, versioned per customer), `Rule_Execution_Log`, `Algorithm_Execution_Log` |
| Correlation | `Correlation_Rule`, (links live in `Case_Link` w/ rule provenance) |
| Assignment/SLA | `Queue_Config`, `Sla_Config`, `Sla_Breach_Log` |
| Mapping (§13) | **`Field_Mapping`** (dynamic-field ↔ typed-column bridge for Customer + Trade, per product/tab/customer-type — §8.5), `Mapping_Profile` (JSON profiles for API/status/risk/workflow/queue/case-type kinds) |
| Notifications | `Notification_Rule`, `Notification_Log` |
| Ops | `Api_Call_Log` (external integration calls incl. FCCM exchanges — supplements existing per-row payload logs), `Workflow_Execution_Log` (case-scoped projection of engine history for the audit pack) |

### 8.2 ERD (core relationships)

```text
                         RISK CONFIG (per model version)                CASE CORE
 +----------------+   1..n  +-------------------+
 | RISK_MODEL     |-------->| RISK_PARAM_WEIGHT |--+          +-----------------+
 | (juris x ctype |   1..n  +-------------------+  | n..1     |     CASE        |
 |  x mtype x ver)|-------->| RISK_RULE         |  +------+   |  1 --> n:       |
 |                |   1..n  +-------------------+         |   |   CASE_EVENT    |
 |                |-------->| RISK_BAND_CONFIG  |         v   |   CASE_NOTE     |
 +-------+--------+         +-------------------+  +----------+-+ CASE_ATTACH.  |
         ^                                         |RISK_       | CASE_DECISION |
         | n..1                                    |PARAMETER   | CASE_STATUS_H.|
 +-------+--------+  explains  +---------------+   +-----+------+ CASE_AUDIT    |
 | RISK_SCORECARD |----------->| RULE_EXEC_LOG |         | 1..n | CASE_PARTY    |
 | (per customer, |----------->| ALGO_EXEC_LOG |         v      | CASE_LINK ----+--> CORRELATION_RULE
 |  versioned)    |            +---------------+   +------------+-+  (nullable = manual link)
 +-------+--------+                                |RISK_PARAM_   |
         ^                                         |SCORE_MAP     |    SLA_CONFIG --> SLA_BREACH_LOG
         | cited by KYC case (Scorecard_Id)        |("ref sheet") |    NOTIFICATION_RULE --> NOTIFICATION_LOG
         +--- CASE                                 +--------------+
 Value-key refs (no cross-schema FK): CASE.Transaction_Id -> dbo.TmX_Transaction
   CASE.Process_Instance_Id -> dbo.WorkflowProcessInstance ·  ledger -> dbo.KycCaseRequest
```

### 8.3 Table structures (column level — core set)

**Case core** *(clarification per FSI review: for TBML, `Case_Event` rows ARE the raised red flags — one event per ticked/auto-raised flag carrying its matched value + proof link, plus one customer-details event, exactly mirroring today's `EVENT_STAGING` semantics; for KYC the case cites the scorecard instead of flag events)*:

```text
CMS.Case
  Case_Id            BIGINT IDENTITY PK
  Case_Number        NVARCHAR(20)  UNIQUE            -- 'TMX-CA' + Case_Sequence
  Case_Type          NVARCHAR(20)                    -- KYC_ONBOARDING | TBML_SURV
  -- (Case_Source REMOVED per FSI review: CMS.Case rows exist only when Internal is the
  --  provider, so the column carried no information here. Era lineage lives where both
  --  providers actually write — dbo.KycCaseRequest.Case_Source — which is what the
  --  mixed-era Risk Assessment history and drain-monitoring read.)
  Transaction_Id     BIGINT        (idx, value key)
  Customer_Id        BIGINT        FK -> CMS.Customer (registry, §8.5)
  Customer_Code      NVARCHAR(50)  (idx, denormalised for search)
  Scorecard_Id       BIGINT NULL   FK->Risk_Scorecard (KYC cases)
  Priority           NVARCHAR(10)                    -- from reconciled band
  Status_Lkp         INT                             -- CASE_STATUS lookup
  Tier               NVARCHAR(10) NULL               -- L1|L2|L3|AUTH1..3 (current queue)
  Assignee_User_Id   NVARCHAR(50) NULL
  Process_Instance_Id UNIQUEIDENTIFIER NULL (value key -> workflow engine)
  Jurisdiction_Cd    NVARCHAR(4)
  Opened_At/By, Closed_At/By, Disposition NVARCHAR(30) NULL, Row audit cols

CMS.Case_Event                                        -- = raised flags (TBML) + customer event
  Event_Id BIGINT PK · Case_Id FK · Event_Type (Flag|Customer)
  Flag_Scope_Id INT NULL (value key -> dbo flag catalogue) · Description NVARCHAR(500)
  Matched_Value NVARCHAR(400) NULL · Origin (Auto|Manual) · Severity · Proof_Ref NVARCHAR(200) NULL
  Created_At/By

CMS.Case_Decision
  Decision_Id PK · Case_Id FK · Decision_Type (Recommend|Approve|Reject|SendBack|Escalate)
  Disposition NVARCHAR(30) NULL · Remarks NVARCHAR(1000) · Actor_User_Id · Actor_Role · Decided_At

CMS.Case_Link
  Link_Id PK · From_Case_Id FK · To_Case_Id FK · Link_Type (related|duplicate|reopens|merged_into)
  Correlation_Rule_Id INT NULL FK (NULL = manual) · Matched_Context_Json · Reason · Created_At/By
```

**Risk model config — answering the rule-vs-algorithm structure question directly**: rule-based and algorithm-based scoring are **separate tables** (their shapes differ: a rule is predicate→outcome; an algorithm entry is parameter→weight). They join only through `Model_Id`, and both write into the same scorecard. The **weightage** and the **reference list ("sheet") are two different tables linked through the parameter**:

```text
CMS.Risk_Model                                        -- model header (versioned)
  Model_Id INT PK · Jurisdiction_Cd · Customer_Type (IND|LE|FI) · Model_Type (RealTime|Batch)
  Version INT · Status (Draft|Active|Retired) · Supersession_Policy (HigherWins)
  Effective_From · Approved_By/At · Created_By/At          (one Active per juris x ctype x mtype)

CMS.Risk_Parameter                                    -- reusable parameter library (model-independent)
  Param_Code NVARCHAR(50) PK                          -- e.g. 'CountryOfResidence'
  Label · Data_Type · Source_Binding NVARCHAR(200)    -- mapping-profile key (where the value comes from)
  Lookup_Type NVARCHAR(50) NULL                       -- e.g. 'Country' -> TmX_Lookup: admin picks values
  Multi_Value_Strategy (MaxScore|Sum|Any) · Active_Flag

CMS.Risk_Param_Weight                                 -- THE WEIGHTAGE (algorithm-based, per model)
  Model_Id INT FK -> Risk_Model                       -- e.g. Model 7:
  Param_Code NVARCHAR(50) FK -> Risk_Parameter        --   'CountryOfResidence'
  Weight DECIMAL(7,2)                                 --   10
  Comments NVARCHAR(200) · PK (Model_Id, Param_Code)

CMS.Risk_Param_Score_Map                              -- THE REFERENCE SHEET (value -> score)
  Score_Map_Id BIGINT PK
  Param_Code NVARCHAR(50) FK -> Risk_Parameter        -- 'CountryOfResidence'
  Model_Id INT NULL FK                                -- NULL = global default sheet, reusable by
  Code_Value NVARCHAR(100)                            --        every model; non-NULL = model override
  Score DECIMAL(5,2)                                  -- 'AF' = 10 · 'PK' = 8 · 'AE' = 6 ...
  Active_Flag · audit cols · UQ (Param_Code, ISNULL(Model_Id,0), Code_Value)

CMS.Risk_Rule                                         -- RULE-BASED scoring (separate shape)
  Rule_Id INT PK · Model_Id FK · Rule_No INT (priority) · Rule_Name
  Predicate_Json NVARCHAR(MAX)                        -- safe-expression on parameters
  Outcome (Force_Band|Force_Score|Not_Recommended|Accelerate_ReReview)
  Outcome_Value NVARCHAR(30) · Stop_On_Match BIT · Reason_Template · Active_Flag · audit cols

CMS.Risk_Band_Config                 -- THE LINK between risk score and risk category:
  Band_Config_Id PK · Model_Id FK    --   configurable, per model version
  Band_Key (1|2|3) · Label (Low|Medium|High)
  Score_From DECIMAL(5,2)            -- INCLUSIVE lower bound
  Score_To   DECIMAL(5,2)            -- EXCLUSIVE upper bound (highest band: inclusive)
  audit cols
  -- Seeded example:  LOW [0.00–40.00) · MEDIUM [40.00–70.00) · HIGH [70.00–100.00]
  -- Boundary rule pinned to avoid the 40-in-both-bands ambiguity: a score of exactly
  --   40.00 is MEDIUM, 70.00 is HIGH. Validator enforces: bands contiguous, non-
  --   overlapping, gap-free, covering 0–100, exactly one band per score.
```

The score→category link is maintained at **both** ends: config-time — this table defines the mapping, admin-edited (Thresholds screen, §9), maker-checker'd and versioned with the model; assessment-time — every scorecard stores the continuous `Effective_Score` **and** the derived `Effective_Band` **and** the `Model_Id + Model_Version` used, so the exact range table that produced any historical categorization is reproducible (AG8), and re-banding after a threshold change never rewrites history — new ranges apply only to new assessments.

**Linkage worked example (Country of Residence)**: `Risk_Parameter('CountryOfResidence', Lookup_Type='Country')` ← `Risk_Param_Weight(Model 7, 'CountryOfResidence', Weight=10)` ← `Risk_Param_Score_Map('CountryOfResidence', NULL, 'AF', 10 / 'PK', 8 / …)`. At runtime the Algorithm Engine resolves the customer's value (`AF`) via the parameter's `Source_Binding`, looks up its score on the reference sheet (model override first, else global sheet) → `10`, multiplies by the model weight → contribution `10 × 10`, aggregates `Σ(wᵢsᵢ)/Σwᵢ`, and records `value/score/weight/contribution` in `Algorithm_Execution_Log` + the scorecard's `Param_Scores_Json`. `Code_Value` entries are picked from the parameter's configured `TmX_Lookup` type in the admin screen — never free-typed, never hardcoded.

```text
CMS.Risk_Scorecard (immutable; 1 customer -> many rows)
  Scorecard_Id BIGINT PK · Customer_Id FK -> CMS.Customer · Version INT (per customer)
  Trigger (Onboarding|PeriodicReview|Refresh|Override) · Transaction_Id · Request_Id
  Provider_Source (FCCM|TMX) · Model_Id · Model_Version · Jurisdiction_Cd · Customer_Type
  Algorithm_Score DECIMAL(5,2) · Algorithm_Band
  Rule_Based_Score DECIMAL(5,2) NULL · Rule_Based_Band NULL
  Superseded_By_Rule BIT · Effective_Score DECIMAL(5,2) · Effective_Band (=Risk_Category_Key)
  Override_Score NULL · Override_By/At/Reason
  Reconciled_Band · Reconciliation_Status (Done|Disabled|Pending)
  -- (BRAINS band REMOVED per FSI review: the comparison source already lives in
  --  Cstm_Brains (FCCM_Score/Brains_Score/Final_Score/Display per transaction);
  --  the scorecard stores only the reconciliation OUTCOME it owns.)
  Case_Generated BIT · Policy_Verdict_Json (which §10.4 criteria evaluated/triggered)
  Rcmnd_To_Onbrd · Inputs_Json · Generated_At · Generated_By
  UQ (Customer_Id, Version) · idx (Customer_Id, Version DESC)

CMS.Risk_Scorecard_Detail            -- QUERYABLE per-component breakdown (per FSI review):
  Detail_Id BIGINT PK                -- the audit answer to "how was 40 calculated?"
  Scorecard_Id FK -> Risk_Scorecard  -- e.g. for final 40 on 2026-07-16:
  Component_Type (AlgorithmParameter | Rule | Override | Reconciliation)
  Component_Code NVARCHAR(50)        --   'CountryOfResidence'   'RULE_HSCODE'   ...
  Component_Label NVARCHAR(200)      --   'Country of Residence' 'HS Code Rule'
  Raw_Value NVARCHAR(200)            --   'AF'                   '2844.20'
  Score DECIMAL(5,2)                 --   10                     10
  Weight DECIMAL(7,2) NULL           --   10 (algorithm rows only)
  Contribution DECIMAL(7,2)          --   20                     10
  Sequence INT · Created_At          -- rows sum/compose to Effective_Score, timestamped
```
The JSON blobs (`Inputs_Json`, rule-hit detail) remain for full-fidelity replay, but the **detail rows are the reportable breakdown** — `SELECT` produces exactly the "Algorithm = 20, Country Rule = 10, HS Code Rule = 10 → Final 40, generated 2026-07-16" justification without JSON parsing.

Indexing: queue composite `(Status_Lkp, Tier, Priority, Opened_At)`, `Customer_Code`, `Transaction_Id`; remaining tables (notes, attachments, status history, audit, SLA, notifications, mapping registry, execution logs) follow the same column conventions — full DDL lands in the numbered migrations at M0/M1.

### 8.5 Structured data layer (per FSI review — Oracle-style typed columns for rules, reporting, audit)

**Why**: TMX holds customer data only as **per-transaction snapshots** (`TmX_Customer_Master` is misnamed) and transaction facts inside **UDF JSON**. Oracle CMS executes rules against structured customer/transaction columns — and the Internal CMS should too: deterministic indexed rule execution (no JSON parsing per evaluation), straightforward reporting/search, and frozen auditable facts. The known drawback — duplication/sync — is contained because every row is written at a **single controlled event** and is then immutable (snapshots) or updated only by the next controlled event (registry). Population is always through **mapping profiles** (§13 Entity/Field kinds — UDF path → typed column), never hardcoded.

```text
CMS.Customer                          -- REGISTRY: one row per customer (FSI lifecycle note:
  Customer_Id BIGINT PK               --  onboarding CREATES it; every reassessment UPDATES
  Customer_Code NVARCHAR(50) UNIQUE   --  the SAME row — never a duplicate customer)
  Customer_Name · Customer_Type (IND|LE|FI) · Jurisdiction_Cd · Country_Of_Residence
  Country_Of_Incorporation · Industry · Legal_Structure · National_Id · Tax_Id
  Current_Risk_Band · Current_Scorecard_Id FK · Onboarded_At · Last_Reassessed_At
  Next_Review_Date · Status (Active|Exited) · audit cols

CMS.Case_Customer_Snapshot            -- frozen customer facts AS-AT assessment/case creation
  Snapshot_Id PK · Case_Id FK NULL · Scorecard_Id FK NULL · Customer_Id FK
  (same fact columns as registry) · Captured_At

CMS.Trade_Detail                      -- typed trade facts per TBML case (Oracle LC_TRN_DET
  Trade_Detail_Id PK · Case_Id FK     --  parity, internal + typed): populated at case
  Transaction_Id (value key)          --  creation from UDF via mapping profile
  Instrument_Type · LC_Reference · Amount DECIMAL(18,2) · Currency · Tenor_Days
  Drawee_Name/Bank · Negotiating_Bank · Issuing_Bank · Advising_Bank
  Port_Of_Loading · Port_Of_Discharge · Country_Of_Origin · Shipment_Mode
  Goods_Description · Incoterm · Payment_Term · Value_Date · audit cols

CMS.Trade_Party                       -- one row per party (drawee, beneficiary, applicant,
  Party_Id PK · Trade_Detail_Id FK    --  negotiating/issuing/advising bank, agent ...)
  Party_Role · Name · Country · Bank_Code NULL · Account NULL · Identifier NULL

CMS.Trade_Goods                       -- line items (HS code, qty, value) for goods rules
  Goods_Id PK · Trade_Detail_Id FK · HS_Code · Description · Quantity · Unit · Line_Amount

CMS.Transaction_Map                   -- explicit TMX ↔ CMS mapping (FSI ask): which TMX
  Map_Id PK · Transaction_Id          --  transaction produced which CMS trade rows / case /
  Trade_Detail_Id NULL · Case_Id NULL --  scorecard — the join spine for rule execution,
  Scorecard_Id NULL · Mapped_At       --  reporting and reconciliation across the boundary
```

**Field mapping table (per FSI clarification — the bridge between TMX dynamic fields and CMS typed columns, both directions, for Trade AND Customer):**

TMX stores facts in dynamic UDF fields whose names differ per product/sub-product (`ILF_PortLoadingCtry` vs `ICR_PortLoadingCtry`) and per KYC section (`KycParam34`, grids like `TmxKYCSPOR`). The typed CMS columns are populated through an **admin-managed mapping table**, not code:

```text
CMS.Field_Mapping                      -- one row per (target column ← source dynamic field)
  Mapping_Id INT PK
  Target_Entity  (Customer | Case_Customer_Snapshot | Trade_Detail | Trade_Party | Trade_Goods)
  Target_Column  NVARCHAR(60)          -- 'Port_Of_Loading' · 'Country_Of_Residence' · 'Drawee_Name'
  Product_Id INT NULL                  -- scope: NULL = all products
  Tab_Id INT NULL                      -- sub-product scope (UDF names differ per sub-product)
  Customer_Type NVARCHAR(4) NULL       -- Customer mappings can differ per IND | LE | FI
  Source_Kind    (udf.detail | udf.customer | udf.grid | snapshot.column | transaction.column | context)
  Source_Path    NVARCHAR(200)         -- 'ILF_PortLoadingCtry' · 'KycParam34' · 'TmxHsCodeList[]'
  Transform      NVARCHAR(200) NULL    -- lookupHidden('Country') | cast(decimal) | dateFormat | valueMap
  Grid_Strategy  NVARCHAR(20) NULL     -- First | MaxScore | AllRows (grid -> Trade_Goods line items)
  Is_Required BIT · Active_Flag · Version + maker-checker audit cols
  UQ (Target_Entity, Target_Column, ISNULL(Product_Id,0), ISNULL(Tab_Id,0), ISNULL(Customer_Type,''))
```

- **TMX → CMS (populate)**: at each controlled event (onboarding/reassessment → `Customer` + snapshot; case creation → `Trade_Detail`/`Trade_Party`/`Trade_Goods`) the `EntityMaterializer` selects the mapping rows for the transaction's (product, tab) / customer type, resolves each `Source_Path` from the UDF/snapshot, applies the transform, and writes the typed column. `AllRows` grid strategy fans a repeating grid into `Trade_Goods` line items.
- **CMS → TMX (vice versa / traceability)**: the same rows answer the reverse question — for any typed column value, the UI/audit can show *which dynamic field it came from* (drill-back tooltip on case/customer screens, and the provenance is recorded per materialization via `Transaction_Map` + the mapping version used). Physical write-back into TMX UDF is deliberately **not** performed (single-direction data flow keeps the transaction the source of truth); if a future flow needs it, the same table already carries the correspondence.
- **Admin surface**: the "Mappings" configuration screen (§16) edits these rows with maker-checker; seeded defaults are generated from the existing onboarding mapping profiles + the `productSummary` field back-trace already in the BE. Mapping changes are versioned (AG4) and apply to **new** materializations only — existing snapshots stay frozen as captured.
- Same mechanism serves **Customer** (KYC Section-A `KycParam*`/grids → registry columns, per customer type) and **Trade** (per sub-product UDF codes → trade columns) — one table, one materializer, no hardcoded field names anywhere.

Rule/correlation/case-generation engines evaluate against these typed tables first (registry + snapshot + trade rows), falling back to the raw UDF resolver only for fields a bank hasn't mapped to columns. **Customer lifecycle (FSI note)**: onboarding creates the `CMS.Customer` row; each periodic review/reassessment updates the same row and appends a new scorecard version (never a duplicate customer); when the §10.4 policy fires, the case is created **against that onboarding/assessment** (`Case.Customer_Id` + `Case.Scorecard_Id`), with the as-at snapshot preserving what the facts were at decision time.

### 8.4 Migration planning

`2026_MM_001_CreateCmsSchema` → `..._002_CaseCore` → `..._003_RiskModelConfig` → `..._004_ScorecardAndLogs` → `..._005_CorrelationSlaMappingNotifications` → `..._006_SeedLookupsPrivilegesStatusVocabulary` → `..._007_SeedDefaultCorrelationRule_SlaDefaults_CaseGenerationPolicy` → data import scripts (score map from Oracle §10.5, historical scorecard backfill from `KycCaseRequest`+`Cstm_Brains` optional). All idempotent, slice-tagged, re-runnable; rollback = schema is additive (nothing in dbo altered except `KycCaseRequest.Case_Source`).

---

<a id="s9"></a>
## 9. Rule & algorithm management (admin screens — Oracle-parity builders)

Admin module (privileges `RiskConfig.Manage/Override`, all writes maker-checker via `TmX_Request`, every change versioned + audited — matching Oracle's Save/Copy/**History** observed live):

- **Rule Builder**: priority-ordered risk rules; predicate composer on the safe-expression grammar (parameter, operator, value/list/lookup-set); outcome (Force_Band / Force_Score / Not_Recommended / Accelerate_ReReview); stop-on-match; per model version. Test panel = dry-run against a sample customer payload (mirrors Auto-Flags `Test`).
- **Algorithm Builder**: per model (jurisdiction × customer type × model type): parameter list with **weights** (Oracle grid parity: Parameter | Weight | Comments), add/remove parameters from the registry, normalisation preview (Σ weights), copy-model (Oracle "Copy") for new versions.
- **Parameter Management**: registry (code, label, source binding via mapping profile key, data type, multi-value strategy MaxScore/Sum/Any).
- **Weightage Management**: inside Algorithm Builder; jurisdiction/customer-type scoped.
- **Threshold Management — the band-range tuning screen** (Oracle "Risk Assessment Category" parity): an editable grid per model — one row per category with `Score_From` / `Score_To` inputs (LOW 0–40 · MEDIUM 40–70 · HIGH 70–100 seeded) plus a visual range bar. Live validation as the admin types: contiguous, non-overlapping, gap-free, 0–100 coverage, boundary convention (from-inclusive/to-exclusive) shown inline; adjacent bounds auto-snap (raising LOW's `To` to 45 moves MEDIUM's `From` to 45). Changes are drafted against a model version and go through maker-checker publish — tuning ranges never re-bands historical scorecards (new assessments only). An impact preview shows how the last N assessments *would* re-band under the drafted ranges before publishing (decision-support only). Also here: recommendation thresholds (`Rcmnd_To_Onbrd` per band).
- **Lookup Management — the Country-Risk example**: `Country Risk Weight = 10` is expressed as parameter `CountryOfResidence` (weight 10 in the model) whose **value scores come from the configurable score-map/lookup** (`Risk_Param_Score_Map`: `AF=90, DZ=75, …`) — sourced from `TmX_Lookup`-backed pick lists, never hardcoded. Reusable parameter library: the same parameter + score map is referenced by multiple models and by rule predicates (single definition, dynamic execution).

Dynamic execution: engines read Active model config at run time (TTL cache + invalidation on approval) — new rules/weights/thresholds require **no deployment**.

---

<a id="s10"></a>
## 10. Scoring engine & Customer Risk Scorecard

### 10.1 Model dimensions (updated from the live instance)

`Risk_Model` is scoped by **Jurisdiction × Customer Type (Individual | Legal Entity | Financial Institution) × Model Type (RealTimeOnboarding | Batch) × Version** — the Customer Type dimension is a live-instance finding now added to the design. One Active model per (jurisdiction, customer type, model type).

### 10.2 Two-stage assessment — both stages ALWAYS compute

1. Resolve parameter values (mapping profiles — same context as the FCCM payload build). 2. **Algorithm pass**: score map per value (multi-value → MaxScore, per parameter) → weighted aggregate `decimal(5,2)` → algorithm band. 3. **Rule pass**: all rules in priority order → rule-based band/score + fired **terms**. 4. **Supersession**: effective = higher-risk of the two (`Supersession_Policy=HigherWins`, per model) — when a rule supersedes/accelerates, `Superseded_By_Rule` + terms answer *"on what terms did it get the high score."* 5. Band via ranges → recommendation → **persist scorecard**. Overrides (privileged) create a new scorecard version, original retained.

### 10.3 Reconciliation (both modes, toggleable)

**When `Reconciliation.Enabled=true`** (default): effective band reconciles vs BRAINS — PEP(0) always wins; higher wins; tie → provider value w/ equal-suffix. `SourceLabel` = "TBML" (Fccm) / "TMX" (Internal). Writes `Cstm_Brains` + snapshot UDF — unchanged consumers.

**When `Reconciliation.Enabled=false` (per FSI review — the no-second-risk flow)**: there is nothing to compare, so the pipeline **skips the reconciliation step entirely** and the provider's own band becomes final directly:
- Final band = `Effective_Band` from the scoring stage (Internal) or `RISK_CATEGORY_KEY` (Fccm) — no suffix, `Prevails = "N/A (single source)"`.
- Scorecard row: `Brains_Band = NULL`, `Reconciled_Band = Effective_Band`, `Reconciliation_Status = 'Disabled'`.
- `Cstm_Brains` upsert: `Brains_Score = NULL`, `Final_Score = provider band`, `Display` without source suffix — downstream readers (snapshot UDF `$.RiskScore`, dashboards) keep working because the written shape is identical.
- UI: the Risk panel/scorecard hides the BRAINS comparison column and shows a "single-source assessment" tag.
- **Degraded case (enabled but BRAINS unreachable)** is distinct from disabled: the assessment proceeds the same way but the scorecard is stamped `Reconciliation_Status = 'Pending'`, and the existing `Refresh` operation re-attempts reconciliation later (audit-safe: no assessment is ever blocked by BRAINS downtime).

### 10.4 Assessment-to-case generation policy (FCCM-parity, per FSI review)

FCCM keeps **KYC assessments and cases as separate surfaces**: every onboarding/review produces an assessment record, but an investigation **case is generated only when the assessment lands high enough**, appearing in Case Management under the KYC category. The internal CMS adopts exactly this split:

- **Assessment always** — every onboarding/periodic review/refresh writes a scorecard (§10.5) and appears on the **Onboarding / KYC Assessments screen** (list + drill-in), regardless of outcome.
- **Case conditionally — criteria are configuration, never code (FSI-confirmed).** Admins enable/disable and combine trigger criteria per case type through the policy table:

```text
CMS.Case_Generation_Policy
  Policy_Id INT PK · Case_Type (KYC_ONBOARDING | TBML_SURV) · Policy_No INT (evaluation order)
  Trigger_Kind  NVARCHAR(30)     -- Band_At_Least | Score_At_Least | PEP | Not_Recommended
                                 -- | Rule_Hit(any/named) | Flag_Count_At_Least
                                 -- | Flag_Severity_At_Least | Flag_Code_In(list)
                                 -- | Expression (safe-expression over assessment+flag context)
                                 -- | Always | Never
  Trigger_Value NVARCHAR(200)    -- e.g. 'High' · '75.00' · '3' · 'High' · 'RF13,RF43'
  Combine       (Or | And_With_Next)   -- rows OR by default; And-groups for compound criteria
  Effect        (Create_Case)    -- + optional Priority_Override / Queue_Override
  Active_Flag · audit cols       -- maker-checker via TmX_Request, changes versioned (AG4)
```

  Examples of what an admin can turn on without any deployment: **band-wise registration** (`Band_At_Least='High'`), **flag-volume based** (`Flag_Count_At_Least='3'`), severity-based (`Flag_Severity_At_Least='High'`), specific-flag based (`Flag_Code_In`), continuous-score threshold (`Score_At_Least='75'`), PEP/not-recommended, or compound (`Band_At_Least='Medium'` **And** `Flag_Count_At_Least='2'`). Seeded defaults preserve current behaviour: KYC → `Band_At_Least='High' OR PEP OR Not_Recommended OR Rule_Hit(accelerating)`; TBML → `Flag_Count_At_Least='1'` (exact parity with today's `redflagsCount > 0`, so zero flags still yields CA-NOHIT). `Always` reproduces every-onboarding-gets-a-case; `Never` (with the module on) gives assessments/scorecards without cases.
- **No-case path** — the scorecard records `Case_Generated = 0` + the policy verdict (which criteria evaluated, none triggered — auditable per AG2); the KYC transaction still completes through its own analyst/authorizer maker-checker workflow (the human gate that already exists), and `ICaseDecisionApplier` applies the outcome with the transaction's authorizer as actor. Low/Medium customers therefore don't create investigator noise, but every assessment remains auditable and comparable.
- **TBML runs through the same policy engine** — its seeded `Flag_Count_At_Least='1'` row is just the first policy; banks can raise the bar (e.g. ≥3 flags or ≥1 High-severity flag) or add compound criteria without touching the CA-NOHIT semantics (not-triggered ⇒ CA-NOHIT branch exactly as today).
- The Case Management queue shows category (KYC | TBML) exactly as FCCM does; the assessment screen links to its generated case when one exists.

### 10.5 Customer Risk Scorecard (1 customer → many scorecards)

`CMS.Risk_Scorecard` — immutable, per-customer `Version` increasing, one row per assessment event (**Onboarding | PeriodicReview | Refresh | Override**): identifiers (customer, transaction, ledger ref, provider source, model id+version, jurisdiction, customer type) · `Algorithm_Score/Band` · `Rule_Based_Score/Band` + `Rule_Hits_Json` (rule, predicate, matched values, reason) · `Superseded_By_Rule` · `Effective_Score/Band` · override (score/by/at/reason) · `Rcmnd_To_Onbrd` · `Brains_Band`, `Reconciled_Band`, `Prevails` · `Inputs_Json`, `Param_Scores_Json` (value/score/weight/contribution per parameter) · timestamps. **Fccm-mode rows are also written** (from `FCC_OB_RA.RISK_SCORE` + `RISK_CATEGORY_KEY`; factor JSON null) so history is continuous across providers/eras.

**Comparison**: `GET …/Customers/{code}/Scorecards` (list) + `/Compare?from=&to=` (default latest-vs-previous): overall Δ, band change, per-factor Δ sorted by |Δ| ("Country of Residence 45→90, +18.0 contribution — drove Low→High"), rule outcomes NEW/CLEARED, model-version change note. UI: scorecard panel + trend on Case detail (KYC) and Customer Insight; comparison view highlights movers.

### 10.6 Calibration & import (updated by live finding)

The admin-UI grids are empty in this environment (§1) ⇒ **export the model from the FCCM database**: `FCC_OB_RISK_PRMS_CODE_SCRE_MAP` (score maps — proven populated), the weights/parameter tables behind the Algorithm screens, `Risk Assessment Category` ranges, rule/accelerated-rule tables (DBA-assisted export at Phase 0). Fallback: reverse-calibration from historical `KycCaseRequest` pairs. Shadow scoring in Fccm mode (compute-and-log TMX result beside every live FCCM result) produces the sign-off evidence; compliance approves the Active model before TMX CMS go-live.

---

<a id="s11"></a>
## 11. Workflow design (CaseWF on the FSI workflow engine)

Configurable **default** schemes (Decision 7) built from proven idioms (`FillAllUsersBucket` pools, `AssignToMe` claim-narrowing, `CurrentActivityExecuted` auto-hops, actor restrictions, `Command_Application_Status_Mapping` status sync):

- **Tiers**: Analyst L1→L2→L3, each pool→claim→assigned; commands `AssignToMe / ReturnToPool / EscalateTo(L2|L3) / SendBackTo(L1|L2) / RecommendClosure(disposition, remarks)`.
- **Authorizers**: Authorizer L1→L2→L3 pools. Routing rule (queue config): recommendation lands at Authorizer L1; **escalation by case priority/amount thresholds or SLA** moves it up (`EscalateToAuth2/3`); commands `ApproveClosure / RejectRecommendation / EscalateToAuth(2|3)`.
- **Parallel approval**: for configured case classes (e.g. High-priority TBML `Confirmed`), the authorizer stage uses the ported **`FillApprovers` + `Approve` + `IsApproveComplete`** actions/conditions — N named approvers must all approve before the auto-transition finalises (same mechanism the legacy schemes used).
- **Dual control + segregation**: recommender ≠ approver ≠ anyone who worked the case (post-hook guard vs transition history); no self-approval even with multiple roles.
- **SLA breach**: auto-escalation transitions guarded by `AppDelay`/`AppDelayWithWF` (thresholds from `Sla_Config`); breach logged to `Sla_Breach_Log` + notification rule.
- **Assignment**: auto (pool by role/queue config + claim) and manual (privileged `Reassign` command → specific identity, audited).
- **Return for review**: `RejectRecommendation` / `SendBackTo*` with mandatory remarks.
- All of it is **scheme data** editable in the designer; per-case-type scheme mapping (`Internal:Scheme`); banks add/remove tiers without code. RBAC: roles `Case Analyst L1-3`, `Case Authorizer L1-3`; privileges `Cases.View/Operate/Authorize`, `RiskConfig.Manage/Override`.

---

<a id="s12"></a>
## 12. Correlation engine (configurable)

`CMS.Correlation_Rule` (admin, maker-checker, priority-ordered): match context (customer, counterparty, national id, transaction ref, product/sub-product, HS family, flag codes, branch — exact/normalised), source/target case types (cross-type per rule), lookback days, closed-case inclusion, action (`Link related` / `Link duplicate` / `Attach_Events` to open case / `Flag_For_Review`), stop-on-match. Engine runs at case create (+optionally event attach). Every auto-link records rule + matched values. Seeded default: same-customer → `Link related`, all types, unlimited lookback. Manual link/unlink with reason always available.

---

<a id="s13"></a>
## 13. Mapping module — no hardcoded mappings

Generalises the proven onboarding mapping-profile mechanism (JSON profiles, `extends`, transforms, hot-reload) into a registry (`CMS.Mapping_Profile`: name, kind, version, body, active) covering:

| Mapping kind | Content | Replaces/parity |
|---|---|---|
| Entity + Field | **Realised as the `CMS.Field_Mapping` table (§8.5)** — dynamic UDF field ↔ typed CMS column (Customer, snapshots, Trade_Detail/Party/Goods), scoped per product/tab/customer-type, with transforms + grid strategies, bidirectional traceability | FCCM staging column maps; `onboarding.v1/v2/legacy` profiles seed the defaults |
| API | provider endpoint templates, auth style, header/format toggles | `IncludeCustomerName`-style env differences |
| Status | provider action codes ↔ TMX statuses (`FccmActionCodeStatusMap` → data) + CaseWF command ↔ case status | `Command_Application_Status_Mapping` idiom |
| Risk | external risk vocabularies ↔ band keys (FCCM 1/2/3, BRAINS 0-3) | hardcoded classifiers |
| Workflow | case type → scheme code | `TmX_Product.Workflow_Scheme_Code` idiom |
| Queue | case type/priority → tier/role queue | routing table |
| Case type | trigger context → case type | product-name branch in the hook |

Profiles are versioned, maker-checker-approved, cached w/ invalidation; unknown-key-tolerant readers.

---

<a id="s14"></a>
## 14. Notification, Reporting, Search, Dashboard

- **Notification Engine**: `Notification_Rule` (event: assigned/escalated/SLA-breach/recommended/decided → recipients by role/actor → template) + `Notification_Log`. Transport = Slice-8 `IDlpNotificationClient` (shared backlog item); until it lands, rules evaluate + log (fail-safe no-op parity with Bucket C).
- **Reporting**: case register, aging/SLA, disposition summary, scorecard movement — SPs + existing DotLiquid/PDF/Excel engine; regulatory extracts scoped after §21 A6 discovery.
- **Search**: queue filters + global search (case number, customer, transaction, status, type, date ranges) over indexed columns; spans FCCM-era + TMX-era records.
- **Dashboard**: Cases KPI strip (open, my pool, aging>SLA, awaiting authorization, closed/week) + widgets on the existing operations dashboard (cases by status/priority, SLA heat, scorecard band distribution).

---

<a id="s15"></a>
## 15. API design (standard envelope, URL versioning, privileges, FE ledger entries on ship)

**One generic, provider-agnostic controller (per FSI review): `CaseManagementController` at `/api/v1/CaseManagement`.** Callers (onboarding hook, trade hook, FE, review orchestrator) never address a provider — the router resolves Fccm vs Internal from configuration behind every route:

- `POST /CaseManagement/Onboarding` — KYC case submission (hook-called; router decides external CMS vs internal) · `POST /CaseManagement/Trade` — TBML submission (owns CA-NOHIT)
- `GET /CaseManagement/Requests/{requestId}` — progress/status (ledger) · `POST /CaseManagement/Requests/{requestId}/Retry|Repoll|Refresh` (`Kyc.Operate`)
- `GET /CaseManagement/Cases` (queue, Kendo filters) · `GET /CaseManagement/Cases/{caseNumber}` · `/Cases/{caseNumber}/Events|Notes|Attachments|Links|Audit|Decisions` · case commands via existing generic `PUT /Workflow/Process/{id}/Execute`
- `GET /CaseManagement/Customers/{code}/Scorecards` + `/Compare?from=&to=`
- Admin (maker-checker): `/CaseManagement/Admin/RiskModels(+Versions,Activate)`, `/RiskRules`, `/ScoreMaps`, `/BandConfig`, `/CorrelationRules`, `/SlaConfig`, `/MappingProfiles`, `/NotificationRules`
- `GET /CaseManagement/Provider/Status` (mode, license, health)
- Compatibility: existing `/Kyc/Case*` (submit/status/callback), `/Kyc/RiskAssessment/{txnId}` and `/Transaction/{id}/ResubmitToFccm` routes remain mounted and forward into the same handlers (FE migrates at its own pace via `FE_CHANGES_REQUIRED.md` entries); the FCCM callback keeps its URL forever (external contract), now shared-secret-enforced.

---

<a id="s16"></a>
## 16. UI planning (FE `src/features/cases/`)

**Design seed**: the approved look is the **"CMS Unified Compliance Workspace" prototype** at `D:\ICBC - Latest\New theme\index.html` (src in `New theme/src/`): module-switcher chip (`CMS | switch`), global search, Case queue with status tabs + SLA time-left bars + periodic re-review dates + saved views/export, and the fullscreen KYC case detail (KYC Onboarding / Re-review Comparison / Events / Notes / Parties tabs; actions incl. Close-as-cleared, **Close — File SAR/STR**, Escalate, Reassign, Request info, Add evidence; per-parameter old-vs-new score comparison). The designer-agent variations (UX brief) refine within this language.

**Module switcher (per FSI review)**: when `Provider = TmxCms` (and the license covers it), the post-login **module picker** gains a **"Case Management (CMS)"** module tile alongside Trade Management, and the in-app switcher chip toggles between them (prototype behaviour). When `Provider = Fccm` or the module is unlicensed/disabled, the tile is absent (app-init payload flag, same mechanism as privilege-gated nav). The Risk Assessment tab on transactions remains available in both modes.

**Navigation / menu structure (CMS module)** — expected menus (per FSI suggestion, aligned to the prototype shell + Oracle's KYC admin tree):

| Section | Menu | Contents | Privilege |
|---|---|---|---|
| — | **Dashboard** | Case KPIs, SLA heat, band distribution, my workload | `Cases.View` |
| CMS | **Case Management** | Queue (All / Assigned to me / Open / Under review / Escalated / Closed), category filter KYC \| TBML, saved views, export → case detail workspace | `Cases.View/Operate/Authorize` |
| CMS | **Onboarding (KYC Assessments)** | Every assessment/scorecard (list + drill-in): dual scores, terms, comparison vs previous, link to generated case when policy fired (§10.4) | `Cases.View` |
| CMS | **Scorecards** | Customer-centric view: 1→many scorecard history, trend, compare any two versions | `Cases.View` |
| Configuration | **Rule Management** | Risk rules (rule builder), accelerated re-review rules, case-generation policy | `RiskConfig.Manage` |
| Configuration | **Risk Scores & Weightage** | Algorithm models (parameters + weights per jurisdiction/customer type), score-map reference sheets (lookup-backed), **risk category mapping** (band ranges + recommendation thresholds) | `RiskConfig.Manage` |
| Configuration | **Correlation Rules** | Match-context rules, actions, lookbacks | `RiskConfig.Manage` |
| Configuration | **Workflow & Queues** | CaseWF scheme mapping per case type, queue routing | `Workflow.Manage` |
| Configuration | **SLA Management** *(own screen — FSI confirmed)* | Case SLA thresholds per case type/priority/tier, aging states, auto-escalation bindings, breach log view. Note: a transaction-level `'sla'` config category already exists (`2026_07_045`: scanner on/off, default window, at-risk %) — the CMS SLA screen governs **case** SLAs (`CMS.Sla_Config`) and coexists with it; the global scanner settings stay where they are | `RiskConfig.Manage` |
| Configuration | **Mappings** | Entity/field/API/status/risk/queue/case-type mapping profiles | `RiskConfig.Manage` |
| Configuration | **Notifications** | Notification rules + log | `RiskConfig.Manage` |
| Insights | **Reports** | Case register, aging/SLA, dispositions, scorecard movement | `Reports.View` |
| Insights | **Audit trail** | Case/audit search + audit-pack export | `Cases.View` |

(Trade module navigation is untouched; User/Role/Tab-visibility/Field-config/Manual-red-flags stay in the existing Administration area.)

**Roles Configuration alignment (per FSI review)**: the existing Role Management screen builds its privilege matrix live from `GET /Privilege/Entities` grouped by module. CMS privileges (`Cases.View/Operate/Authorize`, `RiskConfig.Manage/Override`, plus **tab-level privileges** for the case workspace — `Cases.Tab.Onboarding/Comparison/Events/Notes/Parties/Audit`, mirroring the Tab Visibility concept) are seeded into the catalogue but **surfaced conditionally**: the privilege-entities endpoint filters the CMS group out when `Provider ≠ TmxCms` (or module disabled), so Role Management neither displays nor assigns them; the FE nav/route guards read the same source. Enforcement stays two-layered and consistent by construction — `[RequiresPrivilege]` on every CMS endpoint is always active regardless of presentation, and disabled-module endpoints 404 anyway, so a stale grant can never open a hidden surface. Roles `Case Analyst L1–3` / `Case Authorizer L1–3` are seeded with the module and appear in Role Management only under TMX CMS mode.

**Configuration & admin screen inventory (all planned — consolidated view; each is maker-checker'd, versioned, privilege-gated, follows the Field-Config/Auto-Flags admin idioms, ships in M2 unless noted):**

| # | Screen | Controls | Backing tables | Detail |
|---|---|---|---|---|
| 1 | **Case Management category** in the existing `/admin/configuration` | Provider selector (`OracleFccm`\|`TmxCms`), enabled/reconciliation toggles, provider-scoped settings, Verify chips (FCCM connection/onboarding · CMS readiness) | `TmX_Config_Category/Setting` (+`Provider_Scope`) | §6 — M0 |
| 2 | **Risk Models / Algorithm Builder** | Model list (jurisdiction × customer type × model type × version, Draft/Active/Retired), parameter+weight grid (Oracle parity: Parameter · Weight · Comments), Copy-model, activate/publish | `Risk_Model`, `Risk_Param_Weight` | §9 |
| 3 | **Parameter Library** | Parameter registry: code/label/data type, source binding (mapping key), `Lookup_Type`, multi-value strategy | `Risk_Parameter` | §9 |
| 4 | **Score Maps (reference sheets)** | Per-parameter value→score rows (AF=10, PK=8…), values picked from the bound `TmX_Lookup` type, global sheet + per-model overrides, import view from FCCM export | `Risk_Param_Score_Map` | §9 |
| 5 | **Rule Management** | Risk-rule builder (predicate composer on parameters, outcome, priority, stop-on-match), accelerated re-review rules, **case-generation policy** rows (band/flag-count/severity/expression criteria), dry-run test panel | `Risk_Rule`, `Accelerated_ReReview_Rule`, `Case_Generation_Policy` | §9, §10.4 |
| 6 | **Thresholds (band-range tuning)** | Score_From/To grid + range bar, live contiguity validation, auto-snap, impact preview, recommendation thresholds | `Risk_Band_Config`, `Risk_Recommendation_Config` | §9 (detailed) |
| 7 | **Correlation Rules** | Match-context builder (customer/counterparty/txn-ref/HS/flags…), case-type scoping, lookback, closed-inclusion, action, priority | `Correlation_Rule` | §12 |
| 8 | **SLA Management** *(own screen)* | Case SLA thresholds per type/priority/tier, aging states, escalation bindings, breach log view | `Sla_Config`, `Sla_Breach_Log` | §16 note (coexists with global `'sla'` config category) |
| 9 | **Workflow & Queues** | Case-type → CaseWF scheme mapping, queue routing per tier/role (scheme *editing* stays in the workflow designer) | `Queue_Config`, scheme mapping | §11 |
| 10 | **Mappings** | `Field_Mapping` editor (target entity/column, product/tab/customer-type scope, source UDF path, transform, grid strategy) + JSON profile registry (API/status/risk kinds) | `Field_Mapping`, `Mapping_Profile` | §8.5, §13 |
| 11 | **Notifications** | Event→recipient→template rules + delivery log | `Notification_Rule`, `Notification_Log` | §14 |

Per the UX brief + designer variations outcome: **Queue page** (`/cases`), **Case detail / investigation workspace** (`/cases/:caseNumber`) with events/evidence, dual-score risk panel, scorecard + comparison, related cases, recommendation card, notes/audit, decision dialog family (Recommend/Approve/Reject/Escalate + self-approval block state); **Admin screens**: Rule Builder, Algorithm Builder (parameters/weights), Thresholds, Score-map/Lookup manager, Correlation rules, SLA config, Mapping profiles — following the Field-Config/Auto-Flags admin idioms; **Scorecard comparison** view; license/mode gating from app-init. All server-driven, privilege-gated, no localStorage state.

---

<a id="s17"></a>
## 17. Audit & traceability — all audit gates (AG1–AG12)

**Direct answer to the FSI review question (decision/workflow history vs case audit)**: yes — workflow transition history is maintained by the workflow engine (OptimaJet today, byte-compatible under the FSI engine) in `WorkflowProcessTransitionHistory`. But the case audit trail is **deliberately not dependent on that alone**: the CMS schema keeps its own complete, case-scoped record from creation onward — `Case_Audit` (every non-transition action: notes, links, priority, reassignment, attachments), `Case_Status_History` (every status change with actor/time), `Case_Decision` (every recommend/approve/reject/send-back with disposition + remarks), and `Workflow_Execution_Log` (a case-scoped **projection/copy** of the engine's transitions). So the full lifecycle — created → correlated → assigned/claimed → investigated → escalated → recommended → decided → closed — is answerable entirely from `[CMS].*` tables, independent of the shared workflow tables (which also matters for future extraction). The engine history remains the workflow-mechanics source of truth; the CMS copy exists so the case's audit story is self-contained.

The twelve gates from the superseded doc carry forward verbatim as **go-live exit criteria**, extended with the request's explicit logs: **rule triggered** (`Rule_Execution_Log` per scorecard: rule, predicate, matched values, outcome), **flag generated** (auto-flag `Match_Proof`, already live), **algorithm executed** (`Algorithm_Execution_Log`: per-parameter value/score/weight/contribution), **risk score calculation** (scorecard `Inputs_Json` + model version ⇒ re-derivable), **user actions** (case audit + notes + decisions), **workflow transitions** (engine history + `Workflow_Execution_Log` projection), **assignment changes** (claim/reassign audited), **status history** (`Case_Status_History`), **configuration changes** (maker-checker `TmX_Request` + model/profile versions + who/when — Oracle "History" parity), **mapping changes** (profile versions), **API calls** (`Api_Call_Log` + existing request/response payload capture + module Serilog files), **external integrations** (FCCM exchange logs already on the ledger). Gates recap: AG1 immutable trail · AG2 decision explainability · AG3 segregation of duties (incl. no-self-approval guard) · AG4 change control/versioning · AG5 access/authn (callback secret enforced) · AG6 config integrity (fail-fast, no silent stubs) · AG7 retention/immutability · AG8 reproducibility/lineage · AG9 migration evidence · AG10 log governance · AG11 one-query audit pack (case→events→scorecard→decisions→history export) · AG12 regulatory extracts. **Internal-audit walkthrough of all gates in UAT is a Phase-M4 exit criterion.**

---

<a id="s18"></a>
## 18. Sequence diagrams

### 18.1 KYC onboarding — provider-routed (generic controller → router → one provider)

```text
Finalise hook (KYC product)
   |  POST /api/v1/CaseManagement/Onboarding            (generic controller — §15)
   v
Provider Router ── reads CaseManagement:Provider ──> insert ledger row (Submitted, Case_Source)
   |
   |── Mode = Fccm ────────────────────────────────────────────────────────────────┐
   |     map payload (Mapping module) -> POST InitiateOnboardingService (Basic)    |
   |     CaseProgressWorker polls FCC_OB_RA: CASE_ID -> CaseCreated                |
   |                                RISK_SCORE + RISK_CATEGORY_KEY -> RiskAssessed |
   |                                                                               |
   |── Mode = TmxCms ─────────────────────────────────────────────────────────┐ |
   |     Scoring Engine: ALGORITHM pass (always)  +  RULE pass (always)          | |
   |        -> supersession (HigherWins) -> band -> recommendation               | |
   |     Case_Generation_Policy gate (§10.4): triggered (High/PEP/NotRec/rule)?  | |
   |        YES -> Case Engine: CMS.Case (KYC category) + CaseWF + correlation   | |
   |        NO  -> scorecard only; txn maker-checker remains the human gate      | |
   |                                                                             | |
   +--> Scorecard v(n) written  (BOTH modes)  <-------------------------------- + +
   |
   +--> Reconciliation (if Enabled): vs BRAINS (PEP wins / higher wins)
   |      Disabled -> provider band final, Reconciliation_Status='Disabled' (§10.3)
   v
Decision:  Fccm = callback {caseID, actionCode}   |   Internal = CaseWF ApproveClosure
        -> ICaseDecisionApplier (one impl): transaction status + remark
           + CustomerReviewSchedule + CMS.Case_Decision row
```

### 18.2 TBML case + parallel approval (TMX CMS mode) — Case_Events ARE the raised flags

```text
Trade finalise, redflagsCount > 0        (0 flags -> CA-NOHIT branch: status 3059, no case)
   |  POST /api/v1/CaseManagement/Trade
   v
Case Engine: CMS.Case (TBML_SURV, priority from customer band)
             + CMS.Case_Event rows  = 1 per RAISED RED FLAG (matched value + proof)
                                      + 1 customer-details event
   |
   +--> Correlation Engine: evaluate Correlation_Rules -> link / attach-events / review-flag
   |
   v
CaseWF_TBML (FSI workflow engine)
   L1 pool --AssignToMe--> L1 assigned --investigate--> RecommendClosure(disposition)
      |                         |                             |
      |   (SLA breach: AppDelay auto-escalation L1->L2->L3)   v
      |                                              Authorizer pool (queue routing)
      |                                                 FillApprovers (parallel set)
      |                                                 each approver: Approve
      |                                                 IsApproveComplete -> finalise
      v
ICaseDecisionApplier: transaction status + remark + Case_Decision + audit
```

---

<a id="s19"></a>
## 19. Migration strategy (phased; every phase ships dark behind config/license)

Precondition: FSI workflow engine ≥ Phase 5 (CaseWF portability note: schemes authored in the shared dialect run on OptimaJet meanwhile if sequencing demands).

| Phase | Scope | Exit criteria | Effort |
|---|---|---|---|
| **M0 — Foundations & fixes** | `CMS` schema + `CmsDbContext` (incl. structured-data-layer tables §8.5); split provider options + fail-fast validation (conditional binding §6); `ConfigOnlyLicenseGate` (license file deferred per Decision 11); provider port + **Oracle provider relocation** (zero behaviour change); callback secret enforced; send-back branch restored; Fccm-mode scorecard writes incl. breakdown rows (start reading `RISK_SCORE`); **FCCM DB model export** (score maps, weights, ranges, rules) + calibration report | Fccm mode regression green with module structure in place; calibration signed off; audit fixes live | **2.5 wk** |
| **M1 — TMX CMS core** | Scoring Engine (models/rules/algorithm/bands/override/recommendation + execution logs); Case Engine (case core tables, numbering, events); CaseWF schemes (3+3 + parallel approval) + roles/privileges; correlation engine + default rule; decision applier unification; provider router (binary); `CaseProgressWorker` generalisation | TMX-CMS-mode KYC + TBML E2E green on dev DB (incl. CA-NOHIT, dual-score scorecard, dual-control closure, segregation guard); Fccm regression green | **3.5 wk** |
| **M2 — Admin & UI** | Rule/Algorithm/Threshold/Score-map/Correlation/SLA/Mapping admin screens (maker-checker); Cases FE module (queue + workspace + decision dialogs per chosen design option); scorecard comparison UI; notifications rules (log-only until Slice 8); search; dashboard widgets; audit-pack export | Investigator + authorizer journeys fully in-UI; **AG1–AG12 walkthrough passed in UAT** | **3.5–4 wk** |
| **M3 — Parallel-run evidence (shadow, binary modes)** | Production stays `Provider=Fccm` while the TMX CMS engines run in **shadow**: every live assessment is also scored by the TMX CMS Scoring Engine (compute-and-log only — no CMS case created, no dual-write) with a weekly FCCM-vs-TMX band diff report; UAT runs full `Provider=TmxCms` E2E in parallel | ≥4 weeks shadow with zero unexplained band diffs; UAT TMX-CMS-mode sign-off; compliance approves the Active model | **calendar 4–6 wk** |
| **M4 — Switch & Oracle retirement** | Production config flips to `Provider=TmxCms` (restart); **in-flight FCCM cases continue by `Case_Source` continuity** (poller + callback stay alive for them) until the ledger drains to zero; batch risk-level file obligation re-homed if TMX inherits it (§21 A5); then decommission Oracle connectivity config; FCCM-era records remain readable forever | 30 days TMX-CMS-only stable; ledger shows zero in-flight FCCM cases; retirement checklist signed; license/audit evidence archived | **0.5 wk + bake** |
| Rollback (any point) | Config flips back to `Fccm` (+restart); in-flight continuity by `Case_Source` in both directions (TMX-era cases stay readable/decidable in TMX; new cases go back to FCCM); schema is additive; FCCM path untouched until M4 completes | Rehearsed in UAT during M3 | — |

**Data migration**: none required for cutover (drop-in ledger + additive schema). Optional: historical scorecard backfill from `KycCaseRequest`/`Cstm_Brains`; open-FCCM-case import is **not** planned (they drain naturally via `Case_Source` continuity after the switch). **Testing approach**: unit (engines, esp. scoring truth tables + supersession), integration (provider router, decision parity FCCM-callback vs CaseWF byte-identical effects), scoring shadow diffs, E2E per mode, UAT playbooks (extend the existing KYC validation-log method), FE build/lint gates, audit walkthrough.

---

<a id="s20"></a>
## 20. Risks

| # | Risk | L/I | Mitigation |
|---|---|---|---|
| R1 | Scoring divergence vs FCCM (weights/formula) | M/H | DB-level model export (UI proven empty §1); shadow scoring evidence; compliance sign-off gate; HigherWins supersession explicit |
| R2 | Regulatory acceptance of TMX-issued cases | M/H | AG walkthrough; richer explainability than Oracle; discovery item A6 before M3 widens |
| R3 | Workflow-engine dependency slips | M/M | CaseWF schemes portable to OptimaJet interim |
| R4 | Post-switch era overlap (in-flight FCCM cases coexisting with new TMX cases) | M/M | `Case_Source` continuity rules; unified read UI with source badges; drain monitored via ledger; never dual-write |
| R5 | Scope breadth (engines ×12) dilutes delivery | M/H | Phases gate features; matrix P-phases; premium items licensed later (§22) |
| R6 | Config sprawl / mis-set modes | M/M | One switch + fail-fast matrix; license cross-check; no silent stubs (fixed M0) |
| R7 | FCCM callback after retirement | L/M | Callback endpoint outlives routing until ledger drains; decisions exempt from license gate |
| R8 | Test surface doubles across modes | M/M | Shared-core design; mode-parameterised E2E suite |
| R9 | Two-era history confuses investigators | L/L | Source badges, scorecard continuity (Fccm rows written from M0) |

<a id="s21"></a>
## 21. Assumptions (each validated at the phase named)

A1 FCCM DB export of weights/rules/ranges is obtainable via DBA (M0) — UI grids empty in this env. A2 `FccmActionCodeStatusMap` vocabulary beyond 30004/30003/983/984 confirmed with Oracle team (M0). A3 One jurisdiction ("All"/PAK) and predominantly ORG customer type in scope initially; model dimensions support more (M1). A4 Case numbering `TMX-CA{seq}` acceptable to compliance/regulator (M1). A5 BRAINS batch risk-level file exchange ownership decided before M4 (format documented). A6 Regulatory extracts from FCCM ECM inventoried with compliance ops before M3 widens (drives AG12). A7 Slice-8 notification transport lands independently; rules log-only until then. A8 The live instance remains available for M0 export + M3 shadow comparison.

<a id="s22"></a>
## 22. Recommendations & future enhancements beyond Oracle

**Recommendations**: approve M0 immediately (it hardens Fccm mode regardless: security fixes, scorecards, calibration); run the M3 shadow phase long enough to cover at least one full periodic-review cycle before flipping to Internal; note that the TBML flow's FCCM path is already non-functional in the target environment (missing staging objects) — the switch delivers immediate real-world value there; treat the scorecard as a first-class compliance artifact from M0 so history predates the switch.

**Beyond-Oracle enhancements** (several already in the design): dual-score explainability with per-parameter arithmetic + rule terms (Oracle shows outputs only); correlation link provenance; scorecard trend + factor-delta comparison; one-query audit pack; designer-editable investigation workflows; mixed-era unified history. **Future/premium (license-gated)**: entity relationship graph visualisation; ML-assisted triage (suggested disposition from historical outcomes — always human-decided, explainable-features-only); cross-customer network correlation (shared counterparties/addresses); SLA analytics + workload balancing auto-assignment; STR/regulatory filing generation from case data; event-streaming integration surface (webhooks) for downstream systems.

---

*End of master plan. Approval asks: (1) the plan as consolidated single source of truth; (2) M0 start; (3) A1–A8 owners assigned. Every contract change ships with `FE_CHANGES_REQUIRED.md` / `BE_revamp_Changes.md` entries per standing rules.*

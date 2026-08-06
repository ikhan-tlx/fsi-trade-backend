# Case Management Module — Implementation Plan (modular, provider-based, licensed)

> ⛔ **SUPERSEDED (2026-07-15)** by [`ECM_MASTER_PLAN.md`](ECM_MASTER_PLAN.md) — the consolidated single source of truth. Retained for history; the M1–M5 misfit resolutions were carried into the master plan (schema renamed `CaseManagement` → `CMS`).

**Status**: ⛔ Superseded — see banner. (Original: plan for approval.)
**Date**: 2026-07-14
**Builds on**: `FCCM_ANALYSIS_AND_DUAL_MODE_ARCHITECTURE.md` (functional model: case types, CaseWF, risk model, correlation, audit gates — all still authoritative), `MODULARISATION_PLAN.md` (module/licensing shape), `WORKFLOW_ENGINE_REPLACEMENT_PLAN.md` (CaseWF execution). **This plan supersedes the dual-mode doc on two points**: table placement (dedicated `CaseManagement` schema instead of `dbo TmX_Case*` names) and mode taxonomy (adds **Hybrid**).

---

## 1. Fit review first — what in the request does NOT suit this codebase (with recommended resolutions)

You asked for an explicit misfit check. Five items need adjustment; everything else fits cleanly.

| # | Requested | Why it doesn't suit as stated | Recommendation (adopted in this plan) |
|---|---|---|---|
| M1 | *"EF Core/Fluent migrations"* | The repo deliberately uses **hand-written, numbered, idempotent SQL migrations** (`database/migrations/YYYY_MM_NNN_*.sql`) — there is no EF code-first migration pipeline, and mixing two migration frameworks against one database is an operational hazard (ordering, drift, deploy tooling) | Use the project's migration framework, schema-qualified: `database/migrations/casemanagement/2026_MM_NNN_*.sql`, tagged per the modularisation plan's slice-tag discipline. Your parenthetical "(or the project's migration framework)" is exercised |
| M2 | Case **workflow state inside the `CaseManagement` schema** (implied by "all Case Management tables under this schema") | CaseWF executes on the shared workflow engine whose tables (`WorkflowProcessInstance`, `WorkflowInbox`, history) are engine-owned in `dbo` and shared with transaction workflows. Duplicating an engine persistence set per module would fork the engine | Case rows store `Process_Instance_Id` **by value, no cross-schema FK constraint**. Workflow state stays engine-owned. On future extraction, the module either points at the host's workflow API or hosts its own engine instance (the FSI engine is a NuGet library precisely for this) |
| M3 | **Hybrid = both providers active** could be read as dual-write (both FCCM and TMX create a case for the same event) | Two systems of record for the same compliance event is an **audit-gate violation** (two case numbers, two decisions possible — AG1/AG3 hazard) | Hybrid = **routing, never dual-write**: exactly one provider per case *flow* (per case type and/or per era), plus unified read-side. See §6.3 |
| M4 | *"License key and license validation"* as module-local config | A second, parallel licensing mechanism would conflict with the already-designed platform licensing (`MODULARISATION_PLAN.md`: HMAC-SHA256-signed license file, module bundles, unlicensed endpoints 404) | Case Management licensing is an **entry in the platform license file** (module + feature flags), validated by the same signed-license infrastructure — implemented now for this module, becoming the first real consumer of the modularisation plan's licensing design. See §8 |
| M5 | Risk Scorecard placement ambiguity (is risk scoring part of Case Management?) | Scoring serves KYC onboarding/review; cases *consume* assessments. But extracting Case Management later without the scorecard would strand the case decision context | Risk model + scorecard tables live in the **same `CaseManagement` schema** (they travel together on extraction), but in their own namespace (`Risk_*` tables, `Features/CaseManagement/Risk/` code) so a later split of "Risk" from "Cases" stays cheap. In Fccm/Hybrid modes the scorecard is still generated (§9.4) so history is complete regardless of provider |

Also worth stating: **enable/disable semantics** need care — turning Case Management off must not break KYC/TBML transaction flows (they proceed without case creation; FCCM-mode submission continues if that provider is on). The matrix in §7.2 makes every combination explicit rather than implied.

---

## 2. Module boundary

**In the module** (`CaseManagement`): case entities/lifecycle, case events/evidence links, correlation engine + rules, risk model + scorecard (per M5), provider abstraction (FCCM adapter re-homed, TMX provider), case APIs, case configuration + license gate, Cases FE feature folder.
**Outside (consumed via contracts only)**: workflow engine (`IWorkflowEngine`), transactions/customers (read-only lookup contracts), BRAINS screening (`IKycScreeningService`), lookups, RBAC/privileges, notifications (Slice-8 port when it lands), audit log.

**Coupling rules** (module-fitness versions of the modularisation plan's discipline points, enforced at review):
1. No EF navigation properties from CaseManagement entities to `dbo` entities — cross-boundary references are **value keys** (`Transaction_Id`, `Customer_Code`, `Process_Instance_Id`) resolved through read contracts.
2. No MediatR handler in `Features/CaseManagement` imports another slice's handlers/DTOs; shared needs go through `Application/Contracts/*`.
3. Trade/KYC slices never reference CaseManagement types — they raise **domain events/hooks** (`ITradeCaseService`/`IKycCaseService` seams already exist; the module implements them).
4. `CaseManagementDbContext` maps only `[CaseManagement].*` tables; `ApplicationDbContext` never does.
5. Module registration is a single `AddCaseManagementModule(configuration)` + `MapCaseManagementEndpoints()` — the `IComplianceModule` shape from the modularisation plan.

---

## 3. Code structure (same repo, extraction-ready)

Feature-sliced within the existing projects (consistent with the repo today), with module-scoped folders so extraction = folder moves, not surgery:

```
Services/
├── FSI.Trade.Compliance.Domain/
│   └── Entities/CaseManagement/            Case, CaseEvent, CaseLink, CaseAudit,
│                                            CorrelationRule, RiskModel, RiskParameter,
│                                            RiskParamWeight, RiskParamScoreMap, RiskRule,
│                                            RiskBandConfig, RiskScorecard (+ enums)
├── FSI.Trade.Compliance.Application/
│   ├── Contracts/CaseManagement/           ICaseManagementProvider, ICaseRepository,
│   │                                        IRiskScorecardService, ICaseCorrelationEngine,
│   │                                        ICaseDecisionApplier, ICaseManagementLicenseGate,
│   │                                        provider DTOs (vendor-neutral)
│   └── Features/CaseManagement/            CQRS slices: Cases/{Create,List,Detail,Claim,
│                                            Recommend,Approve,Link,Notes}, Risk/{Assess,
│                                            Scorecard,Compare,ModelAdmin}, Correlation/{RuleAdmin},
│                                            Providers/{status,operations}
├── FSI.Trade.Compliance.Infrastructure/
│   ├── CaseManagement/
│   │   ├── Persistence/                    CaseManagementDbContext (+ configurations, own
│   │   │                                    connection-string key w/ fallback to Default)
│   │   ├── Providers/Fccm/                 FccmCaseManagementProvider (wraps the EXISTING
│   │   │                                    FccmKycCaseService/FccmTradeCaseService/Oracle
│   │   │                                    reader/poller — code moves, behaviour doesn't)
│   │   ├── Providers/Tmx/                  TmxCaseManagementProvider (case mgr, CaseWF glue,
│   │   │                                    TmxRiskAssessmentService)
│   │   ├── Correlation/                    CaseCorrelationEngine
│   │   └── Licensing/                      CaseManagementLicenseGate (platform license reader)
│   └── (existing folders untouched)
└── FSI.Trade.Compliance.API/
    └── Controllers/CaseManagement/         CasesController, RiskScorecardController,
                                             CaseAdminController (correlation/risk-model admin)
database/migrations/casemanagement/          numbered idempotent SQL, schema-qualified
tmx-finance-frontend-revamp/src/features/cases/   FE module (api/pages/components) per the UX brief
```

Namespaces `FSI.Trade.Compliance.*.CaseManagement.*` throughout — a later extraction to `FSI.CaseManagement.*` projects is a rename + project split along folders that already don't cross-reference.

---

## 4. Database — `CaseManagement` schema

- `2026_MM_001_CreateCaseManagementSchema.sql`: `CREATE SCHEMA [CaseManagement]` (idempotent), dedicated role grants (app login gets schema-scoped rights — also the future lift-out seam).
- All module tables under `[CaseManagement]`, **FKs only within the schema**; cross-boundary references are indexed value columns (M2). Table set = the dual-mode doc §7 designs, re-homed and de-prefixed (schema replaces the `TmX_` prefix): `Case`, `Case_Event`, `Case_Link`, `Case_Audit`, `Correlation_Rule`, `Risk_Model`, `Risk_Parameter`, `Risk_Param_Weight`, `Risk_Param_Score_Map`, `Risk_Rule`, `Risk_Band_Config`, `Risk_Recommendation_Config`, `Risk_Scorecard` (§9), `Case_Sequence` (numbering), `Provider_Ledger` view over `dbo.KycCaseRequest` (below).
- **`dbo.KycCaseRequest` stays in dbo** — it predates the module, is written by the finalise hook in both modes, and is shared history. The module reads it through a mapped view/read-model; new columns (`Case_Source`) remain additive on the dbo table. (Moving it would break the FCCM-era ledger and the Risk Assessment tab for zero benefit.)
- Indexing: queue-serving composite (`Status, Tier, Priority, Opened_At`), `Customer_Code`, `Transaction_Id`, scorecard `(Customer_Code, Version desc)`.
- **Future DB split readiness**: separate `ConnectionStrings:CaseManagement` key (defaults to `DefaultConnection`); `CaseManagementDbContext` is the only reader/writer; no cross-schema FK constraints; no cross-schema joins in SQL (read contracts do two-step lookups); migrations folder self-contained. Moving to its own database later = restore schema + change one connection string.

---

## 5. Data access

`CaseManagementDbContext` (schema default `CaseManagement`) + repository interfaces in `Contracts/CaseManagement` (`ICaseRepository`, `IRiskConfigRepository`, `IScorecardRepository`, `ICorrelationRuleRepository`) implemented in `Infrastructure/CaseManagement/Persistence`. Handlers depend on the interfaces, not the context — the module's data layer is mockable and swappable (extraction requirement). Existing repo conventions apply: no raw SQL in handlers, FluentValidation validators, `ValidationBehaviour` pipeline.

---

## 6. Provider abstraction

### 6.1 The port

```csharp
public interface ICaseManagementProvider
{
    string ProviderKey { get; }                       // "Fccm" | "Tmx"
    Task<CaseSubmissionResult> SubmitKycCaseAsync(KycCaseSubmission s, CancellationToken ct);
    Task<CaseSubmissionResult> SubmitTradeCaseAsync(TradeCaseSubmission s, CancellationToken ct);   // owns CA-NOHIT semantics
    Task<CaseProgress>        GetProgressAsync(CaseRef r, CancellationToken ct);                    // poll/read lifecycle
    Task<RiskAssessmentResult> GetRiskAssessmentAsync(CaseRef r, CancellationToken ct);
    Task ApplyExternalDecisionAsync(CaseDecision d, CancellationToken ct);                          // FCCM callback / CaseWF closure → shared ICaseDecisionApplier
    Task<OperationResult>     RetryAsync/RepollAsync/RefreshAsync(CaseRef r, CancellationToken ct);
}
```

- **`FccmCaseManagementProvider`**: adapter over the *existing, proven* FCCM services (`FccmKycCaseService`, `FccmTradeCaseService`, `FccmOracleReader`, poller) — relocation into the module namespace, zero behaviour change; the existing `IKycCaseService`/`ITradeCaseService` seams from the dual-mode doc become thin forwards to the selected provider.
- **`TmxCaseManagementProvider`**: the standalone implementation (dual-mode doc §7): `TmxRiskAssessmentService` (rule + algorithm stages), `CaseManager` (case rows, numbering, events, correlation call, CaseWF instance via `IWorkflowEngine`), decision post-hooks → `ICaseDecisionApplier`.
- Business logic (finalise hook, ledger lifecycle, reconciliation vs BRAINS, review scheduling, Risk Assessment tab) stays **provider-agnostic** — unchanged from the dual-mode design.

### 6.2 Selection

`ICaseManagementProviderRouter` resolves the provider per submission from configuration (§7). Both providers are DI-registered; the router (not DI rebinding) decides — this is what makes **Hybrid** and per-case-continuity possible: a case is always progressed by the provider that created it (`Case_Source` on the ledger row is the routing key for in-flight work), regardless of the current default for new cases.

### 6.3 Hybrid mode — defined (per M3: routing, never dual-write)

`Provider = Hybrid` activates per-flow routing:

```jsonc
"Hybrid": {
  "KycProvider":   "Fccm",   // KYC onboarding/review cases
  "TradeProvider": "Tmx"     // TBML surveillance cases
}
```

Supported hybrid intents: (a) **flow split** — e.g. keep FCCM for KYC onboarding (regulator familiarity) while TMX handles TBML cases (whose FCCM path is already effectively stubbed in the target env); (b) **migration overlap** — new cases route to TMX while FCCM-era in-flight cases continue on FCCM until drained (routing by `Case_Source`); (c) unified read — the Cases module and Risk Assessment history render both sources side-by-side (already designed: source badges, mixed-era history). **Exactly one provider ever owns a given case.** Dual-write is rejected by design (audit gate AG1/AG3).

---

## 7. Configuration management

### 7.1 Keys (follows the existing `Integration:*` options-class conventions, bound to `CaseManagementOptions`)

```jsonc
"CaseManagement": {
  "Enabled": true,                          // module master switch
  "Provider": "Fccm",                       // "Fccm" | "Tmx" | "Hybrid"
  "Hybrid": { "KycProvider": "Fccm", "TradeProvider": "Tmx" },
  "License": { "FilePath": "license.lic" }, // platform license file (§8); key material NOT in appsettings
  "Tmx": {
    "CaseNumberPrefix": "TMX-CA",
    "Correlation": { "Enabled": true, "EvaluateOnEventAttach": false },
    "Scheme": { "KYC_ONBOARDING": "CaseWF_KYC", "TBML_SURV": "CaseWF_TBML" }
  },
  "Fccm": { /* existing Integration:Fccm* keys relocate here over time; alias kept during transition */ }
}
```

### 7.2 Startup validation + behaviour matrix (fail-fast, per audit gate AG6)

| `Enabled` | `Provider` | Behaviour | Startup validation |
|---|---|---|---|
| false | — | No case creation anywhere; KYC/TBML transactions flow normally; Cases UI hidden; scorecards still generated if `Risk` sub-feature licensed (§9.4) | none |
| true | Fccm | Today's behaviour via the provider adapter | Oracle conn + onboarding URL present (Production) |
| true | Tmx | Standalone: scoring + TMX cases + CaseWF | Score map non-empty; Active risk model exists; CaseWF schemes deployed |
| true | Hybrid | §6.3 routing | Union of the above per routed flow |
| any | any | License must cover the selected provider/mode (§8) | signed-license check |

Config changes are deploy-time (appsettings per environment) consistent with how `Brains:Mode` and `Integration:*` work today — no runtime admin toggle in this phase (a runtime provider flip mid-flight would bypass the routing continuity guarantees; revisit only with the router's `Case_Source` continuity rules proven).

---

## 8. Licensing (rides the platform design — M4)

First real implementation of the `MODULARISATION_PLAN.md` licensing model, scoped to this module:

- **License file**: JSON payload + HMAC-SHA256 signature (plan's format): `{ customer, issued, expires, modules: { "CaseManagement": { "enabled": true, "features": ["FccmProvider","TmxProvider","Hybrid","RiskScorecard","CorrelationEngine","AuditPack"] } }, sig }`.
- **`CaseManagementLicenseGate`** (`ICaseManagementLicenseGate`): validates signature + expiry + feature set at startup and caches; consulted by the provider router (mode must be licensed), endpoint mapping (unlicensed module ⇒ case endpoints **404**, per the plan's unlicensed-behaviour decision), and FE bootstrap (module hidden — surfaced via the existing app-init payload so the FE nav gates on it like a privilege).
- **Granularity**: module availability, each provider (`FccmProvider`/`TmxProvider`), `Hybrid`, and named premium capabilities (future: entity-graph, advanced correlation, SLA analytics) — new features are additive strings, no schema change.
- **Expiry behaviour (audit-safe)**: expired license ⇒ module goes **read-only** (existing cases/scorecards viewable, exports allowed, no new case creation or decisions) — never data loss, never a stuck FCCM callback (external decision application is exempted from the gate; a late FCCM decision always lands — AG1).
- The signing key lives with FSI (build/release), not in the repo; validation code treats absence of file as "unlicensed", not error-crash.

---

## 9. Customer Risk Scorecard (historical, comparable)

### 9.1 Persistence — `[CaseManagement].[Risk_Scorecard]`

One row per **completed assessment event** (KYC onboarding *and* every periodic review), immutable:

`Scorecard_Id PK · Customer_Code (idx) · Version (per-customer increasing) · Trigger (Onboarding | PeriodicReview | Refresh | Override) · Transaction_Id · Request_Id (ledger ref) · Provider_Source (FCCM | TMX) · Model_Id/Model_Version (TMX) · Overall_Score decimal(5,2) · Algorithm_Score · Rule_Based_Band · Superseded_By_Rule · Effective_Band (Risk_Category_Key vocab) · Brains_Band · Reconciled_Band + Prevails · Rcmnd_To_Onbrd · Factor_Scores_Json (per-parameter: label/value/score/weight/contribution) · Rule_Outcomes_Json (rule/terms/matched values/result) · Override_Json · Created_At/By`

This **is** the `TmX_Risk_Assessment` design from the dual-mode doc §7.1, promoted to the customer-facing "scorecard" artifact with per-customer versioning. TMX-mode fills every column; **FCCM-mode fills what FCCM exposes** (`Overall_Score` from `FCC_OB_RA.RISK_SCORE` — start reading it, today only the band is read; band; factor/rule JSON null) so history is continuous across providers and eras (M5, §9.4).

### 9.2 Comparison (current vs previous)

- `GET /api/v1/Cases/Customers/{code}/Scorecards` (list, newest first) and `/Scorecards/Compare?from={v}&to={v}` (defaults: latest vs previous). Compare payload: overall delta, band change, per-factor deltas (`factor, before, after, Δscore, Δcontribution`, sorted by |Δ|), rule outcomes appearing/disappearing, model-version change note, trigger + timestamps.
- **UI**: a "Risk Scorecard" panel on the Case detail (KYC) and Customer Insight page: current card + trend sparkline (versions over time) + comparison view highlighting movers ("Country of Residence 45 → 90 (+18.0 contribution) — drove the band change Low → High"), rule-outcome diffs badged NEW/CLEARED. Extends the existing Risk Assessment tab components; FE ledger entry when built.

### 9.3 Generation points (shared pipeline, provider-agnostic)

Scorecard writes happen in the shared post-assessment step (after reconciliation): onboarding completion, every periodic-review completion (`KycReviewOrchestrator` loop — cadence already exists), manual `Refresh`, and manual override (a new version with `Trigger=Override`). Nothing is updated in place — versions only (AG1/AG8).

### 9.4 Scorecards without the Cases module

Because scoring serves KYC (not only cases), scorecard generation is gated by the **`RiskScorecard` license feature**, not by `CaseManagement.Enabled` — a bank can run FCCM-mode with scorecards on and the Cases UI off. (This is the practical consequence of M5.)

---

## 10. APIs (module-owned, standard envelope/versioning/privileges)

`/api/v1/Cases` — list (queue, Kendo filters), `{caseNumber}` detail, `{caseNumber}/Events|Links|Notes|Audit`, claim/commands ride the existing generic `PUT /Workflow/Process/{id}/Execute`; `/api/v1/Cases/Customers/{code}/Scorecards` (+ `/Compare`); admin: `/api/v1/Cases/Admin/CorrelationRules`, `/RiskModel` (CRUD via maker-checker). Privileges `Cases.View/Operate/Authorize`, `RiskConfig.Manage/Override` seeded via `rbac_grants.sql` discipline. Every route logged to `FE_CHANGES_REQUIRED.md` when shipped.

---

## 11. Phasing (slots into the dual-mode roadmap — extends its S-phases where they overlap)

| Phase | Adds on top of dual-mode S-phases | Effort delta |
|---|---|---|
| CM0 (with S0) | Schema + migration scaffold, `CaseManagementDbContext`, options/validation, license gate + file format, provider port + **Fccm provider adapter** (relocation), scorecard table + FCCM-mode scorecard writes (incl. reading `RISK_SCORE`) | +1 wk over S0 |
| CM1 (with S1) | Tmx provider, router + Hybrid, correlation engine + rules admin API, scorecard TMX columns, CaseWF glue | ≈ S1 (structure absorbs it) |
| CM2 (with S2) | Cases FE module (per UX brief variations outcome), scorecard comparison UI, license-driven FE gating, audit-pack | ≈ S2 +0.5 wk |

Audit gates §13 of the dual-mode doc apply unchanged; the license gate adds AG-relevant evidence (signed file, startup validation logs).

---

## 12. Deliverables mapping (your list → this plan)

1. `CaseManagement` schema + migrations → §4 (project migration framework per M1)
2. Modular implementation in-repo → §2–§3, coupling rules
3. Configuration Management → §7 (incl. behaviour matrix)
4. Licensing → §8 (platform license file, feature-granular, audit-safe expiry)
5. Provider abstraction → §6 (Fccm / Tmx / **Hybrid = routing, no dual-write**)
6. Customer Risk Scorecard → §9 (versioned, historical, comparison API + UI, provider-continuous)
7. Extraction-ready structure → §3/§4/§5 (own DbContext + connection key, contracts-only coupling, folder-aligned namespaces, `IComplianceModule`-shaped registration)

---

## 13. Open items

1. Confirm Hybrid default routing (proposal: `Kyc=Fccm, Trade=Tmx` given the target env's missing trade staging objects).
2. License file issuance process (who signs, how delivered per environment) — needed before CM0 exit.
3. Scorecard retention: keep all versions forever (recommended — compliance artifact) vs archival policy after N years — compliance to confirm.
4. Whether `Integration:Fccm*` keys relocate under `CaseManagement:Fccm` in CM0 (clean) or alias until the next major deploy (safe) — recommend alias.
5. FE gating source for the module (license flag in app-init payload) — needs one `FE_CHANGES_REQUIRED.md` entry at CM0.

---

*End of plan. Approve M1–M5 resolutions first — they are the only deviations from the literal request, each chosen to fit the codebase's established conventions and the audit gates.*

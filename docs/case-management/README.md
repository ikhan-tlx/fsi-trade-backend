# Case Management — Document Set

**Single source of truth: [`ECM_MASTER_PLAN.md`](ECM_MASTER_PLAN.md)** (consolidated 2026-07-15). Last reorganised: 2026-07-15.

| # | Document | Role | Status |
|---|---|---|---|
| 1 | [`ECM_MASTER_PLAN.md`](ECM_MASTER_PLAN.md) | **THE plan**: live-instance analysis, current-system analysis, 32-feature FCCM comparison matrix, architecture & engines, provider abstraction (binary `OracleFccm`/`TmxCms` behind a generic CaseManagement controller), configuration, licensing, `CMS` schema + ERD, rule/algorithm builders, scoring + Customer Risk Scorecard, CaseWF (3+3, parallel approval), correlation, mapping, audit gates AG1–AG12, sequence diagrams, migration M0–M4 (parallel run + Oracle retirement + rollback), risks/assumptions/recommendations/future enhancements | ✅ Active — awaiting approval (M0 start + A1–A8 owners) |
| 2 | [`../WORKFLOW_ENGINE_REPLACEMENT_PLAN.md`](../WORKFLOW_ENGINE_REPLACEMENT_PLAN.md) | Foundation dependency: FSI-owned workflow engine that `CaseWF` executes on (its own initiative, kept in `docs/`) | Plan, awaiting approval |
| 3 | [`CASE_MANAGEMENT_UX_DESIGN_BRIEF.md`](CASE_MANAGEMENT_UX_DESIGN_BRIEF.md) | Designer-agent brief: Queue + Investigation Workspace + decision-dialog variations (Option A/B standalones) | Handed to designer agent |
| 4 | [`FCCM_ANALYSIS_AND_DUAL_MODE_ARCHITECTURE.md`](FCCM_ANALYSIS_AND_DUAL_MODE_ARCHITECTURE.md) | History — absorbed into #1 | ⛔ Superseded 2026-07-15 |
| 5 | [`CASE_MANAGEMENT_MODULE_PLAN.md`](CASE_MANAGEMENT_MODULE_PLAN.md) | History — absorbed into #1 (schema renamed `CaseManagement`→`CMS`) | ⛔ Superseded 2026-07-15 |

## Notes

- Designer output (standalone HTMLs + rationale) goes to `D:\ICBC - Latest\Design Html\` per repo convention, referenced from #3.
- Repo placement decision: in-repo extraction-ready module (master plan §4); separate repo only on a concrete trigger (second consumer app, independent cadence/team, standalone sale).
- Live Oracle FCCM instance (`172.20.3.73:7001`) analyzed 2026-07-15 via authenticated read-only session — findings in master plan §1; key consequence: model calibration export must come from the FCCM **database**, not the admin UI (grids empty under the only jurisdiction).

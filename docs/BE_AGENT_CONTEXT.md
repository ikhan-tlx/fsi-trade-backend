# FSI.Trade.Compliance — Backend Agent Context

**Audience:** the next agent or engineer who needs to ship backend work on this project. Read this top-to-bottom before touching code. Most of what you need to know lives in linked docs; this file is the table of contents + the conventions that aren't written down anywhere else.

**Last updated:** 2026-05-29

---

## Table of contents

1. [What this project is](#1-what-this-project-is)
2. [Repo layout](#2-repo-layout)
3. [Working agreements — non-negotiable](#3-working-agreements)
4. [Slice inventory — what's shipped](#4-slice-inventory)
5. [Critical implementation gotchas](#5-critical-implementation-gotchas)
6. [Document map — where to find what](#6-document-map)
7. [Open items + next priorities](#7-open-items)
8. [Common workflows](#8-common-workflows)
9. [Build, run, smoke-test](#9-build-run-smoke-test)

---

## 1. What this project is

FSI.Trade.Compliance is a .NET 8 backend replacing the legacy .NET Framework ICBC Pakistan Trade Compliance system. It's a strangler-fig rewrite — the legacy backend (`tmx-finance-backend`, `tmx-finance-integrations`, `Integration KYC`, `tdl-authserver`) stays alive in parallel until each functional slice is migrated and 30 days production-stable.

**Customer-facing surface:**

- Trade Repository (Import / Export / Guarantee) — dynamic-form-driven transaction capture
- KYC onboarding — Brains screening + FCCM case creation
- Reports — DotLiquid HTML, PuppeteerSharp PDF, ClosedXML Excel
- Workflow — OptimaJet engine on existing DB schema (license-pinned to 3.5.0)

**Tech stack:**

- .NET 8, ASP.NET Core, EF Core 8, MediatR, FluentValidation
- OptimaJet WorkflowEngine v3.5.0 (pinned for license parity)
- Oracle.ManagedDataAccess.Core 23.6.0 (FCCM Oracle staging)
- DotLiquid (reports HTML), PuppeteerSharp (PDF), ClosedXML (Excel)
- Clean Architecture: Domain / Application / Infrastructure / API

**Frontend:** lives at `D:\ICBC - Latest\tmx-finance-frontend-revamp\` and is owned by a separate FE agent. The contract between BE and FE is logged in [`FE_CHANGES_REQUIRED.md`](../../tmx-finance-frontend-revamp/FE_CHANGES_REQUIRED.md).

**Database:** SQL Server (`ICBC_DEMO` database on `WAQASKHAN\SQLEXPRESS` in dev). FCCM staging lives on Oracle — a separate DB accessed via `Oracle.ManagedDataAccess.Core` from the same project. There is no separate "FCCM staging SQL Server" — see [§5 — FCCM staging gotcha](#5-critical-implementation-gotchas).

---

## 2. Repo layout

```
D:\ICBC - Latest\
├── FSI.Trade.Compliance/                  ← THIS PROJECT (BE owned)
│   ├── README.md                          (Slice 1 only; outdated)
│   ├── BACKLOG.md                         deferred work + open questions
│   ├── SANITY_PENDING.md                  living deferred-testing checklist
│   ├── API_GUIDELINES.md                  REST conventions, RBAC discipline
│   ├── docs/
│   │   ├── BE_AGENT_CONTEXT.md            this file
│   │   ├── DB_ENTITY_USAGE.md             which entities map to which DB tables
│   │   ├── SCHEMA_REFERENCE.md            DB schema notes
│   │   ├── LEGACY_FCCM_TRADE_FLOW.md      14-op reference for Slice 9
│   │   ├── LEGACY_TRANSACTION_FLOW.md     legacy transaction lifecycle
│   │   ├── OPTIMAJET_POC.md               workflow engine port research
│   │   └── MODULARISATION_PLAN.md         post-revamp licensed-monolith plan
│   ├── database/
│   │   ├── migrations/                    numbered SQL migrations (idempotent)
│   │   ├── seed/                          rbac_grants.sql + other seeds
│   │   └── diagnostics/                   read-only inventory queries
│   └── Services/
│       ├── FSI.Trade.Compliance.Domain/           POCOs + enums only
│       ├── FSI.Trade.Compliance.Application/      MediatR features + contracts
│       │   ├── Contracts/                         interfaces (ports)
│       │   ├── Common/                            shared models, behaviours, exceptions, options
│       │   └── Features/                          CQRS slices: <Module>/<Action>/Query|Command|Handler|Validator
│       ├── FSI.Trade.Compliance.Infrastructure/   adapters (EF, HTTP, Oracle, file IO)
│       └── FSI.Trade.Compliance.API/              controllers, filters, middleware, Program.cs
│
├── tmx-finance-frontend-revamp/           ← FE owned (DO NOT EDIT)
│   ├── FE_CHANGES_REQUIRED.md             BE→FE migration log (BE writes; FE reads)
│   └── PROJECT.md                         FE spec
│
├── BE_revamp_Changes.md                   FE→BE asks (FE writes; BE reads)
│
└── Legacy reference projects (READ-ONLY, kept for code archaeology):
    ├── tmx-finance-backend/
    ├── tmx-finance-integrations/
    ├── Integration KYC/
    ├── tdl-authserver/
    └── ICBC_DEMO-Schema.sql               full DB schema dump
```

**Within `Features/`:** each slice has its own folder. Convention is `<Module>/<Action>/<Verb>Command|Query.cs` + `<Verb>CommandHandler.cs`. Validators live alongside as `<Verb>CommandValidator.cs` and are picked up automatically by the `ValidationBehaviour` MediatR pipeline (see `Application/Common/Behaviours/ValidationBehaviour.cs`).

---

## 3. Working agreements

These have been hard-earned this slice. Don't break them.

### 3.1 No backend aliases for breaking changes

When a URL is renamed, a field is removed, a privilege gate is added — **the BE does NOT add a server-side shim or alias**. Instead, log a Pending entry in [`FE_CHANGES_REQUIRED.md`](../../tmx-finance-frontend-revamp/FE_CHANGES_REQUIRED.md) describing the change and the migration the FE engineer needs to apply.

Why: aliases create permanent maintenance burden. The migration log gives FE clear ownership of the catch-up work without freezing BE evolution.

This rule was set after we briefly added a `LegacyGetFieldsByProduct` alias in `ProductController` and then reverted it (2026-05-25).

### 3.2 Every BE contract change is logged to `FE_CHANGES_REQUIRED.md`

At the time of the work, not after. URL renames, response-shape additions, new endpoints, new privileges, new request bodies — all get an entry. The FE engineer reads from this file.

If the FE hits a 404 / 400 / 422 / 403 that isn't already documented in `FE_CHANGES_REQUIRED.md`, it's a BE miss and worth flagging.

### 3.3 Privileges are gated and seeded

Every new endpoint that mutates state needs `[RequiresPrivilege("Module.Action")]`. Reads don't always need one — judgment call — but if the endpoint is admin-scoped, gate it.

When adding a privilege, you MUST also update [`database/seed/rbac_grants.sql`](../database/seed/rbac_grants.sql):

- Section 1 — append the privilege code + description (max 100 chars).
- Section 2 — append the grant for IT Admin.
- Section 3 — for "universal" privileges that EVERY role needs (e.g. `Privileges.View` for FE bootstrap), append there too.

Re-run the file on the target environment after deploy. Idempotent — safe to re-run.

### 3.4 Vendor names live only in Infrastructure

URLs and Application interfaces speak pure domain language. Vendor product names (BRAINS, FCCM, OptimaJet) appear ONLY in `Infrastructure/Integrations/` (or DI registrations). Swapping a vendor is a one-line DI change; Application and API never know.

Examples:

```
URL                  Application contract       Infrastructure adapter
─────────────────    ─────────────────────      ────────────────────────
/Customer/{id}/Kyc   IKycScreeningService       BrainsKycScreeningService
/Customer/{id}       ICustomerMasterService     LocalCustomerMasterReader  ← reads TmX_Customer_Master
/Kyc/Case            IKycCaseService            FccmKycCaseService
                                                  + FccmHttpClient + FccmOracleReader
```

### 3.5 Migrations are numbered + idempotent

`database/migrations/YYYY_MM_NNN_Description.sql`. Every CREATE / ALTER / INSERT guarded by `IF NOT EXISTS` or `NOT EXISTS` subquery. Safe to re-run.

Sequence: keep numbers monotonic. Gaps are cosmetic only (e.g. 026 was reverted; 027 still works without it).

Current head: `2026_05_028_SeedTradeLookupCatalogue.sql` (a no-op tombstone; see [§4 Slice 11](#4-slice-inventory) for why).

### 3.6 Lookup catalogue is cross-tenant

`TmX_Lookup` partitions data across multiple `Tenant_ID` values:

- Tenant 1 — bulk catalogue (HSCodeList, Industry, Port, Nationality, …)
- Tenant 12 — user-attribute lookups (GENDER, PEP, Yes_No, …)
- Tenants 245 / 248 — workflow-scoped overrides (APPLICATION_STATUS, Command_Application_Status_Mapping)

When reading lookups, **use the cross-tenant cascade**: caller's tenant per LookupType → fall back to Tenant 1 → fall back to lowest-with-data. See `GetLookupsByCultureQueryHandler` for the implementation. Don't naively filter by `TenantId == caller's tenant` — you'll silently lose ~90% of the catalogue.

### 3.7 No skill/policy bypass

The FE owns `tmx-finance-frontend-revamp/`. BE does not edit anything inside it. The only BE write into that tree is appending to `FE_CHANGES_REQUIRED.md`.

---

## 4. Slice inventory

In rough delivery order. Each entry: scope, key files, status.

### Slice 1 — Auth foundation
**Status:** ✅ Done. JWT, refresh, password-change, ResetExpiredPassword. ASP.NET Identity core + custom `TmxUserStore` (no AspNet* tables — merged into `TmX_User`).

**Key files:** `Features/Auth/`, `Infrastructure/Identity/`, `Infrastructure/Persistence/Configurations/`. Migration `2026_05_001_MergeAspNetUsersIntoTmxUser.sql` + `2026_05_002_CreateRefreshTokens.sql`.

### Slice 2 — User & RBAC
**Status:** ✅ Done. User CRUD, Role CRUD, privilege matrix, `[RequiresPrivilege]` attribute.

**Key files:** `Features/Users/`, `Features/Roles/`, `API/Filters/RequiresPrivilegeAttribute.cs`. Privileges seeded via `database/seed/rbac_grants.sql`.

### Slice 3 — Lookup + Configurations
**Status:** ✅ Done (v1) + reworked in Slice 11 P0. Two endpoints: `/Lookup/{culture}` and `/Configurations/GetUserCompanyConfigurations`.

**Key files:** `Features/Lookups/`, `Features/AppConfigurations/`. See [Slice 11 P0](#slice-11--p0-lookup--customer-fixes) for the v2 grouped endpoint.

### Slice 4 — Integrations
**Status:** ✅ Done. BRAINS KYC (HTTP), Customer master, FCCM Case create (HTTPS + Oracle poll). Hosted `FccmCaseIdPoller` runs every 5s.

**Key files:** `Infrastructure/Integrations/Brains/`, `…/CustomerMaster/`, `…/Fccm/`. Background service: `FccmCaseIdPoller.cs`.

### Slice 5 — Workflow
**Status:** ✅ Done. OptimaJet 3.5.0 (license parity with legacy — see Slice 5.6 below). `WorkflowController` consolidates the three legacy domain controllers. Custom action provider (22 actions) + rule provider (IsCreator / CheckRole / Boss). Bucket-C handlers (notifications, loan-app) currently no-op-with-log; see BACKLOG for the Slice 8 Notifications plan.

**Key files:** `Features/Workflow/`, `Infrastructure/Workflow/`. Engine bootstrap: `WorkflowRuntimeFactory.cs`.

### Slice 5.6 — OptimaJet 3.5.0 pin
**Status:** ✅ Done. Pinned package version to match the expired-but-soft-enforced legacy license key. Long-term: either replace with FSI-owned engine or procure v21 license. See `BACKLOG.md` "Workflow / OptimaJet".

### Slice 6 — Transactions
**Status:** ✅ Done. Full CRUD + cancel + flag panel. Steps 1–5 plus 6.5 (stepper endpoints — Product list, Branch LOV, Lookup by type).

**Key files:** `API/Controllers/TransactionController.cs`, `Features/Transactions/`.

### Slice 7 — Reports
**Status:** ✅ Done. HTML render (DotLiquid), PDF (PuppeteerSharp), Excel (ClosedXML). Smoke-tested. PuppeteerSharp Chromium download configured to `%LOCALAPPDATA%\FSI.Trade.Compliance\PuppeteerCache` to keep it off the D: drive.

**Key files:** `Features/Reports/`, `Infrastructure/Reports/`.

### Slice 8 — Flag catalogue
**Status:** ✅ Done. Six tables (`TmX_Document`, `TmX_Flag_Catalogue`, `TmX_Flag_Catalogue_Locale`, `TmX_Flag_Scope`, `TmX_Transaction_Flag`, `TmX_Transaction_Flag_History`). Migrated ~488 legacy MRL fields into catalogue + scope rows. Full CRUD admin endpoints under `/Flag` with `Flags.View` / `Flags.Manage` privileges.

**Migrations:** `2026_05_011` … `2026_05_014`.

### Slice 9 — Trade-to-FCCM submission
**Status:** ✅ Done. Workflow post-execute hook fires `IFccmTradeCaseService.SubmitAsync` when a transaction's workflow finalises. Implements 14 Oracle ops via pure ADO.NET (Option A — no EF for Oracle staging). Branches on `redflagsCount == 0` to skip FCCM and set status `CA-NOHIT`.

**Key files:** `Infrastructure/Integrations/Fccm/FccmOracleReader.cs` (13 Oracle methods), `Infrastructure/Integrations/Fccm/FccmTradeCaseService.cs` (pipeline). Reference doc: [`docs/LEGACY_FCCM_TRADE_FLOW.md`](LEGACY_FCCM_TRADE_FLOW.md).

**Migration:** `2026_05_015_AddFccmCaseIdToTransaction.sql` — adds `Fccm_Case_Id` column to `TmX_Transaction`. If POST /Transaction is throwing "Invalid column name 'Fccm_Case_Id'", this migration hasn't been run.

### Slice 10 — Transaction Detail Page enrichment + DynamicForm split
**Status:** ✅ Done.

Six sub-tasks from `BE_revamp_Changes.md §7`:
- `GET /Transaction/{id}/WorkflowHistory` — projects `WorkflowProcessTransitionHistory`
- `workflow.assignedActors[]` on `/Transaction/{id}` — from `WorkflowInbox`
- `GET /Transaction/{id}/AuditTrail` — merges transition history + remarks + lifecycle stamps
- `GET/POST /Transaction/{id}/Documents` — via new `TmX_Transaction_Document` bridge
- `POST /Transaction/{id}/Notes` — writes `ApplicationRemark` (Module_Code `"5"`)
- `productSummary` sub-object on `/Transaction/{id}` — back-traced from TenantFieldSetup + UDF_Data

Plus DynamicForm enhancements:
- `Is_Active` filter on TenantFieldSetup (excludes deactivated MRL fields)
- `UDF_Data` passthrough on field nodes (carries `{"TabId":N}` for sub-product navigation)
- `selectorTabId` signal on `/Product/{id}/Tabs` + `/Product/{id}/FormDefinition` (null = one-shot load, non-null = lazy)
- New endpoints: `/Product/{id}/Tabs` (lightweight) + `/Product/{id}/Tabs/{tabId}/Fields` (lazy per-tab)
- Removed: `/Product/Tabs/Mappings` (superseded by per-product variant)

**Migrations:** `2026_05_025_DynamicFormEnhancements.sql` (the FE-side migration that added the columns) + `2026_05_027_CreateTransactionDocumentBridge.sql`.

### Slice 11 — P0 lookup + customer fixes
**Status:** ✅ Done (BE), ⏳ Pending (FE migration to v2 lookup endpoint).

Two BE-side P0 fixes:

1. **Lookup handler** — was filtering by `Tenant_ID == caller's tenant` (245), which excluded the Tenant 1 catalogue (~60K rows). Fixed with cross-tenant cascade. Same URL, no FE action needed.

2. **`GET /Customer/{id}` rewired to local DB** — was proxying to BRAINS (always empty for Trade products). Now reads `TmX_Customer_Master` snapshots via new `LocalCustomerMasterReader`. KYC stays on BRAINS via `/Customer/{id}/Kyc`.

Plus a v2 lookup endpoint (new) with grouped shape + 30-day in-memory cache:

- `GET /api/v2/Lookup/{culture}` returns `{ culture, lookups: { LookupType: [...] } }` with lean rows (`id / code / label / parent`).
- ~60% payload reduction; eliminates client-side grouping.
- Cached in `IMemoryCache` per `(tenantId, culture)` for 30 days.

**Migration:** `2026_05_028_SeedTradeLookupCatalogue.sql` is now a **no-op tombstone**. The original seed was redundant — the legacy catalogue was already in the DB; the handler bug was the only issue.

---

## 5. Critical implementation gotchas

Things that have bitten people. Read before you assume.

### 5.1 FCCM staging is Oracle, not SQL Server

The legacy "FCCM integration" runs against an Oracle DB with `MANTAS.*_STAGING` tables. The new BE uses **pure ADO.NET** (`Oracle.ManagedDataAccess.Core`) — there is NO EF Core mapping for the staging tables, and there is NO separate SQL Server DB called "FccmStaging".

`IntegrationOptions.FccmOracleConnection` is the single connection string for ALL FCCM ops (case-id poll reads + staging INSERTs + SP call + KDD_CASE_CUSTOMERS / KDD_CASES enrichment + correlation INSERTs).

If empty, `FccmOracleReader.IsStub` returns true and every op logs a STUB warning and returns synthetic values (dev mode).

### 5.2 `WorkflowInbox.IdentityId` is uniqueidentifier in this deployment

```csharp
// WRONG — won't compile
inbox.IdentityId == userId

// RIGHT
if (!Guid.TryParse(userId, out var identityGuid)) return Empty;
inbox.IdentityId == identityGuid
```

But `WorkflowProcessTransitionHistory.ExecutorIdentityId` is `nvarchar(256)` in the same deployment (verified against `ICBC_DEMO-Schema.sql`). Both columns hold the same User_ID Guid; they just store the type differently. Use string comparison for transition history, Guid for inbox.

### 5.3 OptimaJet v3.5 API quirks

- `WorkflowState` has no `.IsFinal` member. Resolve via `R.Builder.GetProcessScheme(schemeCode)`.
- `ProcessDefinition` has no `.States` collection. Use `ProcessDefinition.Activities` where each `Activity` has `.State` and `.IsFinal`.
- `WorkflowInbox.AvailableCommands` column is absent in this deployment (only `Id, ProcessId, IdentityId` exist). EF property is `.Ignore()`d.
- `WorkflowInbox.AddingDate` also absent — same treatment.

### 5.4 Lookup catalogue tenant cascade

`TmX_Lookup` data spans Tenants 1 / 12 / 245 / 248. **Do not** add a naïve `WHERE Tenant_ID = caller` filter — you'll silently exclude ~90% of the catalogue. Use the per-LookupType cascade pattern from `GetLookupsByCultureQueryHandler` (caller > Tenant 1 > lowest with data).

### 5.5 `Privileges.View` must be universal

The FE bootstrap calls `GET /Privilege/Entities` regardless of role to render permission gates client-side. If a non-IT-Admin role lacks `Privileges.View`, the SPA gets a 403 and fails to render.

`rbac_grants.sql` Section 3 grants `Privileges.View` to every role via `CROSS JOIN TmX_Role × TmX_Privilege`. New roles created via the role-CRUD endpoint should either auto-grant this (backlog item) or trigger a re-run of `rbac_grants.sql`.

### 5.6 `TmX_Privilege.Privilege_Description` is `nvarchar(100)`

The rbac_grants.sql `@scoped` table-variable column matches. Strings over 100 chars throw `String or binary data would be truncated`. There's a comment-block warning in the seed file — heed it.

### 5.7 Excel cell limit

ClosedXML throws on string values >32,767 chars. The `ClosedXmlReportExcelExporter` truncates with a "..." marker. If you're writing arbitrary text into cells (UDF dumps, audit blobs), use the same truncation helper.

### 5.8 Swashbuckle 6.8.x + `IFormFile` 500 errors

Swashbuckle 6.8.1 throws on `[FromForm] IFormFile` actions with sibling parameters (`[FromQuery]`, `CancellationToken`). Workaround: `[ApiExplorerSettings(IgnoreApi = true)]` on the action. The endpoint still works; it just doesn't appear in Swagger. Test with curl.

### 5.9 Customer master is NOT a master

`TmX_Customer_Master` is misnamed — it's actually a **per-transaction customer snapshot** table. The C# entity was renamed to `TransactionCustomerSnapshot` for clarity; the DB table name stays for legacy compat. The same `Customer_Code` can appear many times (one row per transaction).

`LocalCustomerMasterReader` returns the most-recent snapshot when serving `GET /Customer/{id}`.

### 5.10 PuppeteerSharp Chromium download

PDF generation downloads a ~500 MB Chromium binary on first use. Default location is `bin/Debug/net8.0/.local-chromium/` which fills the D: drive fast. We've configured `Reports.ChromiumCachePath` (defaults to `%LOCALAPPDATA%\FSI.Trade.Compliance\PuppeteerCache`) — override in appsettings if the C: drive is also tight.

---

## 6. Document map

Start with whichever matches your task.

| Doc | When to read |
|---|---|
| [`BACKLOG.md`](../BACKLOG.md) | Deferred work + open questions. Pick next slice from here. |
| [`API_GUIDELINES.md`](../API_GUIDELINES.md) | REST URL conventions, status-code mapping, validation pattern, RBAC seed discipline, PR checklist. |
| [`SANITY_PENDING.md`](../SANITY_PENDING.md) | Living checklist of deferred testing per slice. Run before declaring a slice "really done". |
| [`docs/DB_ENTITY_USAGE.md`](DB_ENTITY_USAGE.md) | EF entity ↔ DB table mapping table. |
| [`docs/SCHEMA_REFERENCE.md`](SCHEMA_REFERENCE.md) | Per-table schema notes. |
| [`docs/LEGACY_FCCM_TRADE_FLOW.md`](LEGACY_FCCM_TRADE_FLOW.md) | 14-op reference for Slice 9 — what each Oracle op does and how the new BE replays it. |
| [`docs/LEGACY_TRANSACTION_FLOW.md`](LEGACY_TRANSACTION_FLOW.md) | Legacy transaction lifecycle and column-level documentation. |
| [`docs/OPTIMAJET_POC.md`](OPTIMAJET_POC.md) | Workflow engine port research; what carries over between v3 and v21. |
| [`docs/MODULARISATION_PLAN.md`](MODULARISATION_PLAN.md) | Post-revamp licensed-modular-monolith plan (Compliance Lite → Enterprise tiers). |
| [`../BE_revamp_Changes.md`](../../BE_revamp_Changes.md) | **FE → BE asks.** Read before picking the next slice — most live issues come from here. |
| [`../tmx-finance-frontend-revamp/FE_CHANGES_REQUIRED.md`](../../tmx-finance-frontend-revamp/FE_CHANGES_REQUIRED.md) | **BE → FE log.** Every BE contract change gets an entry. Append at the top (newest first). |
| `../ICBC_DEMO-Schema.sql` | Full DB schema dump. The source of truth for column types + indexes. |
| `../DynamicForm_Integration_Guide.md` | Migration 20260522_001 reference — what Is_Active / Is_Visible / UDF_Data on dynamic-form tables mean. |

---

## 7. Open items

### From `BE_revamp_Changes.md` — still pending

In rough priority order:

| # | § | Item | Severity |
|---|---|---|---|
| 1 | 10.1 | `Administration/TabVisibility` GET/PUT — tenant-wide config blob | 🟡 Medium (FE runs on localStorage) |
| 2 | 10.2 | `Administration/FieldConfig` GET/PUT — per-form field list with label/visible mutations | 🟡 Medium (FE runs on localStorage) |
| 3 | 10.3 | `Administration/ManualRedFlags` CRUD — **decision needed** between adapter shape vs FE migration to `/Flag` | 🟡 Medium |
| 4 | 8 | `PUT /Report/GenerateCsv` — CSV export for Business Reports | 🟢 Low (deferred by FE) |
| 5 | 6 | KYC "Modify existing customer" lookup endpoint | ⏸️ Explicitly deferred |
| 6 | 2 / 4 | Verify `Select a Tab` Shape A + Submit-like command label | 🟡 30-sec curl pass |

### Backlog (from `BACKLOG.md`)

Highlight items only — read the file for full context:

- Slice 8 — Notifications & Approval Services (Bucket C handlers — currently no-op)
- Replace OptimaJet with FSI-owned workflow engine (post-revamp, large)
- Procure OptimaJet v21 license (alternative to above)
- `Active_Flag` lifecycle semantics on RBAC tables
- Forgot-password / email-link reset
- "New device" notification + trusted-device promotion
- Concurrent-session cap
- Audit log retention / archival
- UUID v7 for new Guid-keyed tables
- `TmX_User.User_Identifier` int identity surrogate (post-revamp FK migration wave)
- Delete legacy projects once new backend is stable in prod for 30 days

### Sanity testing — deferred but tracked

[`SANITY_PENDING.md`](../SANITY_PENDING.md) covers per-slice deferred testing for Slices 7, 8, 8 Step 5, 8 Step 6, 9. Pick this up when you're between slices and want a clean baseline.

---

## 8. Common workflows

### 8.1 Adding a new endpoint

1. Decide URL — follow `API_GUIDELINES.md`. Domain-named, plural-noun resources.
2. Application layer:
   - `Features/<Module>/<Action>/<Verb>Command.cs` (or `Query.cs`) — request DTO + result DTO.
   - `Features/<Module>/<Action>/<Verb>CommandHandler.cs` — MediatR handler.
   - `Features/<Module>/<Action>/<Verb>CommandValidator.cs` (optional) — FluentValidation rules.
3. Controller:
   - `API/Controllers/<Resource>Controller.cs` — action method, `[RequiresPrivilege("Module.Action")]` if it mutates state.
4. RBAC seed:
   - Append privilege code to Section 1 of `database/seed/rbac_grants.sql` (description ≤ 100 chars).
   - Append IT Admin grant to Section 2.
   - If universal, append to Section 3 too.
5. FE log:
   - Append a new entry at the top of `FE_CHANGES_REQUIRED.md` with URL, request body, response shape, verify steps.
6. Build → test → mark slice done.

### 8.2 Adding an EF entity

1. `Domain/Entities/<Folder>/<Entity>.cs` — POCO only.
2. `Infrastructure/Persistence/Configurations/<Folder>/<Entity>Configuration.cs` — column mapping, indexes.
3. `Infrastructure/Persistence/ApplicationDbContext.cs` — add the `DbSet<>`.
4. `Application/Contracts/Persistence/IApplicationDbContext.cs` — add the `DbSet<>` declaration to the interface.
5. If a new table: write a migration in `database/migrations/`.

### 8.3 Running a migration

```powershell
sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E `
  -i "D:\ICBC - Latest\FSI.Trade.Compliance\database\migrations\<file>.sql"
```

All migrations are idempotent — safe to re-run.

### 8.4 Re-seeding RBAC

```powershell
sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -E `
  -i "D:\ICBC - Latest\FSI.Trade.Compliance\database\seed\rbac_grants.sql"
```

Run this after deploying a new endpoint with `[RequiresPrivilege(...)]`. Idempotent; re-runs are harmless.

---

## 9. Build, run, smoke-test

### 9.1 Build

```powershell
cd "D:\ICBC - Latest\FSI.Trade.Compliance"
dotnet build
```

### 9.2 Run

```powershell
cd "D:\ICBC - Latest\FSI.Trade.Compliance\Services\FSI.Trade.Compliance.API"
dotnet run
```

Default ports per `appsettings.json` — Swagger UI at `https://localhost:5081/swagger`.

### 9.3 Smoke test priorities

After any non-trivial change:

1. **Health** — `GET /api/v1/Health`
2. **Login** — `POST /api/v1/Auth/Login` — IT Admin user
3. **Lookup** — `GET /api/v1/Lookup/en` → ~70 lookup types in the list
4. **Lookup v2** — `GET /api/v2/Lookup/en` → grouped dictionary
5. **Product list** — `GET /api/v1/Product/list`
6. **Product tabs** — `GET /api/v1/Product/2/Tabs?culture=en` → carries `selectorTabId`
7. **Transactions** — `GET /api/v1/Transaction` → grid responds; pick a row, `GET /api/v1/Transaction/{id}` → detail payload includes `workflow.assignedActors[]`, `productSummary`, `remarks[]`, `flags[]`.

Then exercise the area you touched.

---

## End of context

If something here is wrong or stale, update it before moving on. This file is for the next agent — keep the floor swept.

Anything not covered here is either in one of the linked docs or in code comments. Code comments are written to be informative — read them. Skim a handler before duplicating its pattern.

Good luck. Be conservative about contract changes; aggressive about logging them.

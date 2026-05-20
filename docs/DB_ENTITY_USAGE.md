# Database Entity Usage Map — FSI.Trade.Compliance

**Purpose**: catalog every table the new backend reads or writes today,
plus every legacy table in/out of scope. Used for two things:

1. New joiners can navigate "where does this data live" without code-spelunking.
2. Operations team has an evidence-backed cleanup checklist for after the
   new backend has been stable in prod for ~30 days — Section 5 enumerates
   tables that are SAFE to drop, and what must be verified first.

**Refresh discipline**: every new slice MUST update this doc when it
introduces / drops / consolidates a table. The slice's PR description
should include the section that was updated.

---

## Legend

| Symbol | Meaning |
|---|---|
| 🆕 | NEW table created by an FSI.Trade.Compliance migration |
| ✅ | Existing table actively used by the new backend |
| ⚠️ | Existing table touched indirectly (FK only, never written) |
| 🚧 | In scope for a future slice (Slice 5 or 6) — not yet wired |
| 🗑️ | Cleanup candidate after new backend is stable + verified |
| ❓ | Existence in live DB unverified by us; placeholder |

---

## 1. New tables introduced by FSI.Trade.Compliance migrations

| 🆕 Table | Migration script | Domain entity | EF config | Read/Write paths |
|---|---|---|---|---|
| `RefreshTokens` | `2026_05_002_CreateRefreshTokens.sql` | `RefreshToken` | `RefreshTokenConfiguration.cs` | Auth Login (W), Refresh (R/W rotation), Logout (W revoke), ChangePassword (W revoke chain) |
| `UserDevices` | `2026_05_004_CreateUserDeviceAndLoginAudit.sql` | `UserDevice` | `UserDeviceConfiguration.cs` | Login (W register), DeviceTrackingMiddleware (R per-request), Sessions endpoints (R list, W revoke) |
| `TmX_Login_Audit` | `2026_05_004_CreateUserDeviceAndLoginAudit.sql` | `LoginAuditEntry` | `LoginAuditEntryConfiguration.cs` | Append-only by LoginCommandHandler, RefreshTokenCommandHandler, concurrent-login enforcement, ChangePassword |
| `KycCaseRequest` | `2026_05_007_CreateKycCaseRequest.sql` | `KycCaseRequest` | `KycCaseRequestConfiguration.cs` | KYC submit (W), poller (R/W state transitions), GetCaseStatus (R), Callback (W terminal state) |
| `TmX_Document` | `2026_05_011_CreateFlagCatalogue.sql` | `Documents.Document` | `Documents/DocumentConfiguration.cs` | Generic file-attachment store. Slice 8 Step 5: `POST /Document` (W upload via `IDocumentStorage`), `GET /Document/{id}` (R download), flag-evidence linkage from `TmX_Transaction_Flag.Evidence_Document_ID`. |
| `TmX_Flag_Catalogue` | `2026_05_011` + `2026_05_013` seed | `Flags.FlagCatalogue` | `Flags/FlagCatalogueConfiguration.cs` | Master flag definitions. Read paths: `GetTransactionByIdQueryHandler` (embedded flags), `ListTransactionFlagsQueryHandler`, `TopFlaggedQueryHandler`. Write: catalogue admin UI (future). |
| `TmX_Flag_Catalogue_Locale` | `2026_05_011` | `Flags.FlagCatalogueLocale` | `Flags/FlagCatalogueLocaleConfiguration.cs` | Per-locale translations. Empty on initial seed; populated when multi-locale deployment is needed. |
| `TmX_Flag_Scope` | `2026_05_011` + `2026_05_013` seed | `Flags.FlagScope` | `Flags/FlagScopeConfiguration.cs` | "Which product / tab carries which flag." Read path: `TransactionFlagProjection` (LEFT JOIN target). 488 rows after Dummy filter. |
| `TmX_Transaction_Flag` | `2026_05_011` + `2026_05_014` backfill | `Flags.TransactionFlag` | `Flags/TransactionFlagConfiguration.cs` | Per-transaction current state. Read: detail + standalone + stats. Write: `UpdateTransactionCommandHandler` via `TransactionFlagDiffer` (no separate save endpoint per FSI direction). |
| `TmX_Transaction_Flag_History` | `2026_05_011` + `2026_05_014` backfill | `Flags.TransactionFlagHistory` | `Flags/TransactionFlagHistoryConfiguration.cs` | Append-only audit. One row per dimension changed (state / notes / evidence). Backfill rows attribute to `"Backfill (Slice 8 migration)"` with synthetic `Changed_Date = Transaction.Created_Date`. |

Ten tables are owned by the new backend. Schema changes go through
new numbered migrations.

---

## 2. Existing legacy tables actively used

| ✅ Table | Domain entity | EF config | Operations | Used by |
|---|---|---|---|---|
| `TmX_User` | `ApplicationUser` | `ApplicationUserConfiguration.cs` | R/W | Auth (login/refresh/lockout, password change), User CRUD (Slice 2), TmxUserStore (custom IUserStore) |
| `TmX_Role` | `Role` | `RoleConfiguration.cs` | R/W | Role CRUD (Slice 2 Step 3), RoleQueryService (JWT roles claim), User-role assignment |
| `TmX_Privilege` | `Privilege` | `PrivilegeConfiguration.cs` | R/W | Privilege scaffold (Slice 2 Step 2), `[RequiresPrivilege]` filter resolution, GET /Privileges/Entities |
| `TmX_Role_Privilege_Mapping` | `RolePrivilegeMapping` | `RolePrivilegeMappingConfiguration.cs` | R/W | PrivilegeService.GetPrivilegesForRolesAsync (hot path, every authenticated request), Role's privilege matrix bulk-replace |
| `TmX_User_Role_Mapping` | `UserRoleMapping` | `UserRoleMappingConfiguration.cs` | R/W | RoleQueryService (JWT roles), User CRUD role assignment |
| `TmX_Lookup` | `Lookup` | `LookupConfiguration.cs` | R | GET /Lookup/{culture} (Slice 3) |
| `TmX_Configuration` | `AppConfiguration` | `AppConfigurationConfiguration.cs` | R | GET /Configurations/GetUserCompanyConfigurations (Slice 3) |
| `TMX_Password_Change_Audit_Trail` | `PasswordChangeAudit` | `PasswordChangeAuditConfiguration.cs` | W | ChangePassword handler (append on success) |
| `WorkflowProcessScheme` | `Workflow.WorkflowProcessScheme` | `Workflow/WorkflowProcessSchemeConfiguration.cs` | **R only** | OptimaJet owns writes via runtime/designer. We project from the Application layer (`ListWorkflowSchemesQueryHandler` for the scheme list; `ListInboxQueryHandler` for the inbox JOIN). v21 column: `SchemeCode` (not `Code`). |
| `WorkflowProcessInstance` | `Workflow.WorkflowProcessInstance` | `Workflow/WorkflowProcessInstanceConfiguration.cs` | **R only** | OptimaJet runtime owns writes (CreateInstance / ExecuteCommand / SetState). We project from the Application layer (`ListInboxQueryHandler` JOIN). Scheme association is via `SchemeId` GUID FK, NOT a denormalised `SchemeCode` column. |
| `WorkflowInbox` | `Workflow.WorkflowInbox` | `Workflow/WorkflowInboxConfiguration.cs` | **R only** | OptimaJet runtime writes via scheme actions (FillApprovers etc.). We project for the inbox feed (`ListInboxQueryHandler`). **`AddingDate` column not mapped** — absent in this v21 deployment; Slice 5 orders by `ProcessId`; Slice 6 will swap to a JOIN against `TmX_Transaction.Created_Date` (the legacy ordering source via the `TmX_Application_VW` view). `IdentityId` is `uniqueidentifier` in v21 — handlers `Guid.Parse` `ICurrentUserService.UserId` before filtering. |
| `TmX_Product` | `Product` | `ProductConfiguration.cs` | R + targeted W | Slice 5 reads `Workflow_Scheme_Code` for product mapping. Slice 6.5 expanded the entity to surface `Product_Description`, `Product_Type_Lkp`, and `Currency_ID` for the new `GET /api/v1/Product/list` LOV. Read paths: `GetProductMappingQueryHandler` (Slice 5), `ListProductsQueryHandler` (Slice 6.5). Write path: `UpdateProductMappingCommandHandler` mutates `Workflow_Scheme_Code` + audit fields only — no full Product CRUD yet. |
| `TmX_Transaction_VW` (view) | `Transaction.TransactionListView` | `Transaction/TransactionListViewConfiguration.cs` | **R only** | `ToView` keyless projection. View itself joins Customer / Product / Branch / Workflow inbox + state and collapses multiple inbox rows into one ("Multiple Users"). Read path: `ListTransactionsQueryHandler` (Slice 6 step 1) for the Trade Repository grid. |
| `TmX_Company_Branch_Users_Mapping` | `CompanyBranchUserMapping` | `CompanyBranchUserMappingConfiguration.cs` | R | Read path: `ListTransactionsQueryHandler` — resolves the caller's effective branch list when they don't have a `Location_ID`. Effective-date filter applied at query time. Writes will land when User-CRUD branch assignment story is filled in. |
| `TmX_Company_Branch` | `CompanyBranch` | `CompanyBranchConfiguration.cs` | R | Read paths: `CreateTransactionCommandHandler` — looks up `Branch_Code` for the transaction-number prefix. Future Branch CRUD endpoints will write here. |
| `TmX_Location` | `Location` | `LocationConfiguration.cs` | R | Read path: `LocationHierarchyService.GetSelfAndDescendantIdsAsync` — single `ToListAsync` of active rows + in-memory BFS walk. Used by `ListTransactionsQueryHandler` to expand a user's home location into the full subtree. In-memory walk is viable because table is <500 rows (per FSI confirmation, May 2026). |
| `TmX_Transaction` | `Transaction.Transaction` | `Transaction/TransactionConfiguration.cs` | R + W (W lands in step 3+) | Read path: `GetTransactionByIdQueryHandler` — the transaction row itself + FK lookups. Step 3 (create) will INSERT. |
| `TmX_Transaction_Detail` | `Transaction.TransactionDetail` | `Transaction/TransactionDetailConfiguration.cs` | R + W (W in step 3+) | One UDF JSON row per transaction. Parsed server-side on read (decision A). |
| `TmX_Beneficiary_Detail` | `Transaction.BeneficiaryDetail` | `Transaction/BeneficiaryDetailConfiguration.cs` | R + W (W in step 3+) | One-to-many UDF JSON rows under a transaction. |
| `TmX_Transaction_Stakeholder` | `Transaction.TransactionStakeholder` | `Transaction/TransactionStakeholderConfiguration.cs` | R + W (W in step 3+) | One-to-many UDF JSON rows under a transaction. |
| `TmX_Customer_Master` | `Customer.TransactionCustomerSnapshot` | `Customer/TransactionCustomerSnapshotConfiguration.cs` | R + W | Per-transaction customer snapshot — NOT a global customer master (FCCM/BRAINS own the real master). Linked via `Transaction_Id`. The C# entity was renamed in Slice 6 Step 3 for clarity; the DB table name stays. Read path: `GetTransactionByIdQueryHandler`. Write path: `CreateTransactionCommandHandler` INSERTs one snapshot row per transaction; full edit/replace lands in Step 4. |
| `TmX_Customer_Banking_Details` | `Customer.CustomerBankingDetail` | `Customer/CustomerBankingDetailConfiguration.cs` | R + W (W in step 3+) | One-to-many bank accounts under a customer master row. |
| `TmX_Application_Checklist` | `Application.ApplicationChecklist` | `Application/ApplicationChecklistConfiguration.cs` | R | Read path: `GetTransactionByIdQueryHandler` (filtered to `Module_Code = "5"`). Writes when document upload lands. |
| `TmX_Application_Remark` | `Application.ApplicationRemark` | `Application/ApplicationRemarkConfiguration.cs` | R + W (workflow execute) | Read path: `GetTransactionByIdQueryHandler` (filtered to `Module_Code = "5"`). Workflow execute appends here when caller supplies comments. |
| `TmX_Application_Deviation_VW` (view) | `Application.ApplicationDeviationView` | `Application/ApplicationDeviationViewConfiguration.cs` | **R only** | `ToView` keyless projection over the deviation+approval join. Filter on read: `Module_Code = "5"`. |
| `TmX_Tab` | `Forms.Tab` | `Forms/TabConfiguration.cs` | R | Tab master; read by `GetProductFormDefinitionQueryHandler` to surface tab names + localised labels. |
| `TmX_Entity_Tab_Product_Mapping` | `Forms.EntityTabProductMapping` | `Forms/EntityTabProductMappingConfiguration.cs` | R | Active rows only. Read paths: `ListEntityTabMappingsQueryHandler` (full list, cached 5 min), `GetProductFormDefinitionQueryHandler` (filtered by product, cached 15 min). |
| `TmX_Tenant_Field_Setup` | `Forms.TenantFieldSetup` | `Forms/TenantFieldSetupConfiguration.cs` | R | Field definitions per (Tenant, Product, Tab) with validation/visibility/formula expressions (string, evaluated client-side). Read by `GetProductFormDefinitionQueryHandler`. **Replaces** the legacy stored proc `sp_GetTanantFieldsByCulture`. |
| `TmX_Template` | `Reports.Template` | `Reports/TemplateConfiguration.cs` | R | Slice 7 (Reports). Read path: `RunReportHtmlCommandHandler` — looks up the row whose `Template_Name` matches the FE-supplied `ReportName`, then renders the Liquid body (`Template_Text`) via `IReportHtmlRenderer`. Read-only from the new backend; admin authoring of templates is out of scope for now and stays in the legacy app or runs as a one-off SQL update. |

Twenty-six tables. All have a corresponding Domain entity + EF config. None are
touched by raw SQL — every access goes through EF Core LINQ, except for the
generic SP runner used by Reports (which is gated by an allowlist of
TmX_Lookup REPORT_TYPE rows, so it never executes anything ad-hoc).

---

## 3. Legacy tables touched indirectly (FK targets only)

| ⚠️ Table | How we depend on it | Note |
|---|---|---|
| `TmX_Tenant` | FK target from `TmX_User`, `TmX_Role`, `TmX_Privilege`, `TmX_Configuration`, `KycCaseRequest`, etc. | We default `Tenant_ID = 1` on writes. If multi-tenant ever activates, this becomes a hot lookup. **Do not drop.** |
| `TmX_Time_Zone` | FK from `TmX_Configuration.Time_Zone_ID` | Surfaced as `timeZoneId` on the Configurations DTO. Read-only via the FK. **Do not drop.** |

---

## 4. Tables in scope for Slice 5 / 6 (not yet wired)

| 🚧 Table | Slice | Purpose |
|---|---|---|
| `WorkflowProcessInstancePersistence` | 5+ | Per-process parameters. Engine-internal — we don't project today. May surface as an entity if the FE needs to display process parameters. |
| `WorkflowProcessInstanceStatus` | 5+ | Status + lock for the runtime. Engine-internal. |
| `WorkflowProcessTransitionHistory` | 5+ | Workflow audit trail. OptimaJet writes; read-only if the FE ever shows a "process history" timeline. |
| `WorkflowGlobalParameter` | 5+ | Workflow runtime config. Engine-internal. |
| `WorkflowProcessTimer` | 5+ | Timer / escalation scheduling. Engine-internal. |
| `TmX_Transaction` | 6 | Transaction CRUD primary table |
| `TmX_Transaction_Detail` | 6 | Transaction UDF / dynamic-form payload (JSON in `udf_data`) |
| `TmX_Customer_Master` | 6 | Customer denormalization for transaction list (also referenced by the legacy `EXEC CBS_REQUEST` SP, which Slice 6 replaces with LINQ) |
| `TmX_Application_VW` | 5 | View-backed inbox source. **Replaced** in Slice 5 by direct `WorkflowInbox` queries — new backend will not query the view. May become a cleanup candidate after Slice 5 stable. |

---

## 5. Cleanup candidates (after new backend is stable in prod ~30 days)

For each row: verify the new backend has fully replaced the legacy
function, confirm zero code references in either codebase, then drop.

| 🗑️ Table | Why it's no longer needed | Verify before drop |
|---|---|---|
| `AspNetUsers` | Merged into `TmX_User` by `2026_05_001`. Pattern A Identity (custom IUserStore) reads only `TmX_User`. | Confirm no legacy backend still queries `AspNetUsers`. Run `SELECT * INTO AspNetUsers_Backup_<date> FROM AspNetUsers` first. |
| `AspNetUserRoles` | Replaced by `TmX_User_Role_Mapping`. | Confirm legacy backend's `IcbcImplementation` and any role-check codepath has been retired. |
| `AspNetRoles` | Replaced by `TmX_Role`. | Same as above. |
| `AspNetRoleClaims` | Replaced by `TmX_Role_Privilege_Mapping`. | Same. |
| `AspNetUserClaims` | Not used in Pattern A. | Confirm zero rows reference active users (legacy may have orphaned data). |
| `AspNetUserLogins` | Not used (no external auth providers). | Confirm. |
| `AspNetUserTokens` | Replaced by `RefreshTokens`. | Confirm legacy refresh path retired. |
| `Cstm_Brains` | Legacy BRAINS risk-score cache. New backend stores risk in `KycCaseRequest.Risk_Category_Key`. | Confirm Slice 4 has been live ≥ 30 days and FE no longer reads any "old" risk surface that depended on this table. |
| `TmX_User_Device` | If this legacy table exists, it's superseded by `UserDevices`. | First confirm it's actually present (we couldn't find a definition; was renamed to avoid collision in our migration). |
| `ICBC_DEMO_AUTH` (database) | If the legacy auth was on a separate DB, after migration it's unused. | Backup before drop. |
| Stored procedure `sp_GetLookupByCulture` | Replaced by Slice 3 LINQ in `GetLookupsByCultureQueryHandler`. | Confirm zero callers from legacy backend. |
| Stored procedure `CBS_REQUEST` | Will be replaced by Slice 6 LINQ in `GetTransactionRiskSummaryQueryHandler`. | Confirm Slice 6 stable + zero callers from `IcbcBrainsController` (which itself goes away). |
| Stored procedure `DropWorkflowInbox` | Replaced inside Slice 5's OptimaJet event handler. | Confirm Slice 5 stable. |
| Stored procedure `sp_GetTanantFieldsByCulture` | Replaced by Slice 6 step 2 LINQ in `GetProductFormDefinitionQueryHandler`. | Confirm Slice 6 stable + zero callers from any legacy code path. |
| Stored procedure `sp_GetNextTransactionSequenceNumber` | Slice 6 Step 3 reads `dbo.TmX_Transaction_Sequence` directly via EF Core `SqlQueryRaw` in `TransactionNumberGenerator`. The SP was a single-line wrapper. | Confirm Slice 6 stable + zero callers from legacy. |
| Table `TmX_Tenant_Field_Setup_23` | Backup/snapshot table with the same shape as `TmX_Tenant_Field_Setup` (no PK, no FK enforcement). | Confirm it's a stale snapshot (no app code references the `_23` suffix anywhere in either backend), then drop. |

**Cleanup ordering** — proceed top-to-bottom in this list. AspNet* drops
are safest first (clear migration in `2026_05_001`). SPs go last (once
their replacement code is verified). DB-level drop (`ICBC_DEMO_AUTH`) is
the final step.

---

## 6. Tables we will NEVER drop

These are infrastructure or domain-critical — listed so cleanup scripts
explicitly skip them:

- `TmX_Tenant` — multi-tenancy backbone.
- `TmX_Time_Zone` — referenced by `TmX_Configuration`.
- `TmX_Lookup`, `TmX_Configuration` — Slice 3.
- `TmX_User`, `TmX_Role`, `TmX_Privilege`, `TmX_Role_Privilege_Mapping`, `TmX_User_Role_Mapping` — Slices 1+2.
- `TMX_Password_Change_Audit_Trail` — compliance audit; never truncate.
- `TmX_Login_Audit` — compliance audit; rotate via retention policy (BACKLOG: "Audit log retention / archival"), never truncate.
- All `Workflow*` tables — Slice 5 (OptimaJet runtime).
- `TmX_Transaction`, `TmX_Transaction_Detail`, `TmX_Customer_Master` — Slice 6.

---

## 7. External upstreams (read-only, NOT in our DB)

For completeness — these are not tables we own; they're integration boundaries:

| Upstream | Type | Accessor |
|---|---|---|
| BRAINS HTTP service | External REST API | `BrainsKycScreeningService` (Infrastructure) |
| Customer master upstream | External REST API | `CustomerMasterClient` (Infrastructure) |
| FCCM HTTP onboarding | External REST API | `FccmHttpClient` (Infrastructure) |
| FCCM Oracle DB (`FCC_OB_RA`) | External Oracle DB | `FccmOracleReader` (Infrastructure — stub today) |

We don't manage their schemas. Changes to upstream contracts go through
config (`Integration:*`) + adapter updates, never schema migrations.

---

## 8. How to update this doc

When introducing a new entity:

1. Add a row to **Section 1** (new tables) or **Section 2** (legacy tables we now use).
2. Cross-reference the slice that introduced it.
3. Note the EF config file name + Domain entity class.
4. List operations (R / W / R/W) and the handlers / middlewares that touch it.

When retiring a legacy concern:

1. Move the table to **Section 5** (cleanup candidates) with the verification checklist.
2. Cross-reference the new entity that replaced it.

When a cleanup item ships in prod:

1. Strike through the row OR move it to a `## ✅ Done` section at the bottom of Section 5.
2. Date-stamp the cleanup so we have an audit trail.

---

*Last updated: 2026-05-11 (Slice 6 step 3 — POST /Transaction. Renamed `CustomerMaster` → `TransactionCustomerSnapshot` for semantic accuracy. `sp_GetNextTransactionSequenceNumber` added to cleanup candidates — direct sequence read via `SqlQueryRaw` retires it.).*

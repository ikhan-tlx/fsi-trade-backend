# Sanity Tests — Pending

Living checklist of sanity-tests that haven't been run yet. Tick off as
each one is verified. New entries go to the top.

**Convention**:
- `⏳` — pending verification
- `🔄` — in progress
- `✅` — verified
- `❌` — verified, failed (open a bug)

---

## Slice 9 — Trade-to-FCCM submission

**Author date**: 2026-05-22  
**Status**: ⏳ Pending end-to-end smoke test

### Pre-requisites (run once before testing)

- ⏳ Apply migration `database/migrations/2026_05_015_AddFccmCaseIdToTransaction.sql`
  ```powershell
  sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -i database/migrations/2026_05_015_AddFccmCaseIdToTransaction.sql
  ```
  Verifies: `Fccm_Case_Id` column exists on `dbo.TmX_Transaction`, filtered
  index `IX_TmX_Transaction_FccmCaseId` created.

- ⏳ Re-run `database/seed/rbac_grants.sql` (adds the new privileges +
  IT Admin grants):
  ```powershell
  sqlcmd -S WAQASKHAN\SQLEXPRESS -d ICBC_DEMO -i database/seed/rbac_grants.sql
  ```
  Adds: `Transactions.Resubmit`, plus `Flags.View` / `Flags.Manage` from
  Slice 8 if not already applied.

- ⏳ Point `Integration:FccmStagingConnection` in `appsettings.Development.json`
  at any reachable SQL Server DB. Can be the same `ICBC_DEMO` DB for dev
  testing — the 4 staging tables live in their own namespace. Empty string
  → staging writes throw at submit time (intentional dev signal).

- ⏳ Pre-create the 4 staging tables (or use `dotnet ef database update`
  against the new `FccmStagingDbContext` once migrations are scaffolded for
  it). The legacy schema files are in
  `Integration KYC/Repositories/{EventStaging,CustMasterStaging,BeneficiaryDetailStaging,LcTrnDetStaging}.cs`
  — same column shapes.

### Auto-fire path

- ⏳ Create a trade transaction (`POST /api/v1/Transaction`)
- ⏳ Tick a few flags via `PUT /api/v1/Transaction/{id}` with a `flags`
  array, OR directly `INSERT INTO TmX_Transaction_Flag` rows
- ⏳ Execute the workflow command that finalises the process
  (`PUT /api/v1/Workflow/Process/{id}/Execute`)
- ⏳ Verify:
  - `TmX_Transaction.Fccm_Case_Id` is now `"STUB-{guid}"` (synthetic)
  - 1× row in `LcTrnDetStaging`, `CustMasterStaging`, `BeneficiaryDetailStaging`
  - 1× customer event + N× flag events in `EventStaging` with `MsgId`
    matching the suffix of `Fccm_Case_Id`
  - Each flag event's `Description` matches
    `TmX_Flag_Catalogue.Flag_Description` for that flag (catalogue-driven,
    NO hardcoded strings)
  - Server logs contain:
    ```
    "FCCM submit post-workflow-finalise: TxId=... CaseId=STUB-... FlagEvents=N"
    ```

### Idempotency

- ⏳ Re-execute the same workflow command (or another command that ends
  in the same finalised state)
- ⏳ Verify:
  - No new staging rows written
  - `Fccm_Case_Id` unchanged
  - Server logs contain:
    ```
    "FccmTradeCaseService.SubmitAsync: transaction {id} already has Fccm_Case_Id={caseId} — skipping."
    ```

### Resubmit endpoint

- ⏳ As an IT Admin (or user with `Transactions.Resubmit` privilege):
  `POST /api/v1/Transaction/{id}/ResubmitToFccm`
- ⏳ Verify:
  - 200 with `data.fccmCaseId` = a NEW `STUB-{guid}` (different MessageId)
  - 4 new staging row sets written
  - Old staging rows still in place (no DELETE — compliance side handles
    de-duping)
  - Server logs contain:
    ```
    "FccmTradeCaseService.ResubmitAsync: transaction {id} resubmit requested. Prior Fccm_Case_Id={prev} will be cleared."
    ```

### Privilege enforcement

- ⏳ Log in as a user WITHOUT `Transactions.Resubmit` (e.g. plain
  Analyst role)
- ⏳ `POST /Transaction/{id}/ResubmitToFccm`
- ⏳ Verify: 403 with `forbidden_privilege` envelope

### Stub-mode warning

- ⏳ With `Integration:FccmOracleConnection` empty (today's state):
- ⏳ Trigger the auto-fire path
- ⏳ Verify the warning logs once per service lifetime:
  ```
  "FccmOracleReader is in STUB mode (Integration:FccmOracleConnection unset). ..."
  ```

---

## Slice 8 Step 6 — Flag admin CRUD + scope management

**Author date**: 2026-05-21  
**Status**: ⏳ Pending

### Catalogue read

- ⏳ `GET /api/v1/Flag?page=1&pageSize=20` as IT Admin → 200 with 64
  catalogue rows from the seed migration
- ⏳ `GET /api/v1/Flag?filter=packaging&take=5` → returns flag(s)
  matching "packaging of goods is inconsistent..."
- ⏳ `GET /api/v1/Flag/{id}` for a high-scope flag (e.g. flag id of
  "packaging inconsistent") → response includes ~16 scope rows
  (one per product variant in the legacy data)

### Catalogue admin

- ⏳ `POST /api/v1/Flag` with body `{ "flagName": "Test", "flagDescription": "Test...", "flagTypeLkpId": <Manual lookup id> }` → 200 with new `flagId`
- ⏳ Verify auto-generated `flagCode` follows pattern `TBML.MRL.{8 hex chars}` (or similar based on category)
- ⏳ `PUT /api/v1/Flag/{id}` updating name/description → 200, fields updated, `Last_Updated_By` reflects caller
- ⏳ `POST /api/v1/Flag/{id}/Deactivate` → `Active_Flag` flipped to 0
- ⏳ `POST /api/v1/Flag/{id}/Activate` → `Active_Flag` flipped back to 1
- ⏳ Duplicate `flagCode` POST → 409 with `flag_code_taken` envelope

### Scope admin

- ⏳ `POST /api/v1/Flag/{flagId}/Scopes` with `{ "productId": 2, "tabId": 6 }` → 200 with new `flagScopeId`
- ⏳ Repeat the same scope → 409 with `flag_scope_exists`
- ⏳ `PUT /api/v1/Flag/Scope/{scopeId}` with `{ "activeFlag": false }` → 200, scope row's Active_Flag=0
- ⏳ `DELETE /api/v1/Flag/Scope/{scopeId}` → 200, row physically removed; verify any `TmX_Transaction_Flag` rows pointing at the parent flag are NOT affected

### Privilege enforcement

- ⏳ As a user with `Flags.View` only (no `Flags.Manage`):
  - `GET /api/v1/Flag` → 200
  - `POST /api/v1/Flag` → 403 `forbidden_privilege`
- ⏳ As a user with neither privilege:
  - `GET /api/v1/Flag` → 403 `forbidden_privilege`

---

## Slice 8 Step 5 — Flag stack EF + Application + API

**Author date**: 2026-05-20  
**Status**: ⏳ Pending

### Embedded read on transaction detail

- ⏳ `GET /api/v1/Transaction/{id}` on a backfilled transaction → response carries `flags` array
- ⏳ Backfilled transactions show prior flag ticks with `setBy = "Backfill (Slice 8 migration)"` and `setDate = transaction.createdDate`
- ⏳ Flag array shape matches the `TransactionFlagDto` spec (flagScopeId, flagId, flagCode, flagDescription, isFlagged, etc.)
- ⏳ Order is by `(tabId, sortOrder, flagName)`

### Standalone flag read

- ⏳ `GET /api/v1/Transaction/{id}/Flags` returns identical array to the embedded version, just without the surrounding transaction payload

### Create-flow flag reads

- ⏳ `GET /api/v1/Flag/Product/2` returns ~448 flag rows (all scopes for Import) with `isFlagged=false` and null tx state
- ⏳ `GET /api/v1/Flag/Product/2/Tab/6` returns just the flags for tab 6 (~25 rows for ILF)

### Flag write (via PUT /Transaction/{id})

- ⏳ `PUT /api/v1/Transaction/{id}` with `flags: [{ flagId: X, isFlagged: true, analystNotes: "test" }]` → 200, response shows the flag now ticked with attribution from caller's JWT
- ⏳ Verify `TmX_Transaction_Flag` row inserted/updated
- ⏳ Verify `TmX_Transaction_Flag_History` row added with `Change_Type_Lkp` matching "SET" and `Changed_By` matching caller
- ⏳ Untick the same flag → second history row with "CLEARED"
- ⏳ Change notes without changing state → history row with "NOTES_UPDATED" only
- ⏳ Send an unknown `flagId` → handler throws (verify error returned cleanly)

### Stats endpoint

- ⏳ `GET /api/v1/Flag/Stats/TopFlagged?take=5` returns the top 5 flags by `flaggedTransactionCount` — should roughly match the migration's diagnostic output (packaging inconsistent ~73, item incompatible ~58, etc.)
- ⏳ With `from`/`to` filter → result narrows to that window
- ⏳ With `productId=2` → results limited to Import transactions

### Document upload/download

- ⏳ `POST /api/v1/Document` (multipart) with a PDF file using curl:
  ```bash
  curl -X POST "https://localhost:5081/api/v1/Document?subfolderHint=flag-evidence" \
       -H "Authorization: Bearer <jwt>" \
       -F "file=@C:\path\to\test.pdf"
  ```
  Returns `{ documentId, originalFileName, mimeType, fileSizeBytes, sha256Hash }`
- ⏳ Verify file landed at `{ContentRoot}/ICBC_Data/flag-evidence/2026/05/<guid>.pdf`
- ⏳ Verify `TmX_Document` row with `Active_Flag=1`, hash populated, Storage_Provider_Lkp_ID points at LOCAL_DISK
- ⏳ `GET /api/v1/Document/{id}` → returns the binary stream with original filename in Content-Disposition

### FormDefinition MRL exclusion

- ⏳ `GET /api/v1/Product/2/FormDefinition?culture=en`
- ⏳ Verify: NO `fieldName` matching `*MRL*` in any tab's fields array (MRL filter applied)
- ⏳ Verify: regular `checkbox_file` fields (real document uploads) ARE still present

---

## Slice 8 (migrations) — Flag catalogue seed + backfill

**Status**: ✅ Verified per the migration smoke test output (64 catalogue / 488 scope / 703 tx-flag / 703 history rows)

---

## Slice 7 — Reports (HTML / PDF / Excel)

**Status**: ✅ Verified end-to-end per prior smoke test

---

## Operational / one-off items still pending

- ⏳ Delete stub controller files (post-consolidation):
  ```powershell
  Remove-Item "D:\ICBC - Latest\FSI.Trade.Compliance\Services\FSI.Trade.Compliance.API\Controllers\CompanyBranchesController.cs"
  Remove-Item "D:\ICBC - Latest\FSI.Trade.Compliance\Services\FSI.Trade.Compliance.API\Controllers\RolesController.cs"
  ```

- ⏳ Initial commit + push to `https://github.com/ikhan-tlx/fsi-trade-backend.git`
  (commands documented in earlier turn — PowerShell setup; needs your
  GitHub PAT for the push)

- ⏳ Recreate `FE_CHANGES_REQUIRED.md` if FE-engineer-facing docs are
  still needed. Document went missing between sessions — recent Slice
  7/8/8.5/9 FE-impact notes weren't carried forward into a replacement
  doc.

- ⏳ Custom `IOperationFilter` for `POST /Document` upload so it
  reappears in Swagger UI (today it's hidden via `[ApiExplorerSettings(IgnoreApi = true)]`)

- ⏳ Oracle wiring for the FCCM trade-case service (Slice 9 Phase 2b):
  - Add `<PackageReference Include="Oracle.ManagedDataAccess.Core" Version="23.6.0" />` to `Infrastructure.csproj`
  - Replace stub bodies in `FccmOracleReader.ExecuteCreateEventDemoProcAsync` + `InsertTransFromRepoAsync` with real `OracleConnection` calls (pseudocode in their XML docs)
  - Set `Integration:FccmOracleConnection` to a real Oracle connection string

- ⏳ Workflow scheme — add an "Application Cancelled" terminal state to
  the ImportWF scheme (BACKLOG entry — currently `POST /Transaction/{id}/Cancel`
  clears the inbox but workflow state doesn't actually move to a cancelled state)

---

## How to use this doc

When you do a sanity pass:

1. Pick a slice from the top section
2. Work through the items in order (pre-reqs first)
3. Tick `⏳` → `✅` when verified, or `❌` + a one-line reason if it fails
4. Move fully-verified slice sections down below the "Verified" divider
5. Add new items at the top whenever new code lands without sanity coverage

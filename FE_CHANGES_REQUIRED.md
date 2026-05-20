# FE Changes Required

Single source of truth for FE-side changes driven by backend work in
`FSI.Trade.Compliance`. The FE engineer works from this file: pick the
next pending entry, apply the listed edits, mark it Done.

## Rules of engagement

- **Backend side does not modify any file in `tmx-finance-frontend-revamp/`.**
  This file is the only place FE changes are noted. The FE engineer is the
  sole owner of the FE codebase, including `PROJECT.md`.
- The FE engineer updates `tmx-finance-frontend-revamp/PROJECT.md` (the FE
  spec) themselves as part of applying each entry below.

## Conventions

- **Status** — `⏳ Pending` → `🔄 In progress` → `✅ Done`. Mark as you go.
- **Trigger** — what changed on the backend (file path / brief reason).
- **Action** — concrete file edits in the FE repo (`tmx-finance-frontend-revamp/`).
- **Verify** — how to know the change works after deploy.
- **Does NOT change** — explicit list of things that stay the same, to avoid
  collateral edits.
- **Spec update (FE engineer to apply)** — the section(s) of `PROJECT.md`
  the FE engineer should update so the spec stays in sync.
- New entries go at the **top** (newest first).

---

## 2026-05-20 — Slice 8 — Flag catalogue (read embedded, write embedded, attachments)

**Status**: ⏳ Pending

**Trigger**: Slice 8 extracts the "Manual Red Flags" out of the dynamic
form's `checkbox_file` fields (legacy: TmX_Tenant_Field_Setup +
UDF_Data JSON keys) into a first-class catalogue with audit history,
on-the-fly stats, and generic-document evidence attachments.

**Per-transaction flags** — the embedded read + write path:

1. **`GET /api/v1/Transaction/{id}`** — response now carries a new top-level
   `flags` array on the detail payload. Flat shape (no tab grouping —
   FE filters by `tabId` client-side):

   ```jsonc
   {
     "status": { "code": 200, "message": "OK" },
     "data": {
       "transactionId": 4333,
       // ...existing header/udf/customers/etc...
       "flags": [
         {
           "flagScopeId": 88,
           "productId": 2,
           "tabId": 6,
           "sortOrder": 555,
           "legacyFieldName": "ILFMRL1",   // bridge during transition

           "flagId": 41,
           "flagCode": "TBML.MRL.F49D94BD",
           "flagName": "The description of goods on the Goods Declaration...",
           "flagDescription": "<full indicator text>",
           "flagTypeLkpId": 901,            // FLAG_TYPE lookup (Manual / Automated)
           "flagCategoryLkpId": 905,        // FLAG_CATEGORY (TBML / KYC / ...)
           "severityLkpId": 912,            // FLAG_SEVERITY (Medium by default)
           "defaultWeight": 1.00,
           "requiresEvidence": true,

           "transactionFlagId": null,       // null when never set on this txn
           "isFlagged": false,
           "evidenceDocumentId": null,
           "evidenceFileName": null,
           "analystNotes": null,
           "setBy": null,
           "setDate": null
         }
         // ...one entry per active scope row for the transaction's product...
       ]
     }
   }
   ```

   - **Flat list, every applicable scope returned** — flags without a
     transaction-flag row come back with `isFlagged=false` and null
     attribution. Lets the FE render the full panel from a single
     payload.
   - **All catalogue + scope columns inlined** — FE doesn't need a
     second roundtrip to render the indicator text or grouping.
     `tabId` is the FE's group key when rendering tab-wise.

2. **`PUT /api/v1/Transaction/{id}`** — request body has a new `flags` array.
   Saved as part of the existing save flow (no separate flag-save
   button per FSI direction):

   ```jsonc
   {
     "transactionDate": "2026-05-20",
     // ...existing fields...
     "flags": [
       { "flagId": 41, "isFlagged": true,  "evidenceDocumentId": 1234, "analystNotes": "Verified against shipping doc XYZ" },
       { "flagId": 42, "isFlagged": false, "evidenceDocumentId": null, "analystNotes": null }
     ]
   }
   ```

   - **Partial-update by Flag_ID** — only send what the analyst changed.
     Flags not in the array are LEFT ALONE (no implicit clear).
   - **Diff + history is automatic** — the backend compares against
     the current `TmX_Transaction_Flag` row and emits one
     `TmX_Transaction_Flag_History` row per dimension that changed
     (state / notes / evidence).
   - **Unknown Flag_ID rejected** — 400 with diagnostic if the FE
     sends a flag_id not in the catalogue.

**Standalone read endpoint** — for stats / admin / integration callers
that only need the flag panel without the rest of the transaction:

3. **`GET /api/v1/Transaction/{id}/Flags`** — returns just the `flags` array
   from above, wrapped in `ResponseViewModel<IReadOnlyList<TransactionFlagDto>>`.
   Same projection as the embedded list, so identical shape per row.

**Stats endpoint** — top-N flags by transaction count over a period:

4. **`GET /api/v1/Flag/Stats/TopFlagged?from=&to=&productId=&take=10`** —
   on-the-fly aggregation (no materialised views):

   ```jsonc
   {
     "status": { "code": 200, "message": "OK" },
     "data": [
       {
         "flagId": 31,
         "flagCode": "TBML.MRL.C8CE048A",
         "flagName": "The packaging of goods is inconsistent with the commodity...",
         "flagDescription": "<full text>",
         "severityLkpId": 912,
         "flagCategoryLkpId": 905,
         "defaultWeight": 1.00,
         "flaggedTransactionCount": 73,
         "weightedScore": 73.00
       }
     ]
   }
   ```

   - `from` / `to` filter on `TmX_Transaction_Flag.Set_Date`.
   - `productId` scopes to one product's transactions.
   - `take` defaults to 10; clamped to 100 max server-side.

**Evidence attachments** — generic document endpoints:

5. **`POST /api/v1/Document`** — multipart upload. Single file per
   request, form-field name `file`. Optional form field
   `subfolderHint` (default `"flag-evidence"`) groups uploads
   server-side. Returns:

   ```jsonc
   {
     "status": { "code": 200, "message": "OK" },
     "data": {
       "documentId": 1234,
       "originalFileName": "shipping-doc.pdf",
       "storedFileName": "ab12cd34ef56....pdf",
       "mimeType": "application/pdf",
       "fileSizeBytes": 245678,
       "sha256Hash": "AB12...EF56"
     }
   }
   ```

   - 50MB request size cap per upload.
   - File lands under `<deploy-root>/ICBC_Data/flag-evidence/<yyyy>/<MM>/<guid>.<ext>` on the server.
   - `sha256Hash` returned for client-side integrity verification.

6. **`GET /api/v1/Document/{id}`** — binary stream with
   `Content-Disposition: attachment; filename="<original>"` and the
   `Mime-Type` captured on upload. Use this to drive any "download
   evidence" UI from the flag panel.

**FE flag-panel flow** (recommended sequence):

```
1. Open transaction        → GET /Transaction/{id}            (flags embedded)
2. User ticks flag #41,
   chooses a PDF as proof  → POST /Document (multipart)        returns documentId
3. User clicks Save        → PUT /Transaction/{id}             (flags array w/ evidenceDocumentId=...)
4. Server renders          → returns updated transaction detail with new flag state
5. To download evidence    → GET /Document/{evidenceDocumentId}
```

**Action in FE**:

- **Render flags in the existing transaction detail page.** Group by
  `tabId` to match the current form's "Manual Redflags" tab.
- **Remove the legacy `checkbox_file` rendering** for fields whose
  `FieldTableName=TmxTransactionDetail[]` AND `FieldName LIKE *MRL*`.
  These are now driven by the new flag panel; rendering them as
  generic checkbox_file fields would create a duplicate UI. Other
  `checkbox_file` fields (non-MRL) keep their existing rendering.
- **Wire upload-then-save** for evidence: separate `POST /Document`
  call BEFORE the `PUT /Transaction/{id}`. Hold the returned
  `documentId` until save fires.
- **Optionally surface stats panel** at the analyst dashboard level
  using `/Flag/Stats/TopFlagged` — picks a product + period and
  shows top-10 flags as a bar chart.

**Verify**:

- Open a backfilled transaction (e.g. transaction whose UDF_Data
  carries `ILFMRL3=1`) → flag panel shows the matching flag ticked
  with `setBy="Backfill (Slice 8 migration)"`.
- Tick a new flag, upload a PDF, save → response payload reflects
  `isFlagged=true`, `evidenceFileName="<your filename>"`, `setBy=<your user>`.
- Untick the same flag, save → history grows by one `Cleared` row.
- Hit `/Flag/Stats/TopFlagged?take=5` → top 5 flags ranked by count.

**Does NOT change**:

- `TmX_Tenant_Field_Setup` rows for the MRL fields are still in place
  (legacy backend keeps reading them). The new backend doesn't read
  them during transaction-detail render — it reads from
  `TmX_Flag_Scope` exclusively.
- Existing `UDF_Data` content is untouched. Old transactions saved
  before Slice 8 keep their JSON keys; new transactions get the keys
  written too IF the FE keeps the legacy form fields visible (which
  per the action above, it shouldn't for MRL fields).
- The form-definition `GET /Product/{id}/FormDefinition` still returns
  every field including the MRL ones — the FE filters them out
  client-side rather than us excluding them server-side. This keeps
  product-form changes backwards-compatible during the transition.

**Operational note (BACKEND)**: the `Documents:BasePath` config in
`appsettings.json` resolves relative to `ContentRootPath`. In dev that's
the API project folder; in prod set this to an absolute path
(`/var/icbc/ICBC_Data` or similar) before running uploads.

---

## 2026-05-18 — Slice 7 — Reports (HTML / PDF / Excel) + companion LOVs

**Status**: ⏳ Pending

**Trigger**: Slice 7 ships the full Reports stack — same three URLs the
legacy AngularJS `ReportService` used, same verbs (POST for HTML, PUT
for the two binary endpoints), wrapped in the standard
`ResponseViewModel<T>` envelope for the HTML one and streamed raw for
PDF/XLSX. Three companion LOVs land alongside (branches, roles, products)
because the report filter panel needs them.

**Endpoints:**

1. **`POST /api/v1/Report/ReportHTML`** — runs the report's SP, renders
   the matching Liquid template, returns rendered HTML. Body matches
   legacy:

   ```json
   {
     "ReportName": "Daily Volume Report",
     "ReportVisibleName": "Daily Volume Report",
     "StoredProcedure": "sp_GetDailyVolumeReport",
     "Arguments": { "FromDate": "2026-05-01", "ToDate": "2026-05-17" },
     "PageOrientation": "P"
   }
   ```

   Response shape:

   ```json
   {
     "status": { "code": 200, "message": "OK" },
     "data": {
       "html": "<html>...</html>",
       "reportName": "Daily Volume Report",
       "reportVisibleName": "Daily Volume Report",
       "rowCount": 42
     }
   }
   ```

   Errors surface as standard `ResponseViewModel` shape:
   - 400 `validation_failed` — SP not on TmX_Lookup REPORT_TYPE allowlist
   - 404 `report_template_not_found` — no TmX_Template row for ReportName
   - 404 `report_template_empty` — Template_Text column is null/empty

2. **`PUT /api/v1/Report/GeneratePdfFromHtml`** — converts pre-rendered
   HTML to PDF bytes. Body:

   ```json
   {
     "ReportVisibleName": "Daily Volume Report",
     "HTML": "<html>...</html>",
     "PageOrientation": "L"
   }
   ```

   Response: `application/pdf` binary stream with
   `Content-Disposition: attachment; filename="Daily Volume Report-2026-05-18.pdf"`.

   `PageOrientation` accepts `"L"` / `"Landscape"` for landscape;
   anything else is portrait. Matches legacy convention.

3. **`PUT /api/v1/Report/ReportExcel`** — runs the SP fresh (no template
   needed) and streams a single-sheet XLSX. Body matches `ReportHTML`'s
   shape minus the HTML field. Response: XLSX binary stream with
   `Content-Disposition: attachment; filename="<ReportVisibleName>-<yyyy-MM-dd>.xlsx"`.

**Companion LOVs** (the FE's `business-reports-api.ts` already calls
these three URLs):

4. **`GET /api/v1/CompanyBranches`** — ALL effective branches (no
   user-scoping). Distinct from `GET /api/v1/CompanyBranch/lov` (Slice
   6.5) which user-scopes. Response:

   ```json
   {
     "status": { "code": 200, "message": "OK" },
     "data": [
       { "companyBranchId": 100, "branchCode": "TLX", "branchName": "ICBC Headoffice", "locationId": 5 }
     ]
   }
   ```

5. **`GET /api/v1/Roles`** — ALL effective roles (Active_Flag + today
   inside effective window). Distinct from `GET /api/v1/Role` (Slice 2.3)
   which is the privilege-gated CRUD list. Response:

   ```json
   {
     "status": { "code": 200, "message": "OK" },
     "data": [
       {
         "roleId": 1,
         "roleName": "Analyst ICBC",
         "roleDescription": null,
         "effectiveStartDate": "2020-01-01T00:00:00",
         "effectiveEndDate": "2099-12-31T00:00:00"
       }
     ]
   }
   ```

6. **`GET /api/v1/Product/ActiveLov`** — active products in the legacy
   LOV shape (`lookupId / visibleValue / hiddenValue / description`).
   Distinct from `GET /api/v1/Product/list` (Slice 6.5) which uses the
   product-domain shape. Response:

   ```json
   {
     "status": { "code": 200, "message": "OK" },
     "data": [
       { "lookupId": 2, "visibleValue": "IMPORT", "hiddenValue": "2", "description": null }
     ]
   }
   ```

**Action in FE** (`src/features/reports/businessReports/api/business-reports-api.ts`):

- The three LOV `transformResponse` mappers currently look only for
  PascalCase keys (`r.CompanyBranchId`, `r.RoleId`, `r.LookupId`,
  `r.VisibleValue`, etc.). The new backend uses **camelCase** wire
  format. Update each `transformResponse` to read camelCase variants
  first, falling through to PascalCase only if needed:
  ```ts
  LookupId: Number(r.companyBranchId ?? r.CompanyBranchId ?? r.LookupId ?? 0),
  VisibleValue: String(r.branchName ?? r.BranchName ?? r.VisibleValue ?? ""),
  ```
- `unwrapList` already handles both `raw.data` and `raw.Data` envelopes;
  no change needed there.
- `runReportHtml` mutation — payload unchanged; response shape now nests
  the HTML inside `data.html` rather than at `data.html` directly via
  legacy unwrap. Read it as `response.data?.html`.
- `downloadReport(...)` helper — no change needed. PUT verb,
  Content-Type, headers, blob handling all carry over.

**Verify**:

- Open Reports page — the three filter dropdowns (Branch, Role, Product)
  populate.
- Pick a report from the report-type dropdown (already wired through the
  Lookup blob — no change there), supply filters, click "Generate".
- HTML preview renders inside the page.
- Click PDF — file downloads with `<ReportName>-<date>.pdf` filename.
- Click Excel — file downloads with `.xlsx` extension; open it, header
  row is bold + frozen, columns auto-sized.
- Try injecting an unknown SP name into the request body — backend
  returns 400 with `validation_failed` code.

**Does NOT change**:

- The legacy `Lookup` blob — `REPORT_TYPE` rows still drive which
  reports exist; the FE still pulls them out of
  `GET /api/v1/Lookup/{culture}` and filters by `LookupType === "REPORT_TYPE"`.
- The user-scoped `GET /api/v1/CompanyBranch/lov` (Slice 6.5) — that
  endpoint stays as the transaction-creation branch LOV. The new
  `CompanyBranches` is additive, for Reports specifically.
- Authentication — every endpoint still requires Bearer JWT.

**One-off operational note (BACKEND ENGINEERING)**: PuppeteerSharp
downloads its Chromium revision (~150 MB) on first PDF request. The
download path is `~/.local-chromium` on Linux and `%LOCALAPPDATA%\…` on
Windows; in container/CI environments, pre-fetch it during image build
to avoid a slow first request. See `PuppeteerReportPdfGenerator.cs`.

---

## 2026-05-11 — Slice 6.5 — Stepper-support endpoints

**Status**: ⏳ Pending

**Trigger**: Slice 6.5 ships three FE-init / wizard endpoints. All three
have URL parity with the legacy paths the FE init API references already,
so the URLs themselves don't change — only the response envelope (now
the standard `ResponseViewModel<T>`) and field casing (camelCase).

**Endpoints:**

1. **`GET /api/v1/Product/list`** — active product catalog. Response shape:

   ```json
   {
     "status": { "code": 200, "message": "OK" },
     "data": [
       {
         "productId": 2,
         "productCode": "IMPORT",
         "productName": "IMPORT",
         "productDescription": null,
         "productTypeLkp": 555,
         "workflowSchemeCode": "ImportWF",
         "currencyId": 1
       }
     ]
   }
   ```

   The FE's `getLoanProducts` query in
   `src/features/tradeRepository/api/trade-repository-init-api.ts` already
   points at this URL — only the response transform needs to read
   `data` (instead of expecting the raw array) and adopt camelCase.

   **Branch-scoped filtering NOT applied** — every active product is
   returned to every authenticated caller. The downstream
   `POST /Transaction` still validates that the caller is mapped to the
   chosen branch, so security isn't compromised. Branch-product filtering
   (legacy used `TmX_Branch_Products_Mapping`) lands in a future slice
   if it becomes necessary.

2. **`GET /api/v1/CompanyBranch/lov`** — caller's effective branch mappings:

   ```json
   {
     "status": { "code": 200, "message": "OK" },
     "data": [
       { "companyBranchId": 100, "branchCode": "TLX", "branchName": "ICBC Headoffice", "locationId": 5 }
     ]
   }
   ```

   **Tighter than legacy** — legacy returned ALL effective branches; the
   new backend returns only branches the AUTHENTICATED CALLER is mapped to
   (via `TmX_Company_Branch_Users_Mapping`, effective today). Aligns with
   how the create-transaction endpoint validates branches. The FE
   dropdown should now only show usable options.

3. **`GET /api/v1/Lookup/GetByType?type=<lookupType>&culture=<en|ar|...>`** —
   single-type slice of the lookup catalog:

   ```
   GET /api/v1/Lookup/GetByType?type=ProductTypes&culture=en
   →
   {
     "status": { "code": 200, "message": "OK" },
     "data": [ { "lookupId": 555, "lookupType": "ProductTypes", "visibleValue": "Draft", "hiddenValue": "Draft", ... } ]
   }
   ```

   Companion to the existing `GET /api/v1/Lookup/{culture}` which returns
   the whole catalog. Use this narrower form when only one type is
   needed (e.g. populating a "Product Type" dropdown).

   The FE's `getProductTypesLkp` query already points here — same URL
   convention. `culture` query parameter is optional, defaults to `en`.

**Action in FE:**

- `trade-repository-init-api.ts` — adjust the three response transforms to
  read `data` from the envelope and camelCase the field names. URLs are
  unchanged.
- For the branch dropdown — confirm the FE filters / displays
  `branchName` (not `branchCode` alone) and uses `companyBranchId` as
  the chosen value. The FE then sends `companyBranchId` in the
  `POST /Transaction` payload (Slice 6.2).

**Verify**:

- Open the Add page — product dropdown populates with the active product
  list (you should see IMPORT, EXPORT, etc.).
- Branch dropdown populates with only the branches the logged-in user is
  mapped to. Log in as a user mapped to only "ICBC Headoffice" — confirm
  only that branch appears.
- Product Type dropdown (or whatever uses `getProductTypesLkp`) populates
  from `Lookup/GetByType`.
- Submit the create flow — transaction lands successfully against the
  selected product + branch.

**Does NOT change**:

- The existing `GET /api/v1/Lookup/{culture}` full-catalog endpoint
  (Slice 3) is unchanged.
- Authentication and authorisation — every endpoint requires Bearer JWT.

---

## 2026-05-11 — Slice 6.4 — Transaction cancel endpoint

**Status**: ⏳ Pending

**Trigger**: Slice 6 Step 5 ships `POST /api/v1/Transaction/{id}/Cancel`.
Replaces legacy `PUT /api/v1/Transaction/CancelById/{transactionId}` with a
RESTful URL (id in path) and an optional reason body.

**Action** in `tmx-finance-frontend-revamp/` — wherever the cancel-transaction
action is wired (likely a "Cancel Transaction" button on the edit page or
in a row context menu):

1. Change URL + method:
   - Was: `PUT /api/v1/Transaction/CancelById/{transactionId}` (no body)
   - Now: `POST /api/v1/Transaction/{id}/Cancel` with optional body

2. Body (all fields optional):

   ```json
   {
     "reason": "Customer withdrew application"
   }
   ```

   If no reason is captured by the FE, the body can be omitted entirely
   (RTK Query: pass `undefined` or `{}` as the body).

3. Response shape: the canonical `TransactionDetailDto` — same as GET
   `/Transaction/{id}`. After a successful cancel, the FE can bind the
   response directly to refresh the page: `Transaction_Status_Lkp` flips
   to the "Application Cancelled" lookup id, `workflow.currentState`
   reflects the new state, `workflow.commands` is typically empty
   (cancelled is a terminal state with no outbound transitions for
   normal actors).

4. **Idempotent**: calling cancel on an already-cancelled transaction
   returns 200 with the same response shape and no state change. Safe
   to invoke from a double-click without a confirmation guard.

**Verify**:

- Pick an in-progress transaction (state != "Application Cancelled"), POST
  the cancel endpoint. Response 200, status flips, workflow state moves
  to the cancelled terminal.
- Confirm via `SELECT Transaction_Status_Lkp, ... FROM dbo.TmX_Transaction
  WHERE Transaction_Id = X` and the corresponding TmX_Lookup row that
  `Visible_Value = 'Application Cancelled'`.
- Confirm `dbo.WorkflowProcessInstance.StateName` for the transaction's
  ProcessInstanceId is at the cancelled terminal state.
- Call cancel a second time — same 200, no extra DB writes (idempotent).

**Does NOT change**:

- The transaction GET endpoint — already shipped.
- The save/update endpoint — `PUT /api/v1/Transaction/{id}` is unchanged.
- The workflow execute endpoint — `PUT /api/v1/Workflow/Process/{id}/Execute`
  is unchanged. Cancel is a forced state mutation, NOT a normal command;
  it goes through SetState, not ExecuteCommand.

---

## 2026-05-11 — Slice 6.3 — Transaction update (save-draft) endpoint

**Status**: ⏳ Pending

**Trigger**: Slice 6 Step 4 ships `PUT /api/v1/Transaction/{id}` —
the save-draft endpoint. Replaces legacy `PUT /api/v1/Transaction` (which
took the id inside the body).

**Action** in `tmx-finance-frontend-revamp/src/features/tradeRepository/edit/trade-repository-edit-api.ts`:

1. Change URL + method binding on `updateTradeTransaction`:
   - Was: `PUT /api/v1/Transaction` with `{ TransactionId, ... }` in body
   - Now: `PUT /api/v1/Transaction/{id}` — id in the URL, body shape below

2. Change request body to camelCase + clean shape:

   ```json
   {
     "transactionDate": "2026-05-12T00:00:00",
     "currencyId": 1,
     "transactionTypeLkp": 555,
     "transactionStatusLkp": 555,
     "clientReferenceNumber": "TEST-001",
     "udfData": { "fieldName1": "value1", ... },     // PARSED object, not stringified
     "customer": {
       "customerMasterId": 12345,                     // present when updating existing snapshot
       "customerCode": "12345",
       "customerName": "ABC Trading Co",
       "nationalIdentifierValue": null,
       "udfData": { ... },                            // customer-level UDF
       "bankingDetails": [
         {
           "customerBankingDetailId": 999,             // present when updating
           "bankAccountNumber": "...",
           "branchCode": "...",
           "udfData": { ... }
         }
       ]
     },
     "beneficiaries": [
       { "id": 100, "udfData": { ... } },              // id present → UPDATE
       { "udfData": { ... } }                           // no id → INSERT
     ],
     "stakeholders": [
       { "id": 200, "udfData": { ... } }
     ]
   }
   ```

3. **Child-collection diff semantics** — important for the FE renderer:
   - Items in the request that have an `id` get UPDATEd in place.
   - Items in the request WITHOUT an `id` get INSERTed (new beneficiary, etc.).
   - Existing rows in the DB whose `id` is NOT in the request are DELETEd.
   - This means: when the user removes a beneficiary from the form, simply
     omit it from the next save — the backend will delete it.

4. **UDF data is a parsed JSON object on the wire** (both directions). Don't
   `JSON.stringify(udfData)` before sending; the backend serializes to
   nvarchar before persisting. Symmetric with the GET response shape from
   Slice 6.1.

5. Response shape is the canonical `TransactionDetailDto` — same as GET
   `/Transaction/{id}`. So save → response → can be re-bound to the form
   state without an extra refetch.

**Save-then-execute pattern**: when the user clicks a workflow command
(e.g. SendToAuthorizer), the FE's current `dynamicForm.tsx` already does
`updateTradeTransaction(payload)` followed by `executeTradeWorkflow(...)`.
That pattern continues to work — first call hits the new
`PUT /Transaction/{id}`, second call hits
`PUT /api/v1/Workflow/Process/{processInstanceId}/Execute` (Slice 5).

**Verify**:

- Open an existing transaction in the edit form, modify a UDF field, click Save.
- Backend response is 200 with the refreshed `TransactionDetailDto`.
- Reload the page (or re-fetch) — the changed field persisted.
- Add a new beneficiary row in the form, save, reload — new row should have
  a server-issued `id`.
- Remove an existing beneficiary, save, reload — row is gone from the response.
- `inboxName`, `inboxUserId`, `currentState`, `workflow.commands` are still
  populated from the workflow snapshot (unchanged by save).

**Does NOT change**:

- The workflow execute endpoint — still `PUT /api/v1/Workflow/Process/{id}/Execute`.
- The transaction-detail GET endpoint — already shipped in Slice 6.1.
- The save-then-execute orchestration in `dynamicForm.tsx` — just the URL/body
  shape changes on the first call.

---

## 2026-05-11 — Slice 6.2 — Transaction create endpoint: cleaner shape

**Status**: ⏳ Pending

**Trigger**: Slice 6 Step 3 ships `POST /api/v1/Transaction` (replaces
`POST /api/v1/Transaction/Create`). Minimal payload, sync workflow init,
response shape matches `GET /api/v1/Transaction/{id}`.

**Action** in `tmx-finance-frontend-revamp/src/features/tradeRepository/api/trade-repository-add-api.ts`:

1. Change URL on `createNewTradeTransaction`:
   - Was: `POST /api/v1/Transaction/Create`
   - Now: `POST /api/v1/Transaction`

2. Change request body shape (camelCase, single customer object instead of array):

   ```json
   {
     "productId": 2,
     "companyBranchId": 100,           // FE should always send this — auto-resolve removed
     "transactionDate": "2026-05-11T00:00:00",
     "clientReferenceNumber": "12345",
     "transactionTypeLkp": 555,
     "transactionStatusLkp": 555,
     "customer": {
       "customerCode": "12345",
       "customerName": "ABC Trading Co",
       "nationalIdentifierValue": null,
       "riskScore": 0.45
     }
   }
   ```

   Was:
   ```json
   {
     "ClientReferenceNumber": "12345",
     "TransactionDate": "...",
     "ProductId": 2,
     "TmxCustomerMaster": [
       { "CustomerCode": "12345", "CustomerName": "...", "RiskScore": 0.45 }
     ],
     "TransactionTypeLkp": 555,
     "TransactionStatusLkp": 555
   }
   ```

3. Response shape: the same `TransactionDetailDto` envelope used by
   `GET /api/v1/Transaction/{id}` (per Slice 6.1 entry below). The FE's
   post-create navigation reads `data.transactionId` — currently looking
   at `data.TransactionId` / `TransactionId`; both now camelCase.

   Status code is **201 Created** (was 200 in legacy). RTK Query's default
   error handling treats 2xx as success, so this is transparent unless
   the FE specifically checks for 200.

**Verify**:

- Open the Trade Repository "Add" page, complete the customer-lookup +
  product + branch + date steps, hit Save.
- Backend creates the transaction row + customer snapshot + initiates
  the workflow synchronously. Response includes `workflow.commands` for
  the initial state.
- FE navigates to `loadDynamicForm(transactionId)` with the new ID. The
  edit page loads as before.

**Does NOT change**:

- The Add page flow (customer lookup via KYC etc.) — that's FE-internal.
- `GET /api/v1/Integration/GetKYC/{id}` / `GetCustomerByCustomerId` —
  those are existing endpoints, no change.
- Branch selection UX — the FE already prompts for a branch; the API
  now requires it (was optional with auto-resolve in legacy).

**Spec update (FE engineer to apply)** in `tmx-finance-frontend-revamp/PROJECT.md`:

- The doc currently references `/api/v1/LoanApplication/...` URLs (Section
  8.2/8.3). These are stale — the actual FE code uses `/api/v1/Transaction/...`
  for trade-repo. Update the doc to match the source code (`/Transaction`
  for everything in the trade-repo grid + add + edit features).
- Add the new create-payload shape to Section 8.3 (Trade Repository).

---

## 2026-05-11 — Slice 6.1 — Transaction edit page: three new endpoints

**Status**: ⏳ Pending

**Trigger**: Slice 6 step 2 ships three endpoints powering the Trade
Repository edit page. The old endpoint surface is being retired.

**Action** in `tmx-finance-frontend-revamp/src/features/tradeRepository/edit/trade-repository-edit-api.ts`:

1. Replace `getTradeRepositoryEdit` URL:
   - Was: `GET /api/v1/Transaction/${id}`
   - Now: same path; response shape changes (see below).

2. Replace `getTabEntityMapping` URL:
   - Was: `GET /api/v1/Entity/TabEntityMapping`
   - Now: `GET /api/v1/Product/Tabs/Mappings`
   - Response now in the standard envelope: `{ status, data: [{ id, tenantId, entityId, productId, tabId, parentTabId?, sortOrder? }] }`. Field casing is camelCase.

3. Replace `getFieldsByProduct` URL:
   - Was: `GET /api/v1/TenantFieldSetup/GetFieldsByProduct/${productId}/${culture}`
   - Now: `GET /api/v1/Product/${productId}/FormDefinition?culture=${culture}`
   - Response shape: `{ status, data: { productId, culture, tabs: [{ tabId, parentTabId?, tabName, localeLabel, localeId, hiddenValue, sortOrder, fields: [...], tabs: [...] }] } }`. Tabs already nested by the backend; the recursive `tabs` array contains child tabs. Fields are camelCase versions of the legacy model.

4. The new `Transaction/{id}` response shape:
   ```
   { status, data: {
       transactionId, tenantId, productId, transactionNumber, ...header...,
       udfData: { ...parsed JSON object from TmX_Transaction_Detail.UDF_Data... },
       customers: [{ customerMasterId, customerName, ..., udfData: {...},
                     bankingDetails: [{ customerBankingDetailId, bankAccountNumber, ..., udfData: {...} }] }],
       beneficiaries: [{ id, udfData: {...} }],
       stakeholders:  [{ id, udfData: {...} }],
       checklist:     [{ applicationChecklistId, tabId, ... }],
       remarks:       [{ applicationRemarkId, comments, userId, createdDate, ... }],
       deviations:    [{ deviationId, ruleName, ruleMessage, deviationAction, userId, ... }],
       workflow:      { processInstanceId, currentState, activityName, commands: [...] }
   } }
   ```

5. **Important — UDF parsing**: `udfData` is now a JSON OBJECT, not a JSON STRING. Remove any `JSON.parse(response.data.udfData)` calls — the FE consumes the object directly. Same for the udfData on customers / beneficiaries / stakeholders / bankingDetails.

6. **UDF parse error case**: if the backend can't parse the stored UDF JSON (corrupt legacy data), it returns `{ _udfParseError: true, raw: "<original string>" }` in place of the object. FE should check for that shape and display "Invalid UDF data — please contact admin" instead of attempting to render the form.

7. **Workflow commands** are now inline on the transaction-detail response (`workflow.commands`). Remove the separate `/Workflow/Process/{id}/Commands` call from the edit-page open path (it's still available as a standalone endpoint for partial refreshes).

8. **Localisation** on form fields: each `FormFieldDto` includes both `fieldLabel` (default — typically English) and `localeLabel` (if a localised row exists) plus the row's `localeId`. The FE picks based on its current culture. If `localeLabel` is null, fall back to `fieldLabel`.

**Verify**:

- Open the Trade Repository edit page for an existing transaction; all four tabs (Customer, Beneficiary, Stakeholder, Compliance) render with their fields, pre-populated with the saved UDF data.
- Workflow command buttons appear inline on the page (e.g. "Submit for Screening" if the transaction is in Draft state and the user has that command).
- Switching the FE culture to Arabic (if your build supports it) flips field/tab labels.

**Does NOT change**:

- The PUT update endpoint (`/api/v1/Transaction`) — step 4 will replace it; for now legacy update path remains.
- Workflow execute endpoint — already lives at `/api/v1/Workflow/Process/{id}/Execute` from Slice 5.

**Spec update (FE engineer to apply)** in `tmx-finance-frontend-revamp/PROJECT.md`:

- Replace the old `Entity/TabEntityMapping` and `TenantFieldSetup/GetFieldsByProduct/{id}/{culture}` references with the new `/Product/Tabs/Mappings` and `/Product/{id}/FormDefinition?culture=` URLs.
- Note that UDF columns are now parsed objects on the wire, not strings.
- Note that workflow commands are folded into the transaction-detail response.

---

## 2026-05-11 — Slice 6.0 — Trade Repository grid moves to clean `PagedQuery` shape

**Status**: ⏳ Pending

**Trigger**: New backend ships `GET /api/v1/Transaction` (Slice 6 step 1).
Replaces the legacy Kendo-shaped `DataSourceRequest` contract with a clean
`PagedQuery` shape that's reused across every grid endpoint.

**Action** in `tmx-finance-frontend-revamp/`:

1. `src/features/tradeRepository/api/trade-repository-api.ts` — rewrite
   `buildTransactionQueryParams`:
   - Drop everything Kendo-shaped: no more `filter[filters][0][field]=...`,
     no `take`/`skip` duplication, no `firstPage`.
   - Send these query params only:
     - `Page` (1-based int)
     - `PageSize` (int, 1-1000)
     - `Sort` (optional string — `"field-asc"` or `"field-desc"`,
       comma-separated for multi-sort; first token is honoured today)
     - `Filter` (optional freetext — searched across transaction number,
       customer name, national identifier, customer code, product name)
   - Sort field whitelist for now: `transactionId`, `transactionNumber`,
     `transactionDate`, `createdDate`, `customerName`, `productName`,
     `branchName`, `currentState`, `status`. Anything else falls back to
     `createdDate-desc`.

2. `src/features/tradeRepository/api/trade-repository-api.ts` — the
   `TradeRepositoryGridResponse` shape changes:
   - Was: `{ Data, Total, AggregateResults?, Errors? }`
   - Now: `{ status, data: { items, total, page, pageSize } }` (the
     standard `ResponseViewModel<PagedResult<T>>` envelope). Update the
     RTK Query `transformResponse` accordingly.

3. `TradeRepositoryItem` field casing — all camelCase now:
   - `TransactionId` → `transactionId`
   - `TransactionNumber` → `transactionNumber`
   - `CustomerName` → `customerName`
   - `NationalIdentifierValue` → `nationalIdentifierValue`
   - `CreatorName` → `creatorName`
   - `InboxName` → `inboxName`
   - `InboxUserId` → `inboxUserId` (NEW — surfaced now)
   - `TransactionDate` → `transactionDate`
   - `ProductId` → `productId`, `ProductName` → `productName` (NEW)
   - `ProcessInstanceId` → `processInstanceId`
   - `TransactionStatusLkp` → `transactionStatusLkp`
   - `Status` → `status`
   - `CurrentState` → `currentState` (NEW)
   - `BranchName` → `branchName` (NEW)
   - `CustomerCode` → `customerCode` (NEW)
   - Drop `StatusBKColor` from the type — it was FE-derived anyway, not
     a backend field; keep the FE-side mapping unchanged.

4. Filter UX: per-column Kendo filters won't work against the new
   freetext box. Two options for the FE:
   - Switch the grid to a single search-box above the grid (recommended),
     OR
   - Wait for backend Slice 7 which adds structured column filters back.

**Verify**:

- Trade Repository page loads and shows transactions scoped to the
  caller's location-tree / branch assignments / own creations.
- Sorting by any whitelisted column toggles ascending/descending and
  the URL reflects `?Sort=...`.
- The single search box (or the structured filter for the columns the
  freetext supports) returns matching rows.
- `inboxName` shows "Multiple Users" when more than one workflow actor
  has the transaction in their inbox; otherwise shows the assigned
  user's display name; "Unassigned" stays as the FE-side fallback when
  `inboxUserId` is null and `inboxName` is also null (transaction not
  yet workflow-attached).

**Does NOT change**:

- Route in the FE router; the page URL `/trade-repository` stays.
- Authentication / JWT handling — uses the same `Authorization: Bearer`
  + `X-Device-Id` headers.

**Spec update (FE engineer to apply)** in `tmx-finance-frontend-revamp/PROJECT.md`:

- Replace the section that documents the Kendo `DataSourceRequest`
  contract for Trade Repository with the new `PagedQuery` shape.
- Note that every future grid endpoint (Audit, Roles, Users) will use
  the same shape — the helper is the single point of change.

---

## 2026-05-06 — Slice 3.0 — Lookup + Configurations endpoints (app-init)

**Status**: ⏳ Pending

### Trigger

The FE's app-init flow needs two reference reads to render after login.
Both are now live on the new backend at the same URLs the FE already calls
in `features/auth/api/app-init-api.ts`:

- `GET /api/v1/Lookup/{culture}` (line 34-35)
- `GET /api/v1/Configurations/GetUserCompanyConfigurations` (line 38-39)

Both authenticated (Bearer + X-Device-Id), no privilege gate — every user
needs them to operate the app.

### URL changes — none

The URLs match the legacy paths exactly. The FE doesn't need to edit
`app-init-api.ts` URLs.

### Response-shape changes — camelCase + standard envelope

Both endpoints now follow the `{ status, data }` envelope and camelCase
field naming (Slice 2.2 rules). The FE's existing typing was `<any, void>`
— so binding code may need updates when fields are read.

**Lookup response (was: stored-proc result, mixed casing):**

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": [
    {
      "lookupId":       1,
      "parentLookupId": null,
      "lookupType":     "Country",
      "lookupName":     "United States",
      "visibleValue":   "United States",
      "hiddenValue":    "US",
      "localeLabel":    "en-US",
      "sortOrder":      1,
      "isActive":       true
    }
    // ...
  ]
}
```

The FE's current grouping logic (group rows by `lookupType` to populate
dropdowns) keeps working — just read the camelCase field names instead
of PascalCase.

**Configurations response (was: ConfigurationsViewModel list):**

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": [
    {
      "configurationId":          42,
      "configurationKey":         "MAX_LOAN_AMOUNT",
      "configurationValue":       "1000000",
      "configurationDescription": "Per-transaction cap",
      "productId":                null,
      "timeZoneId":               null
    }
    // ...
  ]
}
```

Filtered server-side to the caller's tenant via the `tenantId` claim in
the JWT — the FE doesn't need to pass it. Cross-tenant reads aren't
allowed.

### Behaviour notes

- **Active_Flag is currently ignored on read** (per FSI direction, May 2026).
  The FE may filter `isActive === true` client-side for display.
  See `BACKLOG.md → Active_Flag handling on RBAC tables` — same convention.
- **Effective_Start_Date / Effective_End_Date** ARE filtered server-side on
  Configurations — only rows whose effective window includes "now" come back.
  No FE filter needed for stale-config hiding.
- **Culture matching for Lookup** — the new backend does a string-prefix
  match on `localeLabel` (e.g. `culture = "en"` returns rows with
  `localeLabel` starting with `en`, plus rows where `localeLabel` is null —
  those are culture-agnostic). If the live data uses `Locale_ID` joins
  through a separate locale table, we'll refine the query to match — the
  FE-visible response shape doesn't change either way.

### Verify after deploy

1. `GET /api/v1/Lookup/en` → 200 with `data: [...]` containing rows like
   `{ lookupId, lookupType, visibleValue, hiddenValue, ... }` in camelCase.
2. `GET /api/v1/Configurations/GetUserCompanyConfigurations` → 200 with
   `data: [...]` containing rows scoped to caller's tenant.
3. Without Bearer header → 401 `unauthenticated`.
4. With Bearer but no `X-Device-Id` → 400 `device_id_required`.

### Does NOT change

- The X-Device-Id header convention (Slice 1.5).
- The `{ status, data }` envelope (Slice 1).
- The 401-triggers-logout interceptor.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:
- §App init — note that both endpoints now follow the standard
  `{ status, data }` envelope and camelCase fields. URL paths unchanged.
- §Architecture — add a note that lookup and configuration data is
  tenant-scoped server-side via the JWT's `tenantId` claim.

---

## 2026-05-06 — Slice 4.0 — Integration URLs renamed (domain resources, vendor names hidden)

**Status**: ⏳ Pending. Lands with Slice 4 (Integrations) — flagging now so the
FE engineer can plan the URL changes alongside Slice 4 deployment.

### Trigger

The legacy backend exposed all upstream-facing reads under
`/api/v1/Integration/*`. That umbrella told the FE nothing about what each
endpoint did, and it leaked the legacy multi-project topology
(tmx-finance-backend → tmx-finance-integrations → Integration KYC) into
the URL surface.

The new backend follows a strict layering: **URLs use domain language
(Customer, KYC Case), upstream-vendor names (BRAINS, FCCM) live only in
Infrastructure adapters**. So the FE never sees "Brains" or "Fccm" in any
path. If ICBC swaps screening vendors next year, the FE doesn't change.

### URL changes

| FE call (legacy) | FE call (new) | Replaces / why |
|---|---|---|
| `GET /api/v1/Integration/GetKYC/${customerId}` | `GET /api/v1/Customer/${customerId}/Kyc` | KYC screening lookup. URL now expresses the domain — Customer is the resource, Kyc is its sub-resource. Vendor (BRAINS) hidden in Infrastructure (`BrainsKycScreeningService`). Response shape unchanged: `{ riskScore, customerName }` (camelCased per Slice 2.2). |
| `GET /api/v1/Integration/GetCustomerByCustomerId/${customerId}` | `GET /api/v1/Customer/${customerId}` | Full customer master record (TBAML products only). REST-shaped: Customer is the resource, GET on it returns the full representation. Response shape unchanged otherwise — full ~30-field master record. |
| (no FE call yet — server-internal in legacy) | `POST /api/v1/Kyc/Case` | NEW: explicit KYC case submission, replacing the legacy 20-second blocking poll. Returns `{ requestId, status: "AwaitingCaseId" }` immediately. The FE can choose to poll via the next URL, or background it entirely if the workflow scheme handles it server-side. |
| (no FE call yet — server-internal in legacy) | `GET /api/v1/Kyc/Case/${requestId}` | NEW: poll case status. Returns `{ status, fccmCaseId?, riskCategory?, ... }`. Status state machine: `AwaitingCaseId → CaseCreated → RiskAssessed → terminal` (or `Failed` / `Timeout`). |
| (no FE call — webhook destination) | `POST /api/v1/Kyc/Case/Callback` | Replaces `updateTransactionStatusAPIController.Post`. Server-only; FE doesn't call this. Listed for completeness. |

### Action — FE edits

**1. Trade Repository add API** (`features/tradeRepository/api/trade-repository-add-api.ts`):

```typescript
// Two endpoint URLs change. Hooks stay the same name; only the URL edits.

// was line 7
url: `/api/v1/Integration/GetKYC/${customerId}`
// becomes
url: `/api/v1/Customer/${customerId}/Kyc`

// was line 14
url: `/api/v1/Integration/GetCustomerByCustomerId/${customerId}`
// becomes
url: `/api/v1/Customer/${customerId}`
```

**2. Response shape — minor camelCase normalisation**

The legacy responses use a mix of PascalCase and camelCase. The new
backend serialises pure camelCase (Slice 2.2 envelope rules):

```typescript
// KYC lookup response — was
{ status: { ... }, data: [{ RiskScore: "Low", CustomerName: "..." }] }
// becomes
{ status: { ... }, data: [{ riskScore: "Low", customerName: "..." }] }

// Customer master response — every PascalCase field becomes camelCase
{ CustomerCode → customerCode, NationalIdentifierValue → nationalIdentifierValue, ... }
```

Update the `kycRows[0]?.RiskScore` etc. binding sites in
`trade-repository-add.tsx:610-611` to camelCase. Same for the customer
master destructuring around line 625.

**3. No new mutation needed for KYC case submission yet**

The new `/Kyc/Case` endpoints are written but the FE doesn't have to call
them directly in Slice 4. Today the workflow scheme will fire submission
internally as part of a workflow action (Slice 5/6 work). When the FE
needs explicit case-submission UI (e.g. an "Re-screen" button), the
endpoints are ready.

### Verify after deploy

1. Open Add Transaction for a KYC product. Customer ID + date filled, click
   Add. Network tab shows `GET /api/v1/Customer/${id}/Kyc` returning 200
   with `{ status, data: [{ riskScore, customerName }] }`.
2. Same flow for a TBAML product. Network tab shows
   `GET /api/v1/Customer/${id}` returning the full master record.
3. Both should fail-fast with 4xx if the customer doesn't exist (404
   `customer_not_found` per API_GUIDELINES). Currently legacy returns 200
   with a `success:0` envelope — new backend uses HTTP status codes
   correctly.

### Does NOT change

- The `{ status, data }` envelope shape.
- The X-Device-Id requirement.
- The 401-triggers-logout interceptor.
- The Transaction Create flow (`POST /api/v1/Transaction/Create`) — no URL
  change there.
- The dynamic-form schema endpoints — same paths.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:
- §Trade Repository — replace the two `Integration/*` URLs in the add
  flow with the new `Customer/{id}/Kyc` and `Customer/{id}` paths.
- §Architecture — note the layering: domain URLs (Customer, Kyc/Case),
  Application contracts (`IKycScreeningService`, `ICustomerMasterService`,
  `IKycCaseService`), Infrastructure adapters (vendor-named —
  Brains, Fccm). FE only ever sees the URLs.

---

## 2026-05-05 — Slice 2.5 — Workflow controller consolidation (URL changes for Inbox + ExecuteWf)

**Status**: ⏳ Pending. Lands in Slice 5 (workflow port) — flagging now so the
FE engineer can plan the URL changes alongside Slice 5 deployment.

### Trigger

End-to-end trace of the legacy `LoanApplicationController` revealed that the
endpoints the Inbox + Process Designer modules call are **generic workflow
plumbing** that don't depend on loan-specific code at all (`/ApplicationInbox`
queries `TmX_Application_VW` returning a generic DTO; `/GetCommandsByProcessId`
is a 3-line passthrough to `WorkflowRuntime.GetAvailableCommandsAsync`). The
new backend consolidates them onto a single dedicated `WorkflowController`.

The dynamic-controller pattern in `InboxPage.tsx:307-316` collapses to one
URL — the workflow runtime knows which scheme is running for a given
process ID, so the FE no longer needs to map work-item type to controller
name.

### URL changes

| Module | Was | Becomes |
|---|---|---|
| **Inbox** | `GET /api/v1/LoanApplication/ApplicationInbox` | `GET /api/v1/Workflow/Inbox` |
| **Inbox actions** | `GET /api/v1/LoanApplication/GetCommandsByProcessId/${processId}` | `GET /api/v1/Workflow/Process/${processId}/Commands` |
| **Inbox actions** | `PUT /api/v1/${targetApiController}/ExecuteTransactionWF` (LoanApplication / AccountApplication / Transaction) | `PUT /api/v1/Workflow/Process/${processId}/Execute` |
| **Transaction edit** | `PUT /api/v1/Transaction/ExecuteWf` | `PUT /api/v1/Workflow/Process/${processId}/Execute` (same as above) |
| **Process Designer** | `GET /api/v1/LoanApplication/Workflow` | `GET /api/v1/Workflow/Schemes` |
| **Process Designer** | `GET /api/v1/LoanApplication/Workflow/ProductMapping?schemeCode=X` | `GET /api/v1/Workflow/ProductMapping?schemeCode=X` |
| **Process Designer** | `POST /api/v1/LoanApplication/Workflow/ProductMapping` | `POST /api/v1/Workflow/ProductMapping` |
| **Process Designer** | `GET /api/v1/LoanApplication/Workflow/Designer` | `GET /api/v1/Workflow/Designer` |

### Action — FE edits

**1. Inbox API** (`features/inbox/api/inbox-api.ts`):

```typescript
// was
query: () => `/api/v1/LoanApplication/ApplicationInbox?...`
// becomes
query: () => `/api/v1/Workflow/Inbox?...`
```

Response shape stays the same — `data.items[]` with `processInstanceId`,
`applicationType`, `currentState`, etc. (Slice 2.2 envelope changes still
apply.)

**2. Inbox-actions API** (`features/inbox/api/inbox-actions-api.ts`):

Two separate edits:

```typescript
// GetCommandsByProcessId — was
query: ({processId}) => `/api/v1/LoanApplication/GetCommandsByProcessId/${processId}`
// becomes
query: ({processId}) => `/api/v1/Workflow/Process/${processId}/Commands`

// ExecuteTransactionWF — was (in InboxPage.tsx around line 318)
let targetApiController = "LoanApplication";
if (selected.ApplicationType === "AccountApplication") {
  targetApiController = "AccountApplication";
} else if (selected.ApplicationType === "Trade" || selected.ApplicationType === "TradeRepository") {
  targetApiController = "Transaction";
}
url: `/api/v1/${targetApiController}/ExecuteTransactionWF`

// becomes — DELETE the conditional, single URL
url: `/api/v1/Workflow/Process/${processId}/Execute`
```

The `ApplicationType` field stays on the inbox row (the FE may still display
it as a column in the grid), but it no longer drives URL routing.

**3. Transaction edit API** (`features/tradeRepository/api/trade-repository-edit-api.ts`):

```typescript
// was
url: `/api/v1/Transaction/ExecuteWf`
// becomes
url: `/api/v1/Workflow/Process/${processId}/Execute`
```

Pass `processInstanceId` from the loaded transaction.

**4. Process Designer API** (`features/controls/processDesigner/api/process-designer-api.ts`):

Six edits — change the URL prefix from `/api/v1/LoanApplication/Workflow` →
`/api/v1/Workflow` for each endpoint. Bodies and response shapes unchanged.

### Request-body contract for Workflow/Process/{id}/Execute

```json
{
  "command":           "Approve",        // string from GetCommands result's HiddenValue
  "comments":          "Approved with conditions",   // optional
  "parameters":        { /* optional dictionary, scheme-specific */ }
}
```

Response on success:

```json
{
  "status": { "code": 200, "message": "OK" },
  "data": {
    "processInstanceId": "...",
    "wasExecuted":       true,
    "newState":          "Approved",
    "comment":           "..."
  }
}
```

### Verify after deploy

1. Inbox loads against the new URL. Grid shows the same rows as before.
2. "Proceed" on a Transaction-type item: FE fires `PUT /Workflow/Process/{id}/Execute`. State advances. Inbox refreshes.
3. Same for an item with `ApplicationType = "AccountApplication"`. Same URL, different scheme — handled server-side.
4. Process Designer loads against `/Workflow/Schemes`. Designer iframe loads `/Workflow/Designer`.
5. Transaction edit page workflow buttons fire `/Workflow/Process/{id}/Execute` correctly.

### Does NOT change

- The `{ status, data }` envelope shape.
- The X-Device-Id requirement.
- The 401-triggers-logout interceptor.
- Inbox row data structure — same fields, same paging.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:
- §Inbox section: replace LoanApplication URLs with Workflow URLs.
- §Process Designer: same swap.
- §Architecture: note that workflow is a single backend resource — the
  FE no longer maps work-item type to controller name.

---

## 2026-05-05 — Slice 2.4 — Cross-cutting contract clarifications (audit-driven)

**Status**: ⏳ Pending

### Trigger

Audit found three contract clarifications that affect the whole new backend
but were never explicitly documented for the FE in earlier entries. Logging
them here so the FE engineer has the full surface area in one place.

### 1) Validation error envelope shape

When **any** request fails FluentValidation (Login body, role create, user
create, privilege bulk-replace, etc.), the response carries field-level
errors in this exact shape:

```json
{
  "status": {
    "code": 400,
    "message": "validation_failed",
    "description": "One or more validation failures occurred."
  },
  "data": {
    "success": 0,
    "code": "validation_failed",
    "errors": {
      "RoleName": [
        "'Role Name' must not be empty.",
        "'Role Name' must be 100 characters or fewer."
      ],
      "RoleDescription": [
        "'Role Description' must be 200 characters or fewer."
      ]
    }
  }
}
```

Rules:

- **Keys** in `data.errors` are the C# property names (PascalCase), not the
  JSON field names. So if the FE sends `{ roleName: "..." }`, the error
  key is `RoleName` not `roleName`. Match case-insensitively on the FE,
  or maintain a small camelCase ↔ PascalCase map in the form-binding code.
- **Values** are arrays of human-readable strings. A single field can have
  multiple errors (e.g. NotEmpty + MaxLength both fail). Render each
  string as a separate hint under the field.
- **`data.code`** is always `"validation_failed"`. Don't try to parse it
  for branch logic — branch on `status.code === 400` and presence of
  `data.errors`.
- **HTTP status** is always 400 for validator failures. Other 400s come
  from `AuthenticationException` (e.g. `data.code = "invalid_grant"`)
  and don't carry `data.errors`.

### 2) `Active_Flag` semantics — what Deactivate actually does today

The new backend currently **ignores** `Active_Flag` on RBAC reads (per FSI
direction, May 2026). Tracked in `BACKLOG.md` for formalisation. Concrete
implications for FE UX:

| FE button / action                                  | Current backend behaviour                                                                                                                                                                                 | Recommended FE handling                                                                                                                               |
| --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `POST /Role/{id}/Deactivate`                        | `Active_Flag = 0` on the role row, but the role STILL appears in `GET /Role`, STILL grants its privileges (mapping reads don't filter), and STILL attaches to existing users (their JWTs keep that role). | Show "Inactive" badge in admin list; filter out from role-assignment dropdowns client-side. **Don't** assume the role is removed from existing users. |
| `POST /User/{id}/Deactivate`                        | `Active_Flag = 0` AND `Status = "Inactive"`. Login is blocked via `AuthGuards.EnsureUserActive`. User STILL appears in `GET /User` list.                                                                  | Show "Inactive" badge; filter from active-only views client-side. Login attempts return `account_inactive`.                                           |
| `PUT /Role/{id}/Privileges` removing all privileges | Mapping rows DELETED (not deactivated).                                                                                                                                                                   | If the role had privileges, sending `[]` truly revokes them.                                                                                          |
| `PUT /User/{id}` with `roleIds: []`                 | Mapping rows DELETED (not deactivated).                                                                                                                                                                   | Sending `[]` truly revokes all user roles.                                                                                                            |

**Consequence**: Deactivate is **functional only for users** today. For
roles, it's a cosmetic flag the FE should respect in its own filtering
logic. Once `BACKLOG: Active_Flag handling on RBAC tables` ships, the
backend will start filtering and these become consistent.

### 3) JWT `roles` claim — bootstrap state caveat

JWTs carry role names from `TmX_User_Role_Mapping` via
`IRoleQueryService.GetRoleNamesAsync`. **Today the mapping table is
empty** (per the May 2026 schema diagnostic). So:

- Every user logging in today gets a JWT with **no role claims**.
- Any FE role-gated UI (`user.roles.includes("IT Admin")` → show admin
  menu) returns false uniformly.
- The `BootstrapAdminRoles: ["IT Admin"]` config DOES NOT add claims to
  the JWT — it only short-circuits the `[RequiresPrivilege]` filter
  server-side. So FE role-gating won't work for IT Admin via the
  bootstrap escape hatch.

To fix, do this once on each environment after deploying Slice 2:

1. Run `database/seed/rbac_grants.sql` (privilege rows + IT Admin grants).
2. Use the new `POST /api/v1/User` (or `PUT /api/v1/User/{id}` for an
   existing user) to assign the `IT Admin` role to your real admin user(s)
   via `roleIds: [1]`. (Role ID 1 is "IT Admin" per the live data —
   confirm with `GET /Role`.)
3. The admin user logs in again. JWT now carries `roles: ["IT Admin"]`.
   FE role-gated UI starts working.

### Verify after deploy

1. **Validation error**: `POST /api/v1/Role` with `{ roleName: "" }` →
   400 with `data.errors.RoleName: ["'Role Name' must not be empty."]`.
2. **Active_Flag for users**: `POST /User/{id}/Deactivate` then attempt
   to login as that user → 400 with `data.code = "user_inactive"` (or
   similar — verify exact code in `AuthGuards.EnsureUserActive`).
3. **Active_Flag for roles**: `POST /Role/{id}/Deactivate` for a role
   that has user assignments → those users still get the role in their
   JWT on next login (until the lifecycle work ships).
4. **JWT roles populated**: after Step 1+2 above, login response's JWT
   (decode at jwt.io) contains a `roles` claim listing the assigned roles.

### Does NOT change

- Endpoint paths or response shapes from earlier slices.
- The 401-triggers-logout interceptor.
- Token storage / refresh / device-binding.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:

- §global error-handling — document the validation error envelope shape
  (`data.errors: { Pascal_Field_Name: [...] }`).
- §Roles + §Users — note "Deactivate is cosmetic for roles today" and
  "Deactivate blocks login for users".
- §1 (Stack) — note that JWT `roles` claim depends on
  `TmX_User_Role_Mapping` being populated; bootstrap config doesn't fill
  it.

---

## 2026-05-05 — Slice 2.3 — User CRUD endpoints

**Status**: ⏳ Pending

### Trigger

Backend now ships User CRUD on the new `UserController` with role assignment
inline (not a separate `/User/{id}/Roles` endpoint). Replaces the legacy
`/api/v1/User` flattened-by-role response with the camelCase `roles[]` array
shape we agreed in Slice 2.2.

Backend pieces:

- `IUserAuthenticationService` extended with `CreateUserAsync(user, password)`
  + `UnlockAsync(user)`. Existing methods unchanged.
- `UserRoleMapping` entity + EF config expanded to carry NOT NULL audit columns.
- Application/Features/Users/: List, Get, Create, Update, SetActive, Unlock.
- API: `UserController` with seven endpoints, each guarded except the
  self-bypass on `GET /User/{id}` (caller can read their own profile).

### Endpoints

| Method | Path                           | Privilege              | Body / Query                                                          |
| ------ | ------------------------------ | ---------------------- | --------------------------------------------------------------------- |
| GET    | `/api/v1/User`                 | `Users.View`           | `?page=&pageSize=&sort=&filter=`                                      |
| GET    | `/api/v1/User/{id}`            | `Users.View` (or self) | —                                                                     |
| POST   | `/api/v1/User`                 | `Users.Create`         | `{ userName, password, emailAddress?, firstName?, ..., roleIds: [] }` |
| PUT    | `/api/v1/User/{id}`            | `Users.Update`         | `{ emailAddress?, firstName?, ..., roleIds?: [] }`                    |
| POST   | `/api/v1/User/{id}/Activate`   | `Users.Activate`       | —                                                                     |
| POST   | `/api/v1/User/{id}/Deactivate` | `Users.Activate`       | —                                                                     |
| POST   | `/api/v1/User/{id}/UnlockUser` | `Users.UnlockUser`     | —                                                                     |

### Self-bypass on `GET /User/{id}`

`MyProfilePage` calls `GET /api/v1/User/{caller's own id}` without holding
`Users.View`. The controller checks: if `id == caller.userId`, skip the
privilege gate; otherwise enforce `Users.View`. This is the only endpoint
in the backend with self-bypass logic — every other action requires the
named privilege.

### Response shapes

**List** (`GET /User`):

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": {
    "items": [
      {
        "userId": "0ac78487-e843-4ca7-b604-17dc7929d2cc",
        "userName": "allyali",
        "emailAddress": "allyali@icbc.com",
        "firstName": "Ally",
        "middleName": null,
        "lastName": "Ali",
        "phoneNumber": "+...",
        "status": "Active",
        "isActive": true,
        "isLockedOut": false,
        "lastLoginDate": "2026-05-04T08:21:11Z",
        "createdDate": "2024-08-12T10:14:23Z",
        "createdBy": "System",
        "roles": [
          { "roleId": 1, "roleName": "IT Admin" }
        ]
      }
    ],
    "total": 42, "page": 1, "pageSize": 10
  }
}
```

Sortable fields: `userName`, `emailAddress`, `firstName`, `lastName`,
`status`, `isActive`, `lastLoginDate`, `createdDate`. Unrecognised fields
fall back to `createdDate-desc`. Filter is a free-text substring across
`userName`, `emailAddress`, `firstName`, `lastName`.

**Get one** (`GET /User/{id}`): adds `tenantId`, `locationId`,
`userTypeLkpId`, `designationLkpId`, `imageUrl`, `twoFactorEnabled`,
`firstPasswordChange`, `passwordExpiryDate`, `effectiveStartDate`,
`effectiveEndDate`, `lastUpdatedDate`, `lastUpdatedBy`. Same `roles[]` shape.

**Create**: body fields shown in table above. Returns
`{ data: { userId } }` on success. Errors:

- 409 `user_name_taken` — username already used
- 409 `user_email_taken` — email already used (only checked if non-empty)
- 404 `role_not_found` with the missing IDs listed
- 409 `user_create_failed` if Identity rejects the password (caller composes
  Identity error messages into `errors`)

**Update**: profile fields are PATCH-style — null fields are ignored, only
non-null fields update. **Exception:** `roleIds` semantics —

- `roleIds = null` → don't touch role assignments at all.
- `roleIds = []`   → revoke all roles.
- `roleIds = [1, 2]` → bulk-replace with this set (server diffs).

**Activate / Deactivate / UnlockUser**: idempotent. Calling Activate on an
already-active user returns 200, no error. UnlockUser is also idempotent —
clears `LockoutEndDateUtc` and `AccessFailedCount` whether or not the user
is currently locked.

### FE alignment audit (per Slice 2.2 convention)

| FE call (current legacy + revamp)                                          | Backend                                                                               | Action                                                                                                                                                    |
| -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/v1/User?take=10&skip=0&sort=CreatedDate-desc` (Kendo, flattened) | ✓ Path matches; **shape changes** (now camelCase + `roles[]` array, one row per user) | FE adapts grid binding — was `data: [...]`, now `data: { items: [...], total }`. Was `RoleName: "IT Admin"` per row, now `roles: [{ roleId, roleName }]`. |
| `GET /api/v1/User/${id}`                                                   | ✓ Matches                                                                             | None (camelCase is new but the FE revamp already does that)                                                                                               |
| `POST /api/v1/User`                                                        | ✓ Matches                                                                             | None — FE just sends camelCase body now                                                                                                                   |
| `PUT /api/v1/User` (legacy: ID in body)                                    | ✗ Backend uses REST: `PUT /api/v1/User/{id}`                                          | FE moves the ID to the URL; body keeps the rest. Documented in API_GUIDELINES §1.                                                                         |
| `PUT /api/v1/User/Activate/${id}` (legacy: PUT)                            | ✗ Backend uses `POST /api/v1/User/${id}/Activate`                                     | FE switches verb to POST and reorders the path                                                                                                            |
| `PUT /api/v1/User/Deactivate/${id}` (legacy: PUT)                          | ✗ Backend uses `POST /api/v1/User/${id}/Deactivate`                                   | Same as above                                                                                                                                             |
| (no FE call yet)                                                           | `POST /api/v1/User/${id}/UnlockUser`                                                  | NEW — FE adds an "Unlock account" button on the user-edit screen                                                                                          |

### Verify after deploy

1. `GET /api/v1/User?page=1&pageSize=5` — 200, `data.items[]` with one row per user, `data.total` = real count, each user has `roles[]` array.
2. `POST /api/v1/User` `{ userName: "smoke", password: "Test123!", emailAddress: "smoke@x.com", firstName: "Smoke", lastName: "Test", roleIds: [1] }` — 200, `data.userId` returned. `GET /User/{newId}` shows the row with `roles: [{ roleId: 1, roleName: "IT Admin" }]`.
3. `POST /api/v1/User` with same `userName` — 409 `user_name_taken`.
4. `PUT /api/v1/User/{newId}` `{ roleIds: [] }` — 200, `roles[]` becomes empty.
5. `POST /api/v1/User/{newId}/Deactivate` → `GET /User/{newId}` shows `isActive: false, status: "Inactive"`.
6. `POST /api/v1/User/{newId}/Activate` again — idempotent 200.
7. `GET /api/v1/User/{caller's own id}` without `Users.View` privilege — 200 (self-bypass).
8. `GET /api/v1/User/{some other id}` without `Users.View` — 403 `forbidden_privilege`.

### Does NOT change

- The auth slice endpoints.
- `Auth/ChangePassword` is still where users change their OWN password.
- The 401-triggers-logout interceptor.
- The X-Device-Id header convention.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:

- §8.7 (Users) — replace the legacy User-list flattened shape with the new
  paged `{ items, total, page, pageSize }` shape. Document `roles[]` as
  array-of-objects per user, not single fields.
- §8.7 — note the verb / URL changes: `PUT /api/v1/User` →
  `PUT /api/v1/User/{id}`; `PUT /api/v1/User/Activate/{id}` →
  `POST /api/v1/User/{id}/Activate`; same for Deactivate.
- §8.7 — add the new `POST /api/v1/User/{id}/UnlockUser` endpoint.
- §9 / Profile section — note the self-bypass on `GET /User/{id}`.

---

## 2026-05-05 — Slice 2.2 — FE↔backend endpoint audit + API versioning standardisation

**Status**: ⏳ Pending

### Trigger

Two unrelated improvements landed together because they both adjust how the
FE talks to the new backend:

1. **Endpoint alignment audit.** The new backend's REST patterns mostly match
   what the FE revamp project (`tmx-finance-frontend-revamp`) already calls.
   A handful of divergences exist — mostly the privileges flow. Documented
   below so the FE engineer can see them at a glance and adapt.
2. **API versioning standardisation.** The new backend now uses
   `Asp.Versioning.Mvc` (the modern successor to
   `Microsoft.AspNetCore.Mvc.Versioning`). URLs are unchanged for clients —
   `/api/v1/...` still works exactly as before — but the infrastructure now
   supports per-version Swagger docs, response headers reporting supported
   versions, and clean v2 introduction without copy-paste controllers.

### 1) Endpoint alignment audit

Comparing FE call sites in `src/features/roleManagement/api/role-management-api.ts`
against what the new backend exposes after Slice 2.1:

| FE call (current)                      | Backend                                                                              | Action                                                                                           |
| -------------------------------------- | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| `GET    /api/v1/Role`                  | ✓ Matches                                                                            | None                                                                                             |
| `GET    /api/v1/Role/${id}`            | ✓ Matches                                                                            | None                                                                                             |
| `POST   /api/v1/Role`                  | ✓ Matches                                                                            | None                                                                                             |
| `PUT    /api/v1/Role/${id}`            | ✓ Matches                                                                            | None                                                                                             |
| `POST   /api/v1/Role/${id}/activate`   | ✓ Works (case-insensitive routing); canonical is `Activate`                          | None — FE keeps lowercase if it prefers                                                          |
| `POST   /api/v1/Role/${id}/deactivate` | ✓ Works (case-insensitive); canonical is `Deactivate`                                | None                                                                                             |
| `GET    /api/v1/entity`                | ✗ Not implemented; the new backend rolls entity grouping into the privileges catalog | Replace with `GET /api/v1/Privileges/Entities` (returns entities + their privileges in one call) |
| `GET    /api/v1/Privileges/list`       | ✗ Renamed                                                                            | Replace with `GET /api/v1/Privileges/Entities` (same source data, grouped by entity prefix)      |
| (FE has no call)                       | `GET /api/v1/Role/{id}/Privileges`                                                   | **NEW** — FE needs to add this for the role-edit screen                                          |
| (FE has no call)                       | `PUT /api/v1/Role/{id}/Privileges`                                                   | **NEW** — FE needs to add this for saving the privilege matrix                                   |

**Going-forward convention**: the backend builds REST-modern URLs and
documents every divergence from the FE's current call sites in this file.
The FE engineer is the source of truth for FE call sites — the backend never
edits files under `tmx-finance-frontend-revamp/`. Whenever a backend slice
ships endpoints that don't 1:1 align with what the FE already calls, the
delta gets a row in the table above.

### 2) API versioning — what changed and what to do

**No URL change for clients.** Every existing path still resolves:

```
GET /api/v1/Health         ✓ works
POST /api/v1/Auth/Login    ✓ works
GET /api/v1/Role           ✓ works
```

**New flexibility — three readers in priority order**:

1. **URL segment** (canonical): `/api/v1/Auth/Login`. The FE keeps doing this.
2. **Header**: `X-API-Version: 1.0`. Optional. Useful if the FE ever wants to
   pin a version at the HTTP-client level instead of in URL strings.
3. **Query string**: `?api-version=1.0`. Optional. Good for Postman /
   debugging.

If none are supplied, the server assumes `1.0` (back-compat). If a v2
controller ships later, clients call `/api/v2/...` OR
`X-API-Version: 2.0` against `/api/v1/...` URLs — both work.

**New response header — `api-supported-versions`**: every response now carries
this header listing every version of that endpoint that the server speaks
(today: `1.0`). When v2 ships and the server speaks both, the value becomes
`1.0, 2.0` and the FE can detect and migrate proactively.

**FE optional UX**: nothing breaks if you ignore versioning entirely. But if
you want to be future-proof, consider:

- Reading `api-supported-versions` from the first response in a session and
  storing the latest version the FE supports as a sticky preference.
- Adding `X-API-Version: 1.0` to your `appBaseApi.prepareHeaders` to lock in
  v1 explicitly until you've vetted v2 (when it eventually ships).

### Verify after deploy

1. `GET /api/v1/Health` — 200, response carries header `api-supported-versions: 1.0`.
2. `GET /api/Health` (no version) — 200 (default version 1.0 assumed).
3. `GET /api/v1/Health` with `X-API-Version: 2.0` — currently 400 (`unsupported_api_version`) since v2 doesn't exist yet.
4. Privileges flow:
   - `GET /api/v1/Privileges/Entities` (replaces `/api/v1/entity` + `/api/v1/Privileges/list`) → 200 with privileges grouped by entity.
   - `GET /api/v1/Role/123/Privileges` → 200 with the role's current grants.
   - `PUT /api/v1/Role/123/Privileges` `{ privilegeIds: [10, 11] }` → 200, replaces grants atomically.

### Does NOT change

- The auth, health, role CRUD URLs.
- The `{ status, data }` envelope.
- The 401-triggers-logout interceptor.
- The X-Device-Id header convention.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:

- §1 (Stack) — note that the backend uses `Asp.Versioning.Mvc`. Add
  `X-API-Version` and `api-supported-versions` to the headers list.
- §8.x — replace any references to `/api/v1/entity` and
  `/api/v1/Privileges/list` with `/api/v1/Privileges/Entities`.
- §9 — add the new role-privilege-matrix endpoints in the Roles section.

---

## 2026-05-05 — Slice 2.1 — Role CRUD + privilege matrix endpoints

**Status**: ⏳ Pending

### Trigger

Backend now ships Role CRUD plus the role-privilege-matrix endpoints needed
to drive the FE role-edit UI. All eight endpoints follow the standard
`{ status, data }` envelope and camelCase field naming.

Backend pieces:

- Domain: expanded `Role` entity (full schema), expanded
  `RolePrivilegeMapping` entity (audit columns).
- Application/Features/Roles/: List, Get, Create, Update, SetActive,
  Privileges (Get + Update — bulk-replace).
- API: `RoleController` with eight endpoints, each guarded by
  `[RequiresPrivilege("Roles.View")]` or `[RequiresPrivilege("Roles.Manage")]`.
- New exception types: `NotFoundException` → HTTP 404,
  `ConflictException` → HTTP 409. Wired into the global
  `ExceptionHandlingFilter` so they emit the standard envelope.

### New endpoints

| Method | Path                           | Privilege      | Body / Query                                           |
| ------ | ------------------------------ | -------------- | ------------------------------------------------------ |
| GET    | `/api/v1/Role`                 | `Roles.View`   | `?page=1&pageSize=10&sort=createdDate-desc&filter=...` |
| GET    | `/api/v1/Role/{id}`            | `Roles.View`   | —                                                      |
| POST   | `/api/v1/Role`                 | `Roles.Manage` | `{ roleName, roleDescription?, isActive? }`            |
| PUT    | `/api/v1/Role/{id}`            | `Roles.Manage` | `{ roleName, roleDescription? }`                       |
| POST   | `/api/v1/Role/{id}/Activate`   | `Roles.Manage` | —                                                      |
| POST   | `/api/v1/Role/{id}/Deactivate` | `Roles.Manage` | —                                                      |
| GET    | `/api/v1/Role/{id}/Privileges` | `Roles.View`   | —                                                      |
| PUT    | `/api/v1/Role/{id}/Privileges` | `Roles.Manage` | `{ privilegeIds: [int, int, ...] }`                    |

### Response shapes

**List** (`GET /Role`):

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": {
    "items": [
      {
        "roleId": 1,
        "roleName": "IT Admin",
        "roleDescription": "Administrator",
        "isActive": true,
        "createdDate": "2018-10-09T00:00:00",
        "createdBy": "System",
        "lastUpdatedDate": null,
        "lastUpdatedBy": null,
        "userCount": 4
      }
    ],
    "total": 8,
    "page": 1,
    "pageSize": 10
  }
}
```

Sort syntax: `field-direction` (Kendo-compatible). Sortable fields: `roleName`,
`roleDescription`, `isActive`, `createdDate`. Unrecognised fields fall back to
`createdDate-desc`. `filter` is a free-text substring match against
`roleName` and `roleDescription` (LIKE `%filter%`).

**Get one** (`GET /Role/{id}`): adds `tenantId`, `effectiveStartDate`, `effectiveEndDate`.

**Create / Update**: returns `{ data: { roleId } }` on success. 409 with
`code = "role_name_taken"` if the name is already in use (case-insensitive).

**Activate / Deactivate**: returns `{ data: { roleId, isActive } }`. Idempotent —
calling Activate on an already-active role returns 200 (no error).

**Get privileges** (`GET /Role/{id}/Privileges`):

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": [
    { "privilegeId": 10, "code": "Privileges.View", "description": "..." },
    { "privilegeId": 11, "code": "Roles.View",      "description": "..." }
  ]
}
```

**Update privileges** (`PUT /Role/{id}/Privileges`): bulk-replace —
**not** delta. Send the FULL desired set. Unknown IDs return 404 with
`code = "privilege_not_found"`. Hard cap of 500 IDs per request.

### Action — FE Role-management screen wiring

The FE role admin page ships these shapes against the new endpoints. The
privilege-matrix UI works like:

1. On page load: `GET /api/v1/Role` (with paging) AND `GET /api/v1/Privileges/Entities`
   (the catalog from Slice 2.0). One renders the role grid, the other renders
   the column headers for the matrix.
2. User selects a role row → opens edit modal.
3. Edit modal calls `GET /api/v1/Role/{id}` for details AND
   `GET /api/v1/Role/{id}/Privileges` for current grants (set of checked boxes).
4. User edits name/description, toggles privilege checkboxes.
5. Save fires up to three calls:
   - `PUT /api/v1/Role/{id}` (name + description) IF those changed.
   - `POST /api/v1/Role/{id}/Activate` or `/Deactivate` IF active state changed.
   - `PUT /api/v1/Role/{id}/Privileges` with the FULL set of currently-checked
     IDs IF the matrix changed.

The three calls can fire in parallel; the server diffs the privilege bulk-replace
internally so re-sending unchanged grants is a no-op.

### Verify after deploy

1. `GET /api/v1/Role` — 200, returns the 8 legacy roles (`IT Admin`, `Analyst`,
   `Authorizer`, `Analyst KYC`, `Authorizer KYC`, `ANALYST IMPORT`,
   `AUTHORIZER IMPORT`, `Viewing Rights`).
2. `POST /api/v1/Role` with `{ roleName: "Test Role", roleDescription: "smoke test" }` — 200,
   returns `data.roleId`. `GET /Role/{newId}` — 200.
3. `PUT /api/v1/Role/{newId}/Privileges` with `{ privilegeIds: [<Privileges.View id>] }` — 200.
   `GET /Role/{newId}/Privileges` — 200, returns one row.
4. `PUT /api/v1/Role/{newId}/Privileges` with `{ privilegeIds: [] }` — 200, removes the grant.
5. `POST /api/v1/Role` with `{ roleName: "IT Admin" }` — 409, `code = "role_name_taken"`.
6. `GET /api/v1/Role/9999` — 404, `code = "role_not_found"`.

### Does NOT change

- Other slice-2 endpoints (Privileges).
- Auth slice endpoints.
- The 401-triggers-logout interceptor.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:

- Add a "Roles" section under §8 / §9 with the eight new endpoints + shapes.
- Update the global error-handling docs: 404 with `code = "..._not_found"` and
  409 with `code = "*_taken"` / `"*_in_use"` are non-fatal (toast-only, no logout).

---

## 2026-05-05 — Slice 2.0 — Privilege scaffold + new 403 envelope code

**Status**: ⏳ Pending

### Trigger

Backend now ships a `[RequiresPrivilege("...")]` action filter that gates
admin endpoints behind named privilege codes. The first endpoint guarded by
it is `GET /api/v1/Privileges/Entities`. Subsequent slice-2 user/role CRUD
endpoints will all be guarded the same way.

Backend pieces:

- `IPrivilegeService` (Application contract) + EF impl (`PrivilegeService`).
- `[RequiresPrivilege]` filter — reads role-name claims from the JWT, resolves
  granted privilege codes via `IPrivilegeService`, caches the set in
  `HttpContext.Items` so multiple checks per request cost one query.
- New endpoint: `GET /api/v1/Privileges/Entities` — read-only catalog of
  privilege codes grouped by entity. Drives the role-edit privilege-matrix UI.
- Seed migration `2026_05_005_SeedScopedPrivileges.sql` inserts 8 scoped codes
  (`Users.View`, `Users.Create`, `Users.Update`, `Users.Activate`,
  `Users.UnlockUser`, `Roles.View`, `Roles.Manage`, `Privileges.View`)
  alongside the legacy 8 generic-verb rows. **Existing rows untouched.**

### Action — handle the new 403 code in the FE

The `[RequiresPrivilege]` filter returns 403 with a new `data.code`:

```json
{
  "status": { "code": 403, "message": "forbidden_privilege",
              "description": "This action requires the 'Users.Create' privilege." },
  "data": { "success": 0, "code": "forbidden_privilege", "required": "Users.Create" }
}
```

FE handling:

1. **Don't trigger the global logout interceptor on 403.** 401 still means
   "log me out"; 403 with `code = "forbidden_privilege"` means "you're logged
   in, you just don't have permission for this specific action." Show a toast
   like *"You don't have permission to do that."* — no redirect.
2. The `data.required` string is the privilege code that was missing — not for
   end-user display, but useful for support diagnostics. OK to surface in a
   small subtitle: *"Missing privilege: Users.Create"*.

### New endpoint — `GET /api/v1/Privileges/Entities`

Bearer-protected. Requires `Privileges.View` privilege (or one of the
bootstrap admin roles — see "Bootstrap" below).

Response:

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": [
    {
      "entity": "Common",
      "privileges": [
        { "privilegeId": 2, "code": "View",        "description": null },
        { "privilegeId": 3, "code": "Edit",        "description": null },
        { "privilegeId": 4, "code": "Delete",      "description": null },
        { "privilegeId": 5, "code": "Create",      "description": null },
        { "privilegeId": 6, "code": "Assign",      "description": null },
        { "privilegeId": 7, "code": "Verify",      "description": null },
        { "privilegeId": 8, "code": "Approve",     "description": null },
        { "privilegeId": 9, "code": "View Entity", "description": null }
      ]
    },
    {
      "entity": "Privileges",
      "privileges": [
        { "privilegeId": 10, "code": "Privileges.View", "description": "..." }
      ]
    },
    {
      "entity": "Roles",
      "privileges": [
        { "privilegeId": 11, "code": "Roles.View",   "description": "..." },
        { "privilegeId": 12, "code": "Roles.Manage", "description": "..." }
      ]
    },
    {
      "entity": "Users",
      "privileges": [
        { "privilegeId": 13, "code": "Users.View",       "description": "..." },
        { "privilegeId": 14, "code": "Users.Create",     "description": "..." },
        { "privilegeId": 15, "code": "Users.Update",     "description": "..." },
        { "privilegeId": 16, "code": "Users.Activate",   "description": "..." },
        { "privilegeId": 17, "code": "Users.UnlockUser", "description": "..." }
      ]
    }
  ]
}
```

Use this in the role-edit screen to render the privilege-matrix grid (rows =
privileges grouped by entity, columns = a checkbox column per privilege per
role). The bulk-replace `PUT /api/v1/Role/{id}/Privileges` endpoint ships in a
later slice-2 entry once Role CRUD lands.

### Bootstrap escape hatch — temporary

Until an admin has wired role → privilege grants in the new role-edit UI,
**every protected endpoint would otherwise return 403 for everyone.** To
unblock day-one operation, callers in any role listed in
`Auth:BootstrapAdminRoles` (default: `["IT Admin"]`) skip the privilege check
entirely (still must be authenticated).

This is a deliberate, documented short-term workaround. Once an admin has
populated `TmX_Role_Privilege_Mapping` for the real roles, the operator should
clear `Auth:BootstrapAdminRoles` to `[]` in `appsettings.json`.

FE doesn't need to do anything special for the bootstrap — it just sees
"the IT Admin role has access to everything." But once the bootstrap is
removed, IT Admin needs the same explicit privilege grants as everyone else.

### Verify after deploy

1. Run `2026_05_005_SeedScopedPrivileges.sql` on `ICBC_DEMO`. Confirm the 8 new rows
   appear in `TmX_Privilege` next to the original 8.
2. Login as a user with the `IT Admin` role. `GET /api/v1/Privileges/Entities` →
   200, returns the catalog above. (The bootstrap escape hatch lets this through
   even though `TmX_Role_Privilege_Mapping` is still empty.)
3. Login as a user WITHOUT `IT Admin` (any of the other 7 roles). Same call →
   403 with `data.code = "forbidden_privilege"` and `data.required = "Privileges.View"`.

### Does NOT change

- The auth endpoints' behaviour or shape.
- The existing 8 generic-verb privilege rows are not modified or deleted.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:

- Add a "Privileges" section under §8 / §9 documenting the new endpoint.
- Add a paragraph to the global error-handling section noting that 403 with
  `data.code = "forbidden_privilege"` is non-fatal (toast-only, no logout).

---

## 2026-05-05 — Slice 1.6 — Concurrent-login restriction (config-gated, eviction UX)

**Status**: ⏳ Pending

### Trigger

Backend now honours `Auth.RestrictConcurrentLogin` (default `false`). When the
operator turns it on, a successful login revokes every OTHER active device for
the same user, cascading to their refresh tokens. This restores the legacy
RESTRICT_CONCURRENT_LOGIN behaviour without baking a single-session assumption
into the system — multi-device stays the default.

Backend pieces:

- New flag in `AuthOptions.RestrictConcurrentLogin` (`Application/Common/Options/AuthOptions.cs`).
- Enforcement at login time in `LoginCommandHandler` step 7a — calls `IDeviceService.RevokeAllExceptAsync(...)` and writes a `DeviceRevoked` audit row with detail "Concurrent-login policy: other devices revoked on new sign-in.".
- No middleware changes. Evicted devices simply hit `device_unknown` on their next API call, which is the existing forced-logout path (handled by Slice 1.5).

### Action — FE behaviour to verify (likely zero code change)

This is mostly **a UX surface, not a contract change**. The 401 + `data.code = "device_unknown"` path the FE already implements for Slice 1.5 covers eviction transparently:

1. User logs in on Browser A → tokens + deviceId stored.
2. Operator flips `Auth.RestrictConcurrentLogin = true` and restarts the API.
3. Same user logs in on Browser B → server revokes Browser A's device + refresh token.
4. Browser A's next authenticated call returns 401 with `data.code = "device_unknown"` → FE clears localStorage, redirects to `/login`.

So if the Slice 1.5 work is wired correctly, **nothing extra is required**. Optional polish:

- Distinct toast on the `/login` redirect when the cause was `device_unknown`: e.g. *"You were signed out because you signed in on another device."* (Plain-English explanation of the eviction.)
- "Active sessions" page (Slice 1.5) gains a hint at the top when the policy is on — but the policy state is server-only today, so this requires a small `/api/v1/Auth/Policy` GET endpoint on the backend to expose it. Not in scope for this slice; tracked in `BACKLOG.md`.

### New endpoints — none

### Login response — unchanged

### Verify after deploy

1. Set `Auth.RestrictConcurrentLogin = true` in `appsettings.json`. Restart.
2. Login on Browser A. Confirm tokens + deviceId in localStorage.
3. Login as the same user on Browser B. Confirm Browser B works normally.
4. From Browser A, hit any authenticated endpoint — expect 401 with `data.code = "device_unknown"`. FE redirects to `/login`.
5. Login row in `TmX_Login_Audit` for Browser B: one `Login` row with result `Success` AND one `DeviceRevoked` row with detail starting "Concurrent-login policy:". Both rows share the new `Device_ID`.
6. Flip the flag back to `false` and restart. Confirm Browser A and Browser B can hold sessions side by side again.

### Does NOT change

- Multi-device is still the default (`RestrictConcurrentLogin: false`).
- The X-Device-Id header convention (Slice 1.5).
- Sessions endpoints — `Sign out from all other devices` is still the user-driven version of this; the new flag is the policy-driven version.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md` — add a one-paragraph note under the
"Security" section explaining that when the operator enables
`Auth.RestrictConcurrentLogin`, signing in from a new browser will sign the
user out everywhere else, surfaced via the existing `device_unknown` flow.

---

## 2026-04-30 — Slice 1.5 — Per-device sessions: `X-Device-Id` header + Sessions endpoints

**Status**: ⏳ Pending

### Trigger

Backend now tracks devices per user (`TmX_User_Device`) and audits every auth
event (`TmX_Login_Audit`). Refresh tokens are bound to a device. The FE must
participate by carrying a server-issued `X-Device-Id` header on every
authenticated request.

Backend pieces:

- New tables (`TmX_User_Device`, `TmX_Login_Audit`) + altered `RefreshTokens.Device_ID`. SQL: `database/migrations/2026_05_004_CreateUserDeviceAndLoginAudit.sql`.
- Middleware `DeviceTrackingMiddleware` — runs after JWT validation. Rejects authenticated requests missing the header, or whose device doesn't belong to the JWT user.
- Three new endpoints under `/api/v1/Auth/Sessions*`.

### Action — header lifecycle in `auth-api.ts` / base query

1. **First Login** (no `localStorage` device id yet):
   - Don't send `X-Device-Id`.
   - Server invents one, returns it in `data.deviceId` alongside tokens.
   - FE writes `data.deviceId` to `localStorage` (alongside `accessToken` / `refreshToken`).
2. **Every authenticated request after that**:
   - Read `deviceId` from localStorage.
   - Add header `X-Device-Id: <deviceId>` (alongside `Authorization: Bearer …`).
   - Easiest place: `appBaseApi.prepareHeaders` — same place the Bearer header is attached.
3. **Refresh response**:
   - Backend echoes `data.deviceId` so the FE can repair localStorage if it ever loses it. Just re-write to localStorage on every refresh.
4. **On 401 with `data.code = "device_unknown"` or `"device_user_mismatch"`**:
   - Treat as forced logout. Clear localStorage (tokens + deviceId). Redirect to /login.
   - This happens if the user revokes the device from another browser, or admin revokes.

### New endpoints — UI to add

| Method | Path                               | Purpose                                       | UI                                                    |
| ------ | ---------------------------------- | --------------------------------------------- | ----------------------------------------------------- |
| GET    | `/api/v1/Auth/Sessions`            | List active devices                           | "Active sessions" page (under Profile / Security tab) |
| DELETE | `/api/v1/Auth/Sessions/{deviceId}` | Revoke one device                             | "Sign out" button next to each row in the list        |
| DELETE | `/api/v1/Auth/Sessions/Other`      | Revoke every other device but the current one | "Sign out from all other devices" button              |

Response shape for the list:

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": [
    {
      "deviceId":    "ABC...",
      "label":       "Chrome on Windows",
      "userAgent":   "Mozilla/5.0 ...",
      "firstSeenAt": "2026-04-12T08:14:23Z",
      "lastSeenAt":  "2026-04-30T11:02:00Z",
      "lastSeenIp":  "203.99.x.x",
      "isTrusted":   false,
      "isCurrent":   true
    }
  ]
}
```

The `isCurrent: true` row is the device the user is on right now — UI should
disable the "sign out" button on that row (or label it "this device").

### Login response — new `deviceId` field

Inside `data`, alongside the tokens we already shipped:

```json
"data": {
  "success":      1,
  "userId":       "...",
  "userName":     "...",
  "isFirstLogin": false,
  "accessToken":  "eyJ...",
  "refreshToken": "L4_5o...",
  "expiresIn":    3585,
  "deviceId":     "ABC..."         // ← new — persist to localStorage
}
```

### Verify after deploy

1. Fresh browser (no localStorage). Login → response carries `data.deviceId`. Persist.
2. Subsequent calls carry `X-Device-Id`. Network tab shows the header on every authenticated request.
3. Open `/Auth/Sessions` from Postman with the Bearer + X-Device-Id → returns one row, `isCurrent: true`.
4. Login from a second browser — `/Auth/Sessions` from the first browser now lists two rows, the second one `isCurrent: false`.
5. DELETE the second row from browser 1 → browser 2's next API call returns 401 with `data.code = "device_unknown"`. FE handles by clearing localStorage and redirecting.
6. "Sign out from all other devices" — same effect for every device except the current one.

### Does NOT change

- Login body shape (still form-urlencoded, same fields).
- The other auth endpoints' contracts.
- 401-triggers-logout interceptor — it now also fires on `device_unknown` / `device_user_mismatch`.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md`:

- §1 (Stack) — note the `X-Device-Id` header convention for `appBaseApi`.
- §8.1 — add the four new auth endpoints (3 Sessions + the implicit deviceId
  field on Login/Refresh responses).
- §9 — add a "Security" page-by-page entry for the Sessions list UI.

---

## 2026-04-30 — Slice 1 — New endpoints: ResetExpiredPassword + 2FA setup trio

**Status**: ⏳ Pending

### Trigger

Two real gaps closed on the backend:

1. The `EnforceFirstPasswordChange` and `EnforceExpiry` policy gates would
   trap users with no anonymous recovery path.
2. The OTP login gate had no enrolment / disable flow, so 2FA was conceptually
   reachable but practically unusable.

Backend now ships four new endpoints to make those flows complete:

| Method | Path                                | Auth   | Purpose                                                                                                    |
| ------ | ----------------------------------- | ------ | ---------------------------------------------------------------------------------------------------------- |
| POST   | `/api/v1/Auth/ResetExpiredPassword` | none   | User who hit `password_expired` or `first_password_change_required` resets without admin help              |
| POST   | `/api/v1/Auth/EnableTwoFactor`      | Bearer | Step 1 of 2FA setup. Returns `{ secret, provisioningUri }`                                                 |
| POST   | `/api/v1/Auth/ConfirmTwoFactor`     | Bearer | Step 2 of 2FA setup. User submits a 6-digit code from authenticator. On success, `TwoFactorEnabled = true` |
| POST   | `/api/v1/Auth/DisableTwoFactor`     | Bearer | Turn 2FA off. Requires current password (defends against stolen Bearer)                                    |

### Action — FE flows to add

**Reset-expired-password screen.** Triggered when login returns `data.code = "password_expired"` or `"first_password_change_required"`.

```
1. FE shows "Your password needs to be reset" form with three fields:
     username (prefilled from login attempt),
     currentPassword (the one that just failed the gate),
     newPassword.
2. FE → POST /api/v1/Auth/ResetExpiredPassword
   body (json): { username, currentPassword, newPassword }
3. On 200 → "Password reset. Please sign in." → return to login screen.
4. On 400 with code "no_reset_required" → user wasn't actually in a reset-eligible
   state; tell them to use the regular ChangePassword screen instead.
5. On 400 with code "invalid_grant" → wrong username/password; same generic
   error message as login.
```

**2FA setup screen** (under `MyProfilePage` → "Security" tab, or wherever).

```
1. User clicks "Enable two-factor authentication".
2. FE → POST /api/v1/Auth/EnableTwoFactor   (no body)
   200 returns:
     data: {
       secret:           "JBSWY3DPEHPK3PXP",         // Base32, ~32 chars
       provisioningUri:  "otpauth://totp/FSI.Trade.Compliance:user@example.com?secret=...&issuer=..."
     }
3. FE renders the provisioningUri as a QR code (use any JS QR lib —
   qrcode.react / react-qr-code / similar). Also shows the raw secret
   as fallback for users who can't scan.
4. User opens Google Authenticator (or Microsoft Authenticator, Authy, 1Password, etc.)
   and either scans the QR or types the secret.
5. App displays a 6-digit code that rotates every 30 seconds.
6. FE shows an OTP input. User types the current code and submits.
7. FE → POST /api/v1/Auth/ConfirmTwoFactor
   body: { otp: "123456" }
8. On 200 → "Two-factor authentication enabled" → user's TwoFactorEnabled=true
   on the server. Future logins will require an OTP (provided global
   TwoFactor:Enabled config is also true).
9. On 400 with code "invalid_otp" → bad code; let them retry.
   On 400 with code "otp_not_provisioned" → they hit Confirm without having
   called Enable first; restart the flow.
```

**Disable 2FA screen.**

```
1. User clicks "Turn off two-factor authentication".
2. FE shows a password-confirm dialog (don't allow disabling without it).
3. FE → POST /api/v1/Auth/DisableTwoFactor
   body: { currentPassword: "..." }
4. On 200 → 2FA off, secret cleared.
5. On 400 with code "invalid_grant" → wrong password.
```

### Verify after deploy

1. Trigger an expired-password state on a test user (set `PasswordExpiryDate` to yesterday). Login returns `password_expired`. Then call `ResetExpiredPassword` with right credentials → 200, login again succeeds.
2. Enable 2FA on a test account; QR scans cleanly into Google Authenticator; `Confirm` with the displayed code → 200. Login afterwards: without `otp` returns `otp_required`; with the right `otp` returns 200.
3. `Disable` with the right password → 200; subsequent logins no longer require OTP.

### Does NOT change

- The four existing auth endpoints (Login, Refresh, Logout, ChangePassword) — same paths, same shapes.
- Token storage on the FE.
- The 401-triggers-logout interceptor.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md` §8.1 / §8.12 — add the four new
endpoints. Add a new "Security" section under §9 (page-by-page reference)
covering the reset-password and 2FA-setup screens.

---

## 2026-04-30 — Slice 1 — Login body: drop unused OAuth fields

**Status**: ⏳ Pending

### Trigger

Backend dropped `grant_type`, `client_id`, `client_secret`, `scope` from the
Login form. Single FE-client deployment means we don't validate those fields,
so carrying them in the contract was just legacy noise. `isEncrypted` stays
(rejected when true; no-op when false). `otp` stays (used when 2FA is enabled
both globally and on the user).

Backend file: `Services/FSI.Trade.Compliance.Application/Features/Auth/Login/LoginCommand.cs`

### Action — `auth-api.ts` login mutation body

| Old field       | Action                                                                                                                |
| --------------- | --------------------------------------------------------------------------------------------------------------------- |
| `username`      | **keep** (required)                                                                                                   |
| `password`      | **keep** (required)                                                                                                   |
| `isEncrypted`   | **keep** (send `false`; if you ever send `true` the server returns `data.code = "encrypted_credentials_unsupported"`) |
| `otp`           | **keep** (only set when the user has 2FA and the global TwoFactor flag is on)                                         |
| `grant_type`    | **remove**                                                                                                            |
| `client_id`     | **remove**                                                                                                            |
| `client_secret` | **remove**                                                                                                            |
| `scope`         | **remove**                                                                                                            |

The four removed fields are silently ignored by the backend if you keep
sending them (extra form fields aren't an error in ASP.NET Core), so this is
**optional cleanup**, not a hard breaking change. Recommended to remove for
clarity.

### Behaviour notes — OTP flow

Single endpoint, single API. The "trigger to require OTP" is two flags ANDed:

1. Global `TwoFactor:Enabled` (server config). Default `false`.
2. Per-user `ApplicationUser.TwoFactorEnabled`.

If both are true:

```
1. FE → POST /api/v1/Auth/Login  (username + password, no otp)
2. Server → 400 with data.code = "otp_required"
3. FE shows authenticator-code input
4. User reads 6-digit code from Google/Microsoft Authenticator (TOTP)
5. FE → POST /api/v1/Auth/Login  (username + password + otp)
6. Server → 200 with tokens
```

No emails, no SMS, no separate "verify-otp" endpoint, no challenge tokens.
Step-up auth (new-device / time-based) is **not** implemented — server config
exposes future toggles in `TwoFactorOptions` but they're inert today.

### Behaviour notes — Password expiry flow

```
1. FE → POST /api/v1/Auth/Login  (correct username/password, but expired)
2. Server → 400 with data.code = "password_expired"
3. FE shows "Your password has expired" UI
```

**Gap (slice-2 work)**: there's currently no anonymous endpoint to set a new
password without a Bearer token. So in slice 1, an expired-password user is
stuck until admin intervention. To avoid trapping users:

- Either keep `PasswordPolicy:EnforceExpiry = true` (current default — matches
  legacy) and rely on admin reset, OR
- Set `PasswordPolicy:EnforceExpiry = false` in your environment until
  slice 2 ships the anonymous reset endpoint.

Both are valid stances; FE behaviour doesn't change — just the proportion of
users who hit the gate.

### Verify after deploy

1. Login with the slim body (no `grant_type`/`client_id`/etc.) — 200, tokens issued.
2. Login with the FULL legacy body still works — extras ignored.
3. With `TwoFactor:Enabled = true` (and a 2FA-enrolled test user): submitting without `otp` returns `data.code = "otp_required"`; submitting with the right `otp` returns 200.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md` §8.1 / §1 / §3 — drop references to
`grant_type`, `client_id`, `client_secret`, `scope` in the login body shape.

---

## 2026-04-30 — Slice 1 — Auth response shape: tokens nested inside `data`

**Status**: ⏳ Pending

### Trigger

Backend collapsed the response envelope to a single shape — `{ status, data }` —
to match best practice (one envelope, all payload nested). The legacy OWIN-style
top-level `access_token` / `refresh_token` / `expires_in` fields are gone; the
same values now live inside `data` in camelCase form (`accessToken`,
`refreshToken`, `expiresIn`). Applies to `Auth/Login` and `Auth/Refresh`.

Backend file: `Services/FSI.Trade.Compliance.Application/Common/Models/ResponseViewModel.cs`
(no longer carries top-level token fields) and `AuthResponse.cs` (now carries
`AccessToken` / `RefreshToken` / `ExpiresIn`).

### Action — auth response handling in `auth-api.ts`

Old response shape:

```json
{
  "status": { "code": 200, "message": "OK" },
  "data":   { "Success": 1, "AppLink": null, "AppVersion": null },
  "access_token":  "eyJ...",
  "refresh_token": "L4_5o..."
}
```

New response shape:

```json
{
  "status": { "code": 200, "message": "OK", "description": null },
  "data": {
    "success": 1,
    "appLink": null,
    "appVersion": null,
    "userId": "0ac78487-e843-4ca7-b604-17dc7929d2cc",
    "userName": "allyali@icbc.com",
    "isFirstLogin": false,
    "message": null,
    "accessToken": "eyJ...",
    "refreshToken": "L4_5o...",
    "expiresIn": 3585
  }
}
```

Required FE edits:

| File                                                                                           | Change                                                                                                              |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `features/auth/api/auth-api.ts` — login `transformResponse` (or wherever tokens are extracted) | Read `data.accessToken`, `data.refreshToken`, `data.expiresIn` instead of top-level `access_token`, `refresh_token` |
| `features/auth/api/auth-api.ts` — refresh `transformResponse`                                  | Same change for the refresh response                                                                                |
| Any TypeScript interface declaring `access_token` / `refresh_token` at the top level           | Remove top-level tokens; add `accessToken: string`, `refreshToken: string`, `expiresIn: number` to the data shape   |
| `appBaseApi.prepareHeaders` — Bearer-attach logic                                              | Should already read from `localStorage`; just make sure the localStorage write path uses the new field names        |

### Verify after deploy

1. Network tab — login response shows tokens nested under `data`, no top-level token fields.
2. localStorage shows `access_token` / `refresh_token` (or whatever the FE chose to store as) populated correctly.
3. Subsequent authenticated calls carry the Bearer header — proves the read-path works.
4. Refresh flow rotates correctly: old refresh token returns 401, new one works.

### Does NOT change

- The endpoint paths (`/api/v1/Auth/Login`, `/api/v1/Auth/Refresh`, etc.) are unchanged.
- The `data.success === 0` explicit-failure semantics are unchanged.
- 401-triggers-logout is unchanged.

### Spec update (FE engineer to apply)

`tmx-finance-frontend-revamp/PROJECT.md` §8.1 — update the Login response shape
description from the current `{ status, data, access_token, refresh_token }`
form to the new `{ status, data: { ..., accessToken, refreshToken, expiresIn } }`
form.

---

## 2026-04-30 — Slice 1 — Auth controller path migration

**Status**: ⏳ Pending

### Trigger

Backend split auth-lifecycle operations off `UserController` onto
`AuthController` (single-responsibility separation — login / logout /
change-password operate on tokens & credentials, not user-profile data).

Backend files:

- `Services/FSI.Trade.Compliance.API/Controllers/AuthController.cs` (now owns the four endpoints)
- `Services/FSI.Trade.Compliance.API/Controllers/UserController.cs` (placeholder; reserved for slice-2 user CRUD)
- `Services/FSI.Trade.Compliance.API/appsettings.json` (`Auth:WhitelistedPaths` updated)

### Action — three path-string updates

| Where in the FE                                                                       | Old path                      | New path                      |
| ------------------------------------------------------------------------------------- | ----------------------------- | ----------------------------- |
| `features/auth/api/auth-api.ts` — login mutation                                      | `/api/v1/User/Login`          | `/api/v1/Auth/Login`          |
| `features/auth/api/auth-api.ts` — logout call (also used inline in `left-navbar.tsx`) | `/api/v1/User/Logout`         | `/api/v1/Auth/Logout`         |
| `MyProfilePage` — change-password mutation                                            | `/api/v1/User/ChangePassword` | `/api/v1/Auth/ChangePassword` |

### Optional in same change

Add a `refreshMutation` to `auth-api.ts` hitting `POST /api/v1/Auth/Refresh`.
Body: `{ refresh_token }`. Response shape identical to login. Wire it into the
401 interceptor / silent-refresh path if the client supports it. (Backend
already supports rotation with reuse-detection.)

### Verify after deploy

1. Login from the UI — Network tab shows `POST /api/v1/Auth/Login` (not `/User/Login`), 200 with `access_token` + `refresh_token` in the response body.
2. Reload — `MyProfilePage` still loads (proves `GET /api/v1/User/{id}` was NOT touched by mistake).
3. Change password — Network tab shows `POST /api/v1/Auth/ChangePassword`, 200, then user is silently re-authenticated or required to re-login (depending on FE handling — refresh tokens are revoked on password change).
4. Logout — Network tab shows `POST /api/v1/Auth/Logout`, 200, localStorage cleared, redirect to `/login`.

### Does NOT change

- `GET /api/v1/User/{id}` (user-profile read used by `MyProfilePage`) — stays on `UserController`. Don't touch it in `MyProfilePage`.
- Login request body shape — still `application/x-www-form-urlencoded`. Accepted fields:
  `username, password, isEncrypted, otp, grant_type, client_id, client_secret, scope`.
  All except `username` and `password` are optional.
- Login response shape (still `{ status, data: { Success }, access_token, refresh_token }`).
- `isExplicitValidationFailure` logic on the FE — `data.Success === 0` still signals an explicit validation failure; backend continues to emit it for `AuthenticationException`s.
- The 401-triggers-logout interceptor in `auth-base-query.ts`.
- Bearer-header attachment via `appBaseApi`.

### Behaviour notes for the FE engineer

- **`isEncrypted=true` is rejected** with `status.message = "encrypted_credentials_unsupported"`. The legacy AES decryption path is not ported (weak fixed key/IV, out of scope). FE must send `isEncrypted=false` or omit the field; credentials travel as plaintext over TLS.
- `client_id` / `client_secret` are still **accepted** for OAuth-shape compatibility but are no longer validated against a clients table. The single FE client model (Decision 13) means we don't gate on them. Sending or omitting either has the same effect; sending wrong values does not fail login.
- `grant_type` is accepted; only `password` is meaningful. Other values are ignored in slice 1 (no other grant types implemented).
- `scope` is accepted and ignored in slice 1.

### Spec update (FE engineer to apply)

When the path edits above are merged, also update
`tmx-finance-frontend-revamp/PROJECT.md`:

- §8.1 (Auth & app init) — change the `User/Login` and `User/Logout` rows to `Auth/Login` and `Auth/Logout`. Add an `Auth/Refresh` row if you wire the refresh mutation.
- §8.12 (Profile) — change the `POST /api/v1/User/ChangePassword` reference to `POST /api/v1/Auth/ChangePassword`. Note that the read still uses `GET /api/v1/User/{id}`.

---

<!-- Newer entries go ABOVE this line. -->

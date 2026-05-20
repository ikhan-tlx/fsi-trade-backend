# Legacy Transaction + KYC + BRAINS — End-to-End Flow

**Purpose**: capture how the current legacy system actually executes a Transaction
from the moment the FE opens the add screen to the moment a KYC case is finalised
on FCCM. Used to validate what the new backend (Slices 4 / 5 / 6) needs to
replicate, and to make the implementation traceable for new joiners.

**Sources** (all validated by source-code search May 2026):

| Tier | Project | Path |
|---|---|---|
| FE | revamp | `D:\ICBC - Latest\tmx-finance-frontend-revamp\` |
| Backend (primary) | TMX.WebAPI / TMX.Services / TMX.Workflows | `D:\ICBC - Latest\tmx-finance-backend\` |
| Integrations gateway | tmx-finance-integrations | `D:\ICBC - Latest\tmx-finance-integrations\` |
| KYC project | Integration KYC | `D:\ICBC - Latest\Integration KYC\` |

**Key takeaway up front**: the FE never talks to BRAINS or FCCM directly.
Everything flows through `tmx-finance-backend`, which delegates to the
gateway, which delegates to the KYC project. The case-management upstream
(FCCM) is **fired by workflow transitions**, not by FE actions. The
20-second FCCM-poll happens inside the user's "Submit" click — bad UX the
new backend will fix. The FE makes only four kinds of calls during a
Transaction lifecycle: (1) one screening lookup at submit time (KYC- or
TBAML-product-conditional), (2) Transaction Create, (3) dynamic-form
schema load, (4) workflow-command Execute.

---

## Phase 1 — User opens Add Transaction page

**FE route**: `/transaction/trade-repository/add` →
`src/features/tradeRepository/add/trade-repository-add.tsx`

**No API calls fire on page load.** The page renders an empty Customer
Details mini-form (Customer ID + Application Date inputs). Nothing hits
the backend until the user clicks the "+ Add Transaction" button.

This was a key correction during the May 2026 trace — the earlier
assumption that a "customer picker" fired API calls on focus / search was
wrong. The picker is just a text input.

---

## Phase 2 — User clicks "+ Add Transaction" (the real first phase)

Everything happens inside `handleAddTransaction` —
`tmx-finance-frontend-revamp/src/features/tradeRepository/add/trade-repository-add.tsx:587-695`.

### Step 2.1 — Conditional screening lookup (KYC OR TBAML product, exclusive)

Branch determined by `isKycProduct` / `isTbamlProduct` flags computed from
the selected product (the boolean source is product metadata — likely a
flag on `TmX_Product` row or a category lookup; needs verification at
slice-4 build time).

| Branch | FE call | Backend handler | Project / file / line | Returns |
|---|---|---|---|---|
| `isKycProduct === true` | `GET /api/v1/Integration/GetKYC/{customerId}` | `IntegrationController.GetKYC` | TMX.WebAPI / `Controllers/V1/IntegrationController.cs:132-170` | `{ riskScore, customerName }` — minimal. |
| ↳ gateway hop | (server-to-server HTTP) | gateway `DLP/GetRiskScore` | tmx-finance-integrations | — |
| ↳ upstream call | `IcbcService.GetKYC` | tmx-finance-integrations / `IcbcService.cs:156-200` | HTTP GET to `BRAINSKYCUrl` (BRAINS HTTP service). Sync. |
| `isTbamlProduct === true` | `GET /api/v1/Integration/GetCustomerByCustomerId/{customerId}` | `IntegrationController.GetCustomerByCustomerId` | TMX.WebAPI / `Controllers/V1/IntegrationController.cs:173-211` | Full `CustomerMasterModel` (~30 fields). |
| ↳ gateway hop | (server-to-server HTTP) | gateway `DLP/GetCustomerId` | tmx-finance-integrations | — |
| ↳ upstream call | `IcbcService.GetCustomerById` | tmx-finance-integrations | HTTP GET to upstream customer-master service. Sync. |

The two branches are **mutually exclusive**. The product class determines
which one fires — it's not "fall back from one to the other".

**TBAML** = Trade-Based Anti-Money Laundering. Regulated trade-finance
products (Letters of Credit, Bills for Collection, etc.) where enhanced
customer due-diligence is mandated by AML regulators — full master record
required, not just a risk score.

### Step 2.2 — Transaction Create

After the screening lookup succeeds, the FE immediately fires
`POST /api/v1/Transaction/Create` with the new transaction payload
(populated with `RiskScore` + `CustomerName` from KYC, OR full master
fields from TBAML lookup).

**Handler**: `TransactionController.Create(TransactionViewModel VM)` —
TMX.WebAPI / `Controllers/V1/TransactionController.cs:71-124`.

```csharp
// Line 82
VM.ProcessInstanceId = Guid.NewGuid();

// Line 85 — INSERT into TmX_Transaction
TransactionService.Create(VM.ToServiceModel(VM));

// Line 88 — generates TransactionNumber, sets IsWorkflowAttached, UPDATEs row
FillAndSaveWFParamter();

// Line 94 — product gates the workflow scheme
string workflowSchemeCode = productService.GetById(VM.ProductId).WorkflowSchemeCode;

// Line 95 — instantiates the OptimaJet workflow process at scheme's initial state
base.CreateWorkflowIfNotExists(VM.ProcessInstanceId, workflowSchemeCode);
```

**What's persisted**:

- `TmX_Transaction` row with `Process_Instance_ID = VM.ProcessInstanceId` and
  `Transaction_Status_Lkp_ID = <"Draft" lookup>`.
- `WorkflowProcessInstance` row with `Id = ProcessInstanceId`, `SchemeId =
  <product's scheme>`, `StateName = "Draft"` (or whatever the scheme's
  initial state is).

**Returned to FE**: `{ TransactionId, IsWorkflowAttached, ProcessInstanceId }`.
The FE stores `ProcessInstanceId` and uses it for all subsequent workflow
calls.

**Still no FCCM case submission yet.** The BRAINS lookup in Step 2.1 was
a cached read; FCCM case provisioning happens later in Phase 4 (after the
user advances the workflow to a finalising state). The transaction exists
in Draft; nothing has been submitted to FCCM.

### Step 2.3 — Dynamic form loads (3 parallel calls)

Once Create returns, the FE loads the dynamic form for editing —
`useTradeRepositoryAdd.ts:loadDynamicForm`. Three parallel GETs:

- `GET /api/v1/Transaction/{id}` — re-fetches the just-created transaction.
- `GET /api/v1/Entity/TabEntityMapping` — tab-to-entity field mapping.
- `GET /api/v1/TenantFieldSetup/GetFieldsByProduct/{productId}/{culture}` — per-product form schema. Defines tabs, fields, validation, AND the workflow `Commands` array (which renders as the footer-bar buttons).

These are pure reads; no upstream calls.

---

## Phase 3 — User clicks a workflow command (e.g. "Submit")

FE fires `PUT /api/v1/Transaction/ExecuteWf` with body:

```json
{ "ProcessId": "<guid>", "Command": "Submit", "Comments": "..." }
```

**Handler**: `TransactionController.ExecuteCommand(CommonModel common)` —
TMX.WebAPI / `Controllers/V1/TransactionController.cs:215-358`.

Sequence inside the handler:

1. **Line 229** — `GetWfCommand(common)` resolves the requested command name to an OptimaJet `WorkflowCommand` object.
2. **Line 282** — calls inherited `WorkflowController.ExecuteCommand(wfCommand, common)` — `WorkflowController.cs:94-173`.
3. **Line 131 (in base)** — `WorkflowInit.Runtime.ExecuteCommand(wfCommand, userId, userId)` — OptimaJet engine evaluates conditions, fires the transition's actions, advances state. New row in `WorkflowProcessTransitionHistory`.
4. **Line 287** — back in `TransactionController.ExecuteCommand`, computes `transition.IsFinalised` — true only when the transition lands on a terminal scheme state.
5. **Line 291** — `TenantImplementationModel.GetTenantImplementation(tenant)` returns the ICBC-specific subclass.
6. **Line 292** — `implementation.PostTransactionWFExecution(wfCommand, transactionModel, prevStatusLkp, statusLkp.LookupId)` — the post-workflow hook. **This is where KYC actually fires.**

**`IcbcImplementation.PostTransactionWFExecution`** —
TMX.WebAPI / `ImplementationModels/IcbcImplementation.cs:14-28`:

```csharp
public override async Task<...> PostTransactionWFExecution(WorkflowCommand cmd, TransactionViewModel vm, ...)
{
    return await base.PostTransactionWFExecution(cmd, vm, ...);  // base handles routing
}
```

Base implementation —
TMX.WebAPI / `ImplementationModels/TenantImplementationModel.cs:223-260`:

```csharp
// Line 230 — only fire core integration when the transition is final
if (transition.IsFinalised)
{
    // Line 232 — routes to the integrations gateway
    await IntegrationController.PostTransactionToCore(transactionVM);
}
```

**`IntegrationController.PostTransactionToCore`** —
TMX.WebAPI / `Controllers/V1/IntegrationController.cs:1415+`. Forwards to
the gateway. The gateway then routes by product type to the KYC project or
to the core-bank API.

**This is where the KYC vs non-KYC fork lives.** See Phase 4.

---

## Phase 4 — Product-type fork: KYC vs non-KYC

The fork is **driven by `ProductId`** in `caseInsertionController.cs`:

**`Integration KYC / Controllers/caseInsertionController.cs:35`**:

```csharp
public async Task<IHttpActionResult> Post(RepoPayloadModel jsondata)
{
    // Line 40 — KYC products gate to FCCM submission
    if (jsondata.ProductId.Equals(5))     // 5 = the "KYC" product type
    {
        // 1. Submit case to FCCM (HTTP)
        var caseRequest = ...
        var responseFromKycService = await httpClient.PostAsync(KYCOnboardingURL, caseRequest);
        // (lines 60-66)

        // 2. Poll FCC_OB_RA.CASE_ID for up to 20 seconds
        var caseId = await GetCaseIdByRequestId(ValidResponse.RequestID);
        // → caseInsertionController.cs:236-281: SELECT CASE_ID FROM FCC_OB_RA WHERE REQUEST_ID = @id
        //   in a 1-second-Task.Delay loop, max 20 iterations

        // 3. Once case is created, write FCCM-derived risk to local detail row
        await UpdateRiskInRepo(ValidResponse.RequestID, jsondata);
        // → caseInsertionController.cs:308-385:
        //   1. SELECT RISK_CATEGORY_KEY FROM FCC_OB_RA WHERE REQUEST_ID = @id
        //      (Oracle read from FCCM)
        //   2. UPDATE TmX_Transaction_Detail.UDF_Data JSON with the risk value
        //
        //   NB: There's a SEPARATE legacy SP `EXEC CBS_REQUEST` referenced
        //   from IcbcBrainsController.cs:30 — but that's a server-internal
        //   read against LOCAL tables (TmX_Transaction +
        //   TmX_Transaction_Detail + TmX_Customer_Master). Despite the "CBS"
        //   prefix and the "BRAINS" controller name, it does NOT call any
        //   upstream — it just denormalises previously-stored risk into a
        //   { custNo, custName, riskLevel, kycNo } DTO. Both names are
        //   misleading legacy artefacts; the new backend replaces this SP
        //   with a LINQ query, not an upstream client.
    }
    else
    {
        // Non-KYC products → core-bank API path. The FE revamp doesn't
        // exercise this branch (per Slice 4 audit). Out of scope for the
        // new backend.
    }
}
```

### KYC product flow (ProductId == 5)

```
FE click "Submit"
  → PUT /api/v1/Transaction/ExecuteWf
    → TransactionController.ExecuteCommand              [TMX.WebAPI]
      → WorkflowRuntime.ExecuteCommand                  [OptimaJet]
         (state transition, scheme actions fire)
      → if transition.IsFinalised:
          → IcbcImplementation.PostTransactionWFExecution
            → IntegrationController.PostTransactionToCore
              → integrations gateway
                → caseInsertionController.Post          [Integration KYC]
                  → POST KYCOnboardingURL              (FCCM HTTP — case submit)
                  → GetCaseIdByRequestId               (BLOCK 20s polling Oracle FCC_OB_RA)
                  → UpdateRiskInRepo                   (SELECT FCC_OB_RA.RISK_CATEGORY_KEY)
                  → write risk to TmX_Transaction_Detail.UDF_Data
                  
                  NB: BRAINS HTTP itself isn't called inside this flow.
                  BRAINS is only called from the Phase 2.1 GetKYC lookup.
                  The risk written here comes from FCCM Oracle.
              ← case_id + risk_score
          ← post-execution result
        ← workflow state now Final + KYC submitted
      ← updated transaction
    ← FE gets 200 with new state

Later, asynchronously:
  FCCM finishes case review (manual or automated)
  → POST {our backend}/UpdateTransactionStatus      [webhook into backend]
    → updateTransactionStatusAPIController.Post     [Integration KYC project]
      → updates TmX_Transaction.Status_Lkp
      → optionally fires another workflow command to advance
```

### Non-KYC product flow

Same shape until `caseInsertionController.Post`, where the `if (ProductId == 5)`
branch isn't taken. Routes to core-bank API in legacy. **The FE revamp
doesn't trigger this path** — every transaction the revamp creates is a
KYC product. Confirmed in Slice 4 audit.

---

## Phase 5 — Workflow scheme determines KYC step placement

The product's `WorkflowSchemeCode` (set on the Product master row) maps
to one of the rows in `WorkflowProcessScheme`. Different schemes have
different state graphs.

**Example — `TransactionKYCScheme` (illustrative; verify scheme XML in DB)**:

```
┌─────────┐  Submit   ┌──────────────┐  Authorize ┌──────────┐  ----final---->┌──────────┐
│  Draft  │ ────────► │  PendingKYC  │ ────────► │ Approved │ Post-WF hook ─►│Submitted │
└─────────┘           └──────────────┘            └──────────┘  fires KYC     └──────────┘
                                                                  ↓
                                                       caseInsertionController.Post
```

The actual scheme XML lives in `WorkflowProcessScheme.Scheme` (ntext column,
GET via OptimaJet's `DesignerAPI`). To see the live scheme:

```sql
SELECT Code, SchemeCode, Scheme
FROM   dbo.WorkflowProcessScheme
WHERE  Code = 'TransactionKYCScheme';
```

The 21 actions in `tmx-finance-backend/TMX.Workflows/Runtime/WorkflowActions.cs`
are referenced from inside scheme XML's `<Action>` nodes. The KYC submission
isn't one of those 21 actions — it's wired via `PostTransactionWFExecution`
post-hook (NOT a scheme action). This means the workflow engine doesn't
know about KYC; the post-hook decides based on `transition.IsFinalised`.

---

## Phase 6 — FCCM webhook callback

When FCCM finishes a case (approve / reject), it POSTs back into the
ICBC backend. Handler:

**`Integration KYC / Controllers/updateTransactionStatusAPIController.cs:24-96`**:

```csharp
public async Task<IHttpActionResult> Post(updateTrnRequest Request)
{
    // Request.actionCode: 30004 = approve, 30003 = reject
    // Updates TmX_Transaction.Status_Lkp_ID accordingly
    // No automatic workflow advance — that requires a separate user action
}
```

---

## Summary table — call hops by phase

| Phase | FE call | Project | File | Key call |
|---|---|---|---|---|
| 1 | (no API on page load) | — | — | — |
| 2.1 (KYC product) | GET /Integration/GetKYC/{customerId} | TMX.WebAPI | IntegrationController.cs:132 | gateway DLP/GetRiskScore → IcbcService.GetKYC → BRAINSKYCUrl HTTP |
| 2.1 (TBAML product) | GET /Integration/GetCustomerByCustomerId/{customerId} | TMX.WebAPI | IntegrationController.cs:173 | gateway DLP/GetCustomerId → IcbcService.GetCustomerById → upstream HTTP |
| 2.2 | POST /Transaction/Create | TMX.WebAPI | TransactionController.cs:71 | INSERT + WorkflowRuntime.CreateInstance |
| 2.3 | GET /Transaction/{id}, /Entity/TabEntityMapping, /TenantFieldSetup/GetFieldsByProduct/... | TMX.WebAPI | (dynamic-form) | 3 parallel reads — schema JSON for the editor |
| 3 | PUT /Transaction/ExecuteWf | TMX.WebAPI | TransactionController.cs:215 | WorkflowRuntime.ExecuteCommand → IsFinalised check → PostTransactionWFExecution |
| 4 (KYC product, after final) | (no FE call — server orchestration) | Integration KYC | caseInsertionController.cs:35 | POST KYCOnboardingURL (FCCM HTTP) + 20s poll FCC_OB_RA (Oracle) + write risk to UDF_Data |
| 6 | (FCCM → backend webhook) | Integration KYC | updateTransactionStatusAPIController.cs:24 | UPDATE TmX_Transaction.Status_Lkp |

The legacy SP `EXEC CBS_REQUEST` is server-internal (called from
`IcbcBrainsController.cs:30`); not in the FE call path. It reads only
local tables — see Phase 4 NB.

---

## What the new backend changes — domain-named URLs, vendor-named adapters

The new backend follows a strict layering that the legacy `Integration*`
naming hid: **URLs and Application contracts speak the domain language;
upstream-vendor names live in Infrastructure adapters only**. So
"BRAINS" and "FCCM" never appear in any URL the FE calls and never appear
in any contract that handlers consume.

```
┌──────────────────────────────────────────┐
│  URL (FE-facing)        Domain-named     │
│  /api/v1/Customer/{id}/Kyc               │
│  /api/v1/Kyc/Case/...                    │
└────────────────┬─────────────────────────┘
                 │
┌────────────────▼─────────────────────────┐
│  Application contract   Domain-named     │
│  IKycScreeningService                    │
│  ICustomerMasterService                  │
│  IKycCaseService                         │
└────────────────┬─────────────────────────┘
                 │  (DI swap)
┌────────────────▼─────────────────────────┐
│  Infrastructure         Vendor-named     │
│  BrainsKycScreeningService               │
│  CustomerMasterClient                    │
│  FccmKycCaseService (HTTP + Oracle +     │
│                      poller hosted-svc)  │
└──────────────────────────────────────────┘
```

### Per-phase changes

1. **Phase 2.1 (Customer / KYC lookup)**: legacy URLs
   `/Integration/GetKYC/{id}` and `/Integration/GetCustomerByCustomerId/{id}`
   → new domain-named:
   - `GET /api/v1/Customer/{customerId}/Kyc` — replaces GetKYC. Backed by
     `IKycScreeningService` (impl: `BrainsKycScreeningService`).
   - `GET /api/v1/Customer/{customerId}` — replaces GetCustomerByCustomerId.
     Backed by `ICustomerMasterService` (impl: `CustomerMasterClient`).
   The "Brains" name appears only in the Infrastructure adapter; the FE
   sees a clean Customer-resource URL.

2. **Phase 3 (workflow Execute)** consolidates to
   `PUT /api/v1/Workflow/Process/{processId}/Execute` (Slice 5). Same
   OptimaJet `ExecuteCommandAsync` underneath, but exposed once on
   `WorkflowController` instead of duplicated per domain controller.

3. **Phase 4 (FCCM case submission + 20s blocking poll)** → new
   `KycCaseController` with three endpoints, backed by `IKycCaseService`
   (impl: `FccmKycCaseService` which composes `FccmHttpClient` for submit,
   `FccmOracleReader` for status reads, and `FccmCaseIdPoller :
   BackgroundService` for async polling):
   - `POST /api/v1/Kyc/Case` — accepts a payload, returns
     `{ requestId, status: "AwaitingCaseId" }` immediately. NO blocking.
   - `GET  /api/v1/Kyc/Case/{requestId}` — FE polls this for status
     transitions: AwaitingCaseId → CaseCreated → RiskAssessed → terminal.
   - `POST /api/v1/Kyc/Case/Callback` — replaces
     `updateTransactionStatusAPIController.Post`. FCCM webhook destination.

4. **Phase 4's product-fork logic moves from code into the workflow
   scheme.** Instead of `if (ProductId == 5)` in `caseInsertionController.cs:40`,
   the workflow scheme for KYC products contains a `SubmitKycCase` action
   that calls `IKycCaseService.SubmitAsync(...)` from the action body.
   Non-KYC schemes don't reference the action and so never trigger the
   submission. The action lives in `Infrastructure/Workflow/Actions/`,
   registered with the `IWorkflowActionProvider`. Hardcoded magic-number
   gone.

5. **The legacy `EXEC CBS_REQUEST` SP becomes a LINQ query** in
   `Application/Features/Transactions/GetRiskSummary/`. No
   `CbsController`, no `ICbsClient`. The SP body is a pure projection
   over local tables (TmX_Transaction × TmX_Transaction_Detail ×
   TmX_Customer_Master) with a CASE expression on JSON values — trivially
   reproducible in EF Core 8.

6. **The `IcbcBrainsController` legacy controller does not survive.** It
   was named after BRAINS but actually called the local-only CBS_REQUEST
   SP — two layers of misleading naming. The new backend has no
   controller-level analogue; the LINQ projection above is invoked
   wherever it's needed (currently nowhere from the FE — it was
   server-internal in legacy).

---

## How to navigate this in code (quick reference)

When debugging a flow:

- **"Where does Transaction Create kick off the workflow?"** → `TransactionController.cs:94-95`.
- **"Where does the product determine the scheme?"** → `ProductService.GetById(productId).WorkflowSchemeCode`.
- **"Where does the workflow command get dispatched to the engine?"** → `WorkflowController.cs:131` (`WorkflowInit.Runtime.ExecuteCommand`).
- **"Where does KYC actually fire after a workflow transition?"** → `IcbcImplementation.cs:14` → base at `TenantImplementationModel.cs:230` → `IntegrationController.PostTransactionToCore`.
- **"Where's the 20-second blocking poll?"** → `caseInsertionController.cs:236-281` (Oracle SELECT against FCC_OB_RA in a Task.Delay loop).
- **"Where's BRAINS HTTP actually called?"** → `IcbcService.GetKYC` at `tmx-finance-integrations/IcbcService.cs:156-200` (HTTP GET to `BRAINSKYCUrl`). NOT `caseInsertionController` — that talks to FCCM Oracle, not BRAINS.
- **"What does `EXEC CBS_REQUEST` actually do?"** → `IcbcBrainsController.cs:30` calls it; the SP body is a local SELECT joining `TmX_Transaction × TmX_Transaction_Detail × TmX_Customer_Master` with a CASE on `udf_data.RiskScore`. No upstream call. Misleading name.
- **"Where do FCCM callbacks come back?"** → `updateTransactionStatusAPIController.cs:24-96`.

---

*Last updated: 2026-05-05.*

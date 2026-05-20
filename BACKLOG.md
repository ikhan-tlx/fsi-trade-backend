# Backlog — Future Work

Single source of truth for things we've consciously **deferred**. Each item is
something we discussed, scoped, and decided not to ship in the current slice —
but that needs to land eventually. Pick from here when planning the next slice.

> **Adding a new endpoint?** Follow [API_GUIDELINES.md](./API_GUIDELINES.md). It has the
> URL conventions, status-code mapping, validation pattern, RBAC seed discipline, and a
> PR checklist. Don't reinvent — that doc is the result of every decision made in this slice.

> **Modularisation (post-revamp)**: see [docs/MODULARISATION_PLAN.md](./docs/MODULARISATION_PLAN.md)
> for the licensed-modular-monolith plan that lets banking customers install only the
> modules they paid for (Tier 1: Compliance Lite → Tier 4: Enterprise). Execution
> deferred until revamp completes + 60 days production-stable. Discipline points to
> maintain through the rest of the revamp are in §6 of that doc — apply at code review.

## Conventions

- **Slice tag**: `slice-2`, `slice-3`, `slice-N` — when we expect to ship.
- **Effort**: rough t-shirt size (S/M/L) — not a commitment.
- **Status**: `📥 Open` → `🔄 Picked up` → `✅ Done` (then move to a "Done" section at the bottom or delete).
- New items go at the top of the relevant section.

---

## 🔐 Auth & Identity (slice 2)

### `📥 Open` Active_Flag handling on RBAC tables (slice-2 / S)

- The new backend currently IGNORES `Active_Flag` on `TmX_Role`,
  `TmX_Privilege`, `TmX_Role_Privilege_Mapping`, and `TmX_User_Role_Mapping`
  reads (per FSI direction, May 2026). All rows are treated as live.
- Once lifecycle semantics are formalised (does inactive mean "soft-deleted",
  "scheduled-disabled", "audit-only"?), revisit:
  - `PrivilegeService.GetPrivilegesForRolesAsync` — add `WHERE Active_Flag = 1` on the joins.
  - `RoleQueryService.GetRoleNamesAsync` — same.
  - `ListPrivilegeEntitiesQueryHandler` — same.
  - User CRUD — decide whether deactivating a role implicitly deactivates its
    user-role mappings, or whether they need a separate flag flip.
  - Effective_Start_Date / Effective_End_Date semantics — same question.

### `📥 Open` Remove the BootstrapAdminRoles escape hatch (slice-2 / XS)

- The `database/seed/rbac_grants.sql` file already wires every privilege code
  to the IT Admin role idempotently. Running it on each environment makes the
  `BootstrapAdminRoles` escape hatch redundant for normal operation.
- Once `rbac_grants.sql` has been run on prod and verified, the operator should:
  1. Set `Auth:BootstrapAdminRoles` to `[]` in `appsettings.json` and restart.
  2. Verify IT Admin can still hit every endpoint they need to (proves grants are wired).
  3. Optionally remove the `BootstrapAdminRoles` short-circuit code in
     `RequiresPrivilegeAttribute.cs` entirely once we're confident.
- Keep the config option in code for now — useful for any future "we lost a
  privilege grant" recovery scenario (set it back to `["IT Admin"]`, restart,
  fix the data, clear it again).

### `📥 Open` RBAC grants discipline — keep `rbac_grants.sql` honest (ongoing / XS per endpoint)

- Whenever a new endpoint is added with `[RequiresPrivilege("X")]`, the
  developer MUST append a section to `database/seed/rbac_grants.sql`:
  - One row in the `@scoped` table (privilege definition).
  - One row in the `@grants` table (IT Admin gets the new privilege).
- Re-run the file on every environment as part of deploying the endpoint.
- A pre-merge check (manual or CI) should grep the diff for new
  `[RequiresPrivilege(...)]` strings and flag if `rbac_grants.sql` wasn't
  also updated. Cheap to add; high signal.

### `📥 Open` Slice 5 Phase 1.5 — bind workflow runtime to OptimaJet v21 (slice-5.5 / 1-2 days once verified)

Slice 5 shipped the scaffold (controller, contracts, DTOs, MediatR features, DI
registration, license-key config, RBAC privileges) but the OptimaJet-dependent
classes are **stubbed** because v21's API surface couldn't be verified at
authoring time. Three files need real bodies:

1. **`Infrastructure/Workflow/WorkflowRuntimeFactory.cs`** — restore the
   `Lazy<WorkflowRuntime>` bootstrap. Verify in v21 IntelliSense:
   - `WorkflowRuntime.RegisterLicense(string)` signature
   - `WorkflowBuilder<XElement>` constructor params
   - `WithBuilder / WithActionProvider / WithRuleProvider /
     WithPersistenceProvider / RegisterAssemblyForCodeActions / Start`
   - Persistence/scheme provider class names (`DbPersistenceProvider`,
     `DbSchemePersistenceProvider`, `DbXmlWorkflowGenerator`)
2. **`Infrastructure/Workflow/OptimaJetWorkflowEngine.cs`** — implement the 8
   methods against v21. The header has the binding checklist. The Inbox
   raw-SQL projection is reusable verbatim from the previous draft.
3. **`Infrastructure/Workflow/Rules/FsiWorkflowRuleProvider.cs`** — re-add
   `IWorkflowRuleProvider` interface implementation. Sync + async overload
   shape needs to match v21. The rule bodies (IsCreator / CheckRole / Boss)
   are reusable verbatim.

**`FsiWorkflowActionProvider.cs` is being iterated separately** — user is
already adjusting it to v21 (e.g. `IsConditionAsync` added). When it's
compile-clean, the rule provider follows the same pattern.

Symptoms while stubbed: every workflow endpoint returns 500 with a clear
"workflow runtime not bound" message. Slices 1-4 are unaffected and ship.
Customer-facing impact: workflow features non-functional; everything else
(auth, RBAC, lookup/config, integrations) works.

Estimated effort post-verification: 1-2 days. The architecture, DI wiring,
and controller surface are all stable; only the vendor method-call lines
need their v21-correct names + parameter shapes.

### `📥 Open` Wire Swagger to api-version groups (slice-N / XS — when v2 lands)

- Today's Swagger config has a hard-coded `o.SwaggerDoc("v1", ...)` block. With
  `Asp.Versioning.Mvc.ApiExplorer` already installed, switching to per-version
  Swagger docs is a small change in `Program.cs`:
  ```csharp
  var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
  builder.Services.AddSwaggerGen(o =>
  {
      foreach (var d in provider.ApiVersionDescriptions)
          o.SwaggerDoc(d.GroupName, new OpenApiInfo
          {
              Title   = "FSI.Trade.Compliance API",
              Version = d.ApiVersion.ToString()
          });
  });
  ```
- Defer until v2 is actually shipping — single-version Swagger is fine today.

### `📥 Open` Expose auth-policy state to FE (slice-2 / S)

- New endpoint `GET /api/v1/Auth/Policy` (anonymous) returning the subset of `AuthOptions` / `PasswordPolicyOptions` / `TwoFactorOptions` that the FE legitimately needs to know to render UX correctly: `restrictConcurrentLogin`, `requireDeviceIdHeader`, `twoFactorEnabled`, `enforceFirstPasswordChange`, etc.
- Lets the "Active sessions" page (Slice 1.5) display a hint at the top when the operator has concurrent-login restriction turned on.
- Lets the login screen pre-empt `otp_required` round-trips when 2FA is globally on for a 2FA-enrolled user.
- Stays a flat read-only DTO; never surface secrets like `JwtOptions.SigningKey`.

### `📥 Open` Forgot-password / email-link reset (slice-2 / M)

- Anonymous endpoint `POST /api/v1/Auth/ForgotPassword` (body: `{ username, email }`) — generates a short-lived reset token, emails the user a one-time link.
- Anonymous endpoint `POST /api/v1/Auth/CompletePasswordReset` (body: `{ resetToken, newPassword }`) — verifies the token, sets new password.
- Reset-token storage table or use ASP.NET Identity's `UserManager.GeneratePasswordResetTokenAsync` (data-protection-protected, no DB row).
- Requires an email provider (SendGrid / SMTP from config).
- Closes the second half of the recovery story (companion to `ResetExpiredPassword`).

### `📥 Open` "New device" notification (slice-2 / S)

- When a login lands on a never-before-seen `Device_ID` for that user, fire an email: "New sign-in detected from <city, country>. If this wasn't you, click here to revoke."
- "Revoke" link is a one-time URL pointing at `POST /api/v1/Auth/Sessions/{deviceId}` — revokes that device.
- Toggle via `Auth:NotifyOnNewDevice = true` (default off).

### `📥 Open` Trusted-device promotion on first 2FA (slice-2 / S)

- After a user successfully completes a 2FA challenge from a device, set `TmX_User_Device.Is_Trusted = 1`.
- Future logins from a trusted device skip the OTP gate (configurable: `TwoFactor:AlwaysRequireOtp = false` for opt-in).
- Saves users typing OTP from their own laptop every login.

### `📥 Open` Concurrent-session cap (slice-2 / S)

- Max N active devices per user (config: `Auth:MaxActiveDevicesPerUser = 5`).
- When a new device registers and the user is at the cap, the oldest-by-`Last_Seen_At` is auto-revoked.
- Audit row written for the auto-revoke.

### `📥 Open` Stale-device cleanup job (slice-2 / S)

- Hangfire recurring job (daily) deletes `TmX_User_Device` rows whose `Last_Seen_At` < now − 90 days.
- Cascade-revokes any refresh tokens still bound to those device rows.
- Config: `Auth:StaleDeviceDays = 90`.

### `📥 Open` Audit log retention / archival (slice-2 / M)

- `TmX_Login_Audit` grows fast. Decide: rolling DELETE (90/180/365 days), or partition by month + archive to cold storage (Azure Blob / S3).
- Add `Auth:AuditRetentionDays` config + a daily Hangfire job to enforce.

### `📥 Open` GeoIP enrichment of audit log (slice-3 / S)

- Lookup `Ip_Address` against MaxMind GeoLite2 at write time, store `Ip_City` + `Ip_Country` columns.
- Makes the audit log human-readable without manual whois.
- Free MaxMind feed updates monthly; cache in-process.

### `📥 Open` Admin cross-user session view (slice-3 / M)

- New `AdminController` (Bearer + admin-role required).
- `GET /api/v1/Admin/Users/{userId}/Sessions` — view any user's active devices.
- `DELETE /api/v1/Admin/Users/{userId}/Sessions` — kill all sessions for a user.
- `GET /api/v1/Admin/Audit?from=&to=&user=` — query audit log.

---

## 👤 User & RBAC (slice 2)

### `📥 Open` User CRUD admin endpoints (slice-2 / L)

- `UserController` with full CRUD: list (paged + sortable per FE Kendo shape), get-by-id, create, update, activate, deactivate. Per FE PROJECT.md §8.7.
- Slice 2's biggest single feature.

### `📥 Open` Role CRUD + privilege matrix (slice-2 / M)

- `RoleController` with CRUD + `Activelov` per FE.
- `PrivilegesController.GET /entities` — drives the role-edit privilege matrix UI.
- Connects existing `TmX_Role`, `TmX_Privileges`, `TmX_Role_Privilege_Mappings`, `TmX_User_Role_Mapping` to the new backend via EF entities.

### `📥 Open` Privilege claims in JWT (slice-2 / S)

- Currently the JWT only carries role NAMES. For per-action authorisation we'll either (a) carry privilege codes as claims, or (b) leave them out and resolve server-side per request via `IPrivilegeService`.
- Decision pending: token bloat vs round-trip per privilege check.
- Lean toward (b) for security + flexibility.

### `📥 Open` `[RequiresPrivilege("...")]` filter (slice-2 / S)

- Custom action filter that reads required privilege code from attribute and asks `IPrivilegeService.UserHas(callerUserId, code, ct)`.
- Replaces/enhances the legacy `PrivilegesAuthorizationAttribute`.

### `📥 Open` Self-service profile page support (slice-2 / S)

- `GET /api/v1/User/{id}` — for the FE `MyProfilePage`. Read-only profile.
- `GET /api/v1/User/{id}/Sessions` — alias for `/Auth/Sessions`? Or just point the FE at the auth endpoint. Decide.

---

## 🔄 Workflow / OptimaJet (slice 5)

### `📥 Open` Add explicit "Application Cancelled" state to active scheme XMLs (scheme-design / S)

**Context.** Slice 6 Step 5 cancel handler attempts
`IWorkflowEngine.SetStateAsync(..., "Application Cancelled", ...)` as the
"proper" cancellation path. Verified against `ImportWF` (and likely the other
schemes in this deployment): none of the schemes declare an
`Application Cancelled` state, so SetState is silently rejected by the
engine. The handler logs at INFO and falls back to:

1. Deleting all `WorkflowInbox` rows for the process — the rule provider's
   `CheckRole` gates on inbox membership, so this effectively makes the
   transaction invisible to all actors.
2. Flipping `Transaction_Status_Lkp` to the cancelled lookup id.

This works fine functionally (the user can't act on the transaction; the
grid shows it as cancelled), but the engine state stays at wherever it
was when cancellation was triggered. Audit trail / process history shows
the transaction as "stuck" in (e.g.) `AnalystActivity` even though it's
operationally dead.

**Proposed scheme change** (do via the OptimaJet workflow designer; updates
just the XML in `WorkflowProcessScheme`, no code changes):

- Add a new final activity `ApplicationCancelled` (IsFinal="True",
  State="ApplicationCancelled" or "Application Cancelled" — match whatever
  string `CancelledStateName` constant uses).
- Add an unrestricted auto-transition from every non-terminal activity
  to that new state. Use `IsForSetState="True"` on the activity so
  `SetState` is a valid operation.
- Apply to: `ImportWF`, `KYC_Workflow`, `HsCode_Price_WF`, and any other
  schemes used in production.

**Once the schemes are updated,** the existing cancel handler will start
moving the engine state cleanly — no backend code change required. The
inbox-cleanup + status-flip fallback also continues to work (idempotent).

### `📥 Open` Slice 8 — Notifications & Approval Services (post-Slice 6/7 / L)

**Why this matters.** Slice 5 Phase 2 ported every workflow action and condition,
but split them into three buckets. Bucket A (pure runtime) and Bucket B (DB-only)
ship as real implementations. **Bucket C handlers are no-op-with-log** because
they need integration services we haven't built yet. Slice 8 is where Bucket C
gets real implementations.

**Bucket C inventory** — every handler that currently logs a warning and
returns a safe default (`void` for actions, `false` for conditions):

**Notification actions** (8) — all post to an external DLP integration service
the legacy backend calls at `{IntegrationURL}/DLP/SendEmail` etc. Need a new
`IDlpNotificationClient` HTTP adapter, same pattern as `BrainsKycScreeningService`
/ `FccmHttpClient` from Slice 4:

- `SendEmail`, `SendEmailToReceiver`, `SendEmailToAllReceivers`,
  `SendEscalationEmailByRole`, `SendEmailToCustomer`, `SendEmailToAppReceiver`,
  `SendSMS`, `SendClientNotification`

Each one also needs:
- Lookups: `GetEmailByRoleBranchCommand(roleId, branchId, command) → email`
  (legacy `UserService.GetEmailByRoleBranchCommand`), email template by name
  (legacy `TemplateService.GetByName`).
- DTOs: `EmailRequestModel`, `SmsRequestModel`, `WorkflowEmailModel`,
  `AttachmentModel`, `EmailAttachmentModel`.
- Config: `Integration:Dlp:BaseUrl`, tenant header, retry policy via
  `Microsoft.Extensions.Http.Resilience`.

**Loan-app actions** (6) — currently no-op because the loan-application
domain isn't in Trade scope. If/when the bank wants to run loan workflows on
this engine, port these:

- `AreaWiseVerification`, `FillAppReceiversRecommendation`,
  `ClearRecommendations`, `FillVerificationsFromChecklist`,
  `AttachApplication`, `DeattachApplication`

**Loan-app conditions** (6) — same scope question:

- `PendingApprovals`, `RoleAmountSlabs`, `IsAmountInActorSlab`,
  `CheckWFDeviation`, `IsAppAttached`, `checkSecondApprovalRequiredForNoGuarantor`

**Implementation order if/when Slice 8 lands:**
1. Notification actions first — they're the common-path blockers for any
   workflow that emails on transitions.
2. `IApprovalService` port — backs `Approve` for multi-approver flows
   (current Approve has a working minimum; this would extend it).
3. Loan-app handlers — only if the bank decides Trade Compliance should
   also run loan flows.

**Right now, every Bucket C handler is fail-safe:** workflow advances cleanly,
side effect is silently skipped with a structured warning log entry. So this
slice isn't blocking any other work.

### `📥 Open` Replace OptimaJet runtime with an FSI-owned workflow engine (post-revamp / L)

**Why this matters.** Slice 5.6 pinned the workflow runtime to **OptimaJet 3.5.0**
for license parity with the legacy backend (the `techlogix2019-...` key
expired 2020-07-24 but v3.x has soft enforcement). This is the pragmatic
short-term answer but carries three structural risks:

1. **Licensing exposure.** Using v3.5.0 with an expired key is technically
   running an unlicensed commercial product. Legacy has been doing this for
   six years; the precedent is set, but in any procurement / compliance
   audit this is a real finding. Customers shipping the workflow module
   under the modular-monolith plan inherit the same risk.
2. **End-of-life dependency.** OptimaJet's v3.x line receives no bug-fix or
   CVE patches. For an internal trade-compliance system the security
   surface is low (no internet-facing workflow runtime), but it's a real
   consideration.
3. **No vendor support.** If anything breaks in production, OptimaJet's
   response will be "upgrade to v21+" — which puts us back to the
   procurement question we sidestepped.

**Proposed approach.** Write a minimal FSI-owned workflow engine against
the existing OptimaJet DB schema. Specifically:

- Read scheme XML from `WorkflowProcessScheme` (no format change).
- Parse `<Transition>`, `<Activity>`, `<Actor>`, `<Action>` nodes ourselves.
- Execute state changes via direct EF Core writes to
  `WorkflowProcessInstance`, `WorkflowProcessTransitionHistory`, and
  `WorkflowInbox` — the same tables OptimaJet writes to today.
- Port the 22 stubbed actions in `FsiWorkflowActionProvider` to invoke
  from our own runtime instead of OptimaJet's. Action provider contracts
  already live in our Application layer (`IWorkflowActionProvider` was
  *not* the path; we wrote our own contracts).
- The rule provider (3 rules: IsCreator, CheckRole, Boss) is already
  vendor-agnostic — composes our Application contracts (`IRoleQueryService`,
  `IApplicationDbContext`). Lifts as-is.
- Designer endpoint: re-host the OptimaJet designer XML editor as static
  FE assets if the scheme XML format stays compatible, OR ship a
  React-based replacement editor in the FE. Slice 5 Phase 2 territory.

**Effort estimate.** ~1-2 weeks of focused work (a dedicated slice). The
scheme XML format is the largest unknown — needs validation that we can
faithfully interpret it. Recommend a quick spike before committing.

**Strategic payoff.** Zero vendor lock-in, zero license cost across every
tier of the modular-monolith plan (Compliance Lite tier in particular no
longer requires *any* workflow license). The "license expiry on legacy"
trap that triggered this whole exploration goes away permanently.

**Dependencies.** Slice 6 + Slice 7 should stabilise first so we have the
complete picture of every workflow interaction surface. Then plan as a
focused slice with a 2-3 day spike up front.

### `📥 Open` Procure OptimaJet v21 license (parallel option to the FSI-owned engine)

**Why this matters.** Alternative to the FSI-owned engine: stay on
OptimaJet but pay for a current license. Cleans up the licensing exposure
immediately; trades effort for spend.

**Action**: contact OptimaJet sales for v21 pricing tiers (eval first,
then production subscription). Confirm whether the key tier covers
designer, sub-processes, scheme count we need, and whether multi-tenant
deployments share or split licenses (modular-monolith implications).

**If pursued**: upgrade NuGet packages from 3.5.0 → 21.x, restore the v21
API surface in `OptimaJetWorkflowEngine.cs` (parameters positional list,
TransitionClassifier enum, SetStateParams.Comment removed, async
designer), re-align `FsiWorkflowActionProvider` / `FsiWorkflowRuleProvider`
to v21 interface signatures. Most of this work is already documented from
the brief Slice 5.5 v21 detour — see git history.

---

## 🔌 Integrations (slice 4)

**Scope clarification (corrected May 2026).** FE source-code search confirms
the revamp calls exactly TWO `/Integration/*` URLs:

- `GET /api/v1/Integration/GetKYC/{customerId}` — fires when the user clicks
  "+ Add Transaction" for a **KYC product**. Returns `{ riskScore, customerName }`.
- `GET /api/v1/Integration/GetCustomerByCustomerId/{customerId}` — fires
  when the user clicks "+ Add Transaction" for a **TBAML product** (Trade-Based
  Anti-Money Laundering — enhanced due-diligence regulatory class). Returns
  full `CustomerMasterModel` (~30 fields).

The two are **mutually exclusive** based on product class. **Out of scope
for the new backend**: legacy ICBC core (CBS / IcbcService — not called by
revamp), tenant-bank clients (SilkBank, BOP, UBank, Askari, Samba, ABL,
Vitas — not referenced; only an `isBOP` UI flag).

### Architectural rule for Slice 4 — domain-named URLs, vendor-named adapters

The legacy `IntegrationController` umbrella name leaked nothing about
*what* an endpoint did. The new backend separates layers strictly:

```
URL                Application contract       Infrastructure adapter
(domain-named)     (domain-named, port)       (vendor-named)
─────────────────  ─────────────────────────  ────────────────────────
/Customer/{id}/Kyc IKycScreeningService       BrainsKycScreeningService
/Customer/{id}     ICustomerMasterService     CustomerMasterClient
/Kyc/Case          IKycCaseService            FccmKycCaseService
/Kyc/Case/{id}                                  + FccmHttpClient
/Kyc/Case/Callback                              + FccmOracleReader
                                                + FccmCaseIdPoller (HostedService)
                                                + KycCaseRequestRepository
```

The words "Brains" and "Fccm" appear ONLY in the Infrastructure adapters.
Vendor swaps in future = one DI line change; Application + URL stay.

### `📥 Open` Slice 4 deliverables (slice-4 / M-L)

**Read endpoints (FE-facing, on `CustomerController`)**:

- `GET /api/v1/Customer/{customerId}/Kyc` — replaces
  `Integration/GetKYC/{id}`. Returns `{ riskScore, customerName }`. Backed
  by `IKycScreeningService.GetKycForCustomerAsync(...)`. Implementation
  `BrainsKycScreeningService` HTTP-GETs `BRAINSKYCUrl`.
- `GET /api/v1/Customer/{customerId}` — replaces
  `Integration/GetCustomerByCustomerId/{id}`. Returns full customer
  master record. Backed by `ICustomerMasterService.GetByCustomerIdAsync(...)`.
  Used for TBAML products. Confirm at slice-4 Step 1 whether the upstream
  source is BRAINS or a separate customer-master service.

**Write endpoints (FE-facing, on `KycCaseController`)**:

- `POST /api/v1/Kyc/Case` — submit a KYC case for screening. Returns
  `{ requestId, status: "AwaitingCaseId" }` immediately (NO blocking).
  Backed by `IKycCaseService.SubmitAsync(...)`.
- `GET /api/v1/Kyc/Case/{requestId}` — FE polls this for status
  transitions (AwaitingCaseId → CaseCreated → RiskAssessed → terminal).
  Backed by `IKycCaseService.GetStatusAsync(...)`.
- `POST /api/v1/Kyc/Case/Callback` — replaces
  `updateTransactionStatusAPIController.Post`. FCCM webhook destination.
  Backed by `IKycCaseService.HandleCallbackAsync(...)`.

**Infrastructure adapters (vendor-named, behind DI)**:

- `BrainsKycScreeningService : IKycScreeningService` — HTTPS GET to
  `BRAINSKYCUrl`. Polly retry + circuit-breaker + 30s timeout.
- `CustomerMasterClient : ICustomerMasterService` — HTTPS GET to upstream
  customer-master service. Same Polly defaults.
- `FccmKycCaseService : IKycCaseService` — orchestrates three concerns:
  - `FccmHttpClient` — `POST KYCOnboardingURL` for case submission.
  - `FccmOracleReader` — Oracle queries against `FCC_OB_RA` for
    `CASE_ID` provisioning + `RISK_CATEGORY_KEY` retrieval.
  - `FccmCaseIdPoller : BackgroundService` — replaces the legacy 20-second
    blocking poll. Runs every 5s, advances `KycCaseRequest` rows from
    `AwaitingCaseId` → `CaseCreated` (and onward to `RiskAssessed`).
  - `KycCaseRequestRepository` — local CRUD for the new
    `KycCaseRequest` table that tracks pending requests.

**No `BrainsController`, `FccmController`, `IntegrationController`, or
`CbsController` in the new backend** — those would leak vendor names into
the URL surface.

### `📥 Open` Slice 4 Step 1 — Integration scope inventory (slice-4 / S)

Before writing code, confirm:

- Customer master upstream — is `GetCustomerByCustomerId` ultimately
  hitting BRAINS (same vendor) or a separate customer-master service?
  Verify by tracing `IcbcService.GetCustomerById` URL config.
- Per-FE TS interfaces in
  `src/features/tradeRepository/add/useTradeRepositoryAdd.ts` — confirm
  the exact response shapes the FE binds against, so the new DTOs match
  byte-for-byte (or, if we're cleaning up legacy verbosity, document the
  delta in `FE_CHANGES_REQUIRED.md`).

**Confirmed vendor mix (May 2026 trace)**:

- **BRAINS** is HTTP-only — GET to `BRAINSKYCUrl`. Front-line screening.
  Returns `{ RiskScore: "Low|Medium|High", CustomerName }`.
- **FCCM** is case-management — HTTPS submit + Oracle DB poll on
  `FCC_OB_RA` + HTTPS webhook back to us. Async case provisioning;
  legacy 20-second blocking poll replaced by `FccmCaseIdPoller`.
- The legacy SP `EXEC CBS_REQUEST` is NEITHER a CBS upstream call NOR a
  BRAINS wrapper. It's a local SELECT over `TmX_Transaction × TmX_Transaction_Detail × TmX_Customer_Master` with a CASE expression. New backend
  replaces it with a LINQ query in
  `Application/Features/Transactions/GetRiskSummary/`. No `CbsClient`
  abstraction needed.

### `📥 Open` Slice 4 Step 2 — FCCM background poller (slice-4 / M)

**Scope confirmed by legacy trace — exactly ONE polling pattern exists.**
Legacy `Integration KYC\Controllers\caseInsertionController.cs:236-281`
blocks the HTTP request thread up to 20 seconds polling
`FCC_OB_RA.CASE_ID` every 1 second waiting for FCCM to provision the case.

Replace with a proper async pattern:

1. **New table** `KycCaseRequest`:
   ```
   request_id        bigint identity PK
   customer_id       nvarchar(50)
   submitted_at      datetime
   fccm_case_id      nvarchar(100) NULL
   status            nvarchar(50)  -- AwaitingCaseId | CaseCreated | RiskAssessed | Failed | Timeout
   risk_score        int           NULL
   last_polled_at    datetime      NULL
   error_detail      nvarchar(500) NULL
   ```

2. **New endpoints** (on `KycCaseController` — domain-named, not vendor-named):
   - `POST /api/v1/Kyc/Case` — submit a KYC case. Inserts a `KycCaseRequest`
     row, returns the requestId immediately. Status = `AwaitingCaseId`.
     Backed by `IKycCaseService.SubmitAsync(...)`.
   - `GET  /api/v1/Kyc/Case/{requestId}` — current status + result. FE
     polls this until status reaches a terminal state. Backed by
     `IKycCaseService.GetStatusAsync(...)`.
   - `POST /api/v1/Kyc/Case/Callback` — FCCM webhook destination.
     Replaces legacy `updateTransactionStatusAPIController.Post`.

3. **Background service**: `FccmCaseIdPoller : BackgroundService`. Runs
   inside the API process. Every 5 seconds:
   - Selects pending `KycCaseRequest` rows (status = `AwaitingCaseId`).
   - For each, queries Oracle `FCC_OB_RA` for `CASE_ID` matching the request.
   - If found: sets `fccm_case_id`, transitions to `CaseCreated`.
   - If submitted > 5 minutes ago and still null: sets to `Timeout`.
   - Once at `CaseCreated`, optionally fetches the risk score (also from
     `FCC_OB_RA.RISK_CATEGORY_KEY`) and transitions to `RiskAssessed`.
   - All vendor/protocol details (HTTP, Oracle, etc.) are encapsulated in
     `FccmKycCaseService`'s composed clients (`FccmHttpClient`,
     `FccmOracleReader`). The poller itself is part of the
     `IKycCaseService` impl, registered as a hosted service so it runs
     alongside the API.

4. **Non-blocking** — request threads return immediately. FE handles
   "still pending" via UX (spinner / status badge).

5. The FE-facing `GET /api/v1/Customer/{customerId}/Kyc` (Slice 4 read
   endpoint) stays sync — it reads the latest cached KYC result from
   the BRAINS HTTP service. That endpoint is read-only and fast. The
   async flow above is exclusively for **submitting new KYC cases**,
   which is a separate concern from reading existing screening data.

This is the only place a background poller is needed — every other
upstream call in the legacy backend is sync.

### `📥 Open` Decommission unused tenant clients (slice-4 / S — cleanup)

- Delete `tmx-finance-integrations\Services\TenantServices\` for: SilkBank,
  BOP, U-Bank, Askari, Samba, ABL, Vitas (all four regions),
  `ApiHardcodedRespService`. Plus the legacy `IcbcService` itself.
- Confirm zero code references in the new backend before deletion.
- Tracked here separate from the active integration work because it's pure
  cleanup of legacy folders, not new functionality.

---

## 📊 Data & Reporting (slice 3)

### `📥 Open` Lookup endpoint (slice-3 / S)

- `GET /api/v1/Lookup/{culture}` — returns the global lookup blob keyed by `LookupType`. Per FE PROJECT.md §8.1.

### `📥 Open` Configurations endpoint (slice-3 / S)

- `GET /api/v1/Configurations/GetUserCompanyConfigurations`.

### `📥 Open` Reports trio (slice-7 / M)

- `POST /api/v1/Report/ReportHTML` — render via DotLiquid + SP.
- `PUT /api/v1/Report/GeneratePdfFromHtml` — HTML → PDF.
- `PUT /api/v1/Report/ReportExcel` — SP → XLSX via EPPlus.
- Note: PUT (not POST) for the binary endpoints — matches FE expectation.

---

## 💼 Trade Transactions + workflow architecture (slices 5 & 6)

**Scope clarification (corrected May 2026 after end-to-end legacy trace).**

The legacy `LoanApplicationController` hosts THREE kinds of endpoints,
mixed together:

1. **Generic workflow endpoints** that don't touch loan-specific code:
   - `/ApplicationInbox` — queries generic `TmX_Application_VW` view, returns
     generic `ApplicationVWModel` DTO. **No loan-specific fields.** Same query
     would work for AccountApplication and Transaction inboxes.
   - `/GetCommandsByProcessId/{id}` — 3-line passthrough to
     `WorkflowRuntime.GetAvailableCommandsAsync`. Pure runtime call.
   - `/Workflow`, `/Workflow/ProductMapping` (GET + POST), `/Workflow/Designer` —
     all generic workflow plumbing used by the Process Designer module.

2. **Workflow command execution** dispatched from the Inbox via
   `${targetApiController}/ExecuteTransactionWF`. Today every domain
   controller exposes its own copy of this (LoanApplication, AccountApplication,
   Transaction). The implementation is identical across the three — call
   `runtime.ExecuteCommand(...)` with the process ID + command name.

3. **Loan-domain CRUD** — actually loan-specific (loan amount, interest rate,
   term, etc.). NOT used by the FE revamp at all. The revamp's transaction
   module (`tradeRepository`) replaced loan applications conceptually.

**Architectural decision: workflow lives on its own**.

- Generic endpoints (1) move to a new dedicated `WorkflowController` in
  Slice 5. They are NOT replicated across domain controllers.
- The dynamic-controller pattern in `InboxPage.tsx:307-316` collapses to a
  single URL: `PUT /api/v1/Workflow/Process/{processId}/Execute`. The
  workflow runtime knows which scheme is running for the process and
  applies the command — the FE no longer needs to map work-item type to
  controller name.
- Loan-domain CRUD (3) is OUT OF SCOPE for the revamp. The FE doesn't
  call any loan-specific endpoints — only the inbox + process designer
  surfaces the legacy controller hosted, which are generic workflow.

### `📥 Open` Slice 5 — Workflow as standalone entity (depends on OptimaJet license)

Single dedicated controller + Application contract + Infrastructure impl.
See `docs/OPTIMAJET_POC.md` for the full POC.

| New endpoint | Replaces legacy |
|---|---|
| `GET   /api/v1/Workflow/Inbox` (paged, sortable) | `/LoanApplication/ApplicationInbox` |
| `GET   /api/v1/Workflow/Process/{id}/Commands` | `/LoanApplication/GetCommandsByProcessId/{id}` |
| `PUT   /api/v1/Workflow/Process/{id}/Execute` | `/{controller}/ExecuteTransactionWF` (3 paths collapse to 1) |
| `PUT   /api/v1/Workflow/Process/{id}/Execute` (also covers Transaction edit page) | `/Transaction/ExecuteWf` |
| `GET   /api/v1/Workflow/Schemes` | `/LoanApplication/Workflow` |
| `GET, POST /api/v1/Workflow/ProductMapping` | `/LoanApplication/Workflow/ProductMapping` |
| `GET, POST /api/v1/Workflow/Designer` | `/LoanApplication/Workflow/Designer` |

Application contract:

```csharp
public interface IWorkflowEngine
{
    Task<WorkflowInstanceCreationResult> CreateInstanceAsync(string schemeCode, Guid processId, string identityId, IDictionary<string,object>? parameters, CancellationToken ct);
    Task<WorkflowExecutionResult>        ExecuteCommandAsync(Guid processId, string identityId, string command, IDictionary<string,object>? parameters, CancellationToken ct);
    Task<IReadOnlyList<WorkflowCommandDto>> GetAvailableCommandsAsync(Guid processId, string identityId, CancellationToken ct);
    Task<PagedResult<WorkflowInboxItemDto>> GetInboxForUserAsync(string identityId, PagedQuery query, CancellationToken ct);
    Task<DesignerResponseDto> ProcessDesignerRequestAsync(IDictionary<string,string> formParams, CancellationToken ct);
    // ... rest per POC
}
```

Infrastructure implementation `OptimaJetWorkflowEngine` wraps `WorkflowRuntime`.
Domain controllers (Transaction, etc.) inject `IWorkflowEngine` and call it
internally for create/advance — they do NOT expose generic workflow endpoints.

### `📥 Open` Slice 6 — Transaction CRUD (depends on 4 + 5)

Endpoints the FE calls (`src/features/tradeRepository/api/`):

- `GET    /api/v1/Transaction` (paged Kendo grid)
- `POST   /api/v1/Transaction/Create` — creates row + kicks off workflow via IWorkflowEngine
- `GET    /api/v1/Transaction/{id}`
- `PUT    /api/v1/Transaction/{id}` (REST shape; legacy ID-in-body delta logged in FE_CHANGES)
- `GET    /api/v1/Transaction/{id}/DownloadApplicationPDF`

The legacy `PUT /Transaction/ExecuteWf` is replaced by the generic Slice 5
workflow endpoint `PUT /api/v1/Workflow/Process/{id}/Execute`. FE delta
logged.

**Hard dependencies before this slice can ship**: Slice 4 (Integrations),
Slice 5 (Workflow + OptimaJet).

### `📥 Open` LoanApplication and AccountApplication CRUD — confirmed OUT OF SCOPE

The FE revamp doesn't call any loan-specific or account-application-specific
CRUD endpoints. The dynamic-controller pattern that referenced
`LoanApplication` and `AccountApplication` is replaced by the generic
workflow controller. **No `LoanApplicationController` or
`AccountApplicationController` will be built in the new backend.** Confirmed
by FE source-code search May 2026.

### `📥 Open` Transaction CRUD (slice-6 / XL)

Endpoints the FE calls (`src/features/tradeRepository/api/`):

- `GET    /api/v1/Transaction` (paged Kendo grid)
- `POST   /api/v1/Transaction/Create`
- `GET    /api/v1/Transaction/{id}`
- `PUT    /api/v1/Transaction` (legacy ID-in-body shape — backend will convert
  to REST `PUT /Transaction/{id}` and log delta in FE_CHANGES_REQUIRED)
- `PUT    /api/v1/Transaction/ExecuteWf` — workflow command
- `GET    /api/v1/Transaction/{id}/DownloadApplicationPDF`

**Hard dependencies before this slice can ship**:

- Slice 4 (Integrations) — `/api/v1/Integration/GetKYC/{customerId}` and
  `/api/v1/Integration/GetCustomerByCustomerId/{customerId}` are called from
  the Transaction add/edit flow.
- Slice 5 (OptimaJet 8.x port) — `ExecuteWf` is workflow command execution.
  Dynamic form schema's `Commands` array drives the footer-bar buttons.
- Field-binding endpoints (see below) — drive the entire dynamic-form UI.

### `📥 Open` Field-binding endpoints for dynamic forms (slice-3 or 4 / S each)

The FE Transaction module is dynamic-form-driven. Two read endpoints feed it:

- `GET /api/v1/Entity/TabEntityMapping` — field → table mapping. Drives which
  entity owns which form field. Likely backed by `TmX_Entity_*` tables; needs
  schema inventory before scoping.
- `GET /api/v1/TenantFieldSetup/GetFieldsByProduct/{productId}/{culture}` —
  per-product form schema. Defines tabs, fields, validation, visibility rules,
  and workflow `Commands`. Largest deliverable in this pair — the response
  shape is the contract for the entire Transaction edit UI.

Both are pure reads; no writes from this slice. Decide whether to ship in
Slice 3 (alongside Lookup + Configurations), Slice 4 (alongside Integrations),
or in their own pre-Slice-6 prep — depends on schema scope.

---

## 🧹 Cleanup & Tech Debt

### `📥 Open` Evaluate UUID v7 for new Guid-keyed tables (spike / S-M)

**Why this matters.** .NET's `Guid.NewGuid()` produces UUID v4 — fully random
bytes. For columns that are clustered indexes, random Guids cause page splits,
fragmentation, and reduced cache locality on insert. UUID v7 prefixes a Unix-ms
timestamp to the bytes, so inserts append to the end of the index — same shape
as `int IDENTITY`.

**Where this would help in our codebase:**
- `KycCaseRequest` PK (likely clustered uniqueidentifier) — every KYC submit creates a new row, sustained insert pattern.
- `RefreshTokens` PK (verify if clustered) — high-volume token rotation.
- Any future Guid-PK table we introduce (e.g. audit detail tables, async job tables).

**Where it would NOT help and we should keep `Guid.NewGuid()` (v4):**
- `Process_Instance_Id` on `TmX_Transaction` — OptimaJet's runtime maps these to its own `WorkflowProcessInstance` table where the PK is `NONCLUSTERED`. Vendor expects v4 Guids; mixing v7 here would create inconsistent ordering with engine-generated Guids in the same column.
- `WorkflowProcessScheme.Id` — vendor-managed, can't change.
- `WorkflowInbox.Id` (writes from `FillAllUsersBucket` / `FillApprovers`) — non-clustered index per the schema dump, so the fragmentation hit is minor.

**Implementation paths:**
- **.NET 9** ships `Guid.CreateVersion7()` natively. Cleanest path — upgrade when feasible.
- **.NET 8** (where we are): use NuGet package `Medo.Uuid7` OR roll a ~30-line in-house generator. Both produce valid v7 Guids compatible with `Guid` storage.

**Action items if/when this is picked up:**
1. Spike: profile current insert performance on `KycCaseRequest` + `RefreshTokens` under sustained load. Confirm fragmentation is measurable.
2. If yes, introduce `IUuidGenerator` abstraction. Default impl uses `Guid.NewGuid()`; new impl uses v7. Inject where new Guids are minted in our code (avoid changing OptimaJet sites).
3. Rebuild indexes on the affected tables post-deploy to defragment historical data.

**Adoption trigger:** any new Guid-PK table created in future slices should use v7 by default. Existing tables only after a profiling spike justifies the migration.

### `📥 Open` Introduce `TmX_User.User_Identifier` (int identity) as the join key (post-revamp / L)

**Why this matters.** Today `TmX_User.User_ID` is `nvarchar(50)` and stores a GUID
text representation. That string is the FK target for ~40 tables (auth, RBAC,
workflow inbox, transactions, audit, refresh tokens, devices, etc.) plus the
join key inside `TmX_Transaction_VW`. Every authenticated request pays the cost
of NVARCHAR(50) joins — `PrivilegeService.GetPrivilegesForRolesAsync` does it
on the hot path. It also creates an ambiguity at the API surface: the JWT `sub`
must be a Guid-shaped string to JOIN through `WorkflowInbox.IdentityId`
(`uniqueidentifier` in v21), which is fragile and forces a `Guid.Parse` in
handlers like `ListInboxQueryHandler`.

**Proposed change.**

1. Add `User_Identifier int IDENTITY(1,1) NOT NULL UNIQUE` to `TmX_User`. Backfill from the existing rows. (Surrogate key — `User_ID` stays as the public string used in the JWT `sub` for now.)
2. New backend writes `User_Identifier` into freshly-created child rows where applicable (RefreshTokens, UserDevices, Login_Audit, Password_Change_Audit, new Transaction inserts).
3. Add a parallel column on each child table that needs to join fast: `RefreshTokens.User_Identifier`, `TmX_User_Role_Mapping.User_Identifier`, etc. Indexed.
4. Migrate read paths one slice at a time to JOIN on `User_Identifier` instead of `User_Id`. Keep `User_Id` populated for legacy backend compat.
5. Once legacy backend is dark + 30 days stable, drop `User_Id` from child tables and rename `User_Identifier` to `User_Id`. Final shape: int FKs everywhere.

**Why post-revamp.** ~40 FK migrations spanning auth, RBAC, workflow, transaction,
audit. Both backends must run in parallel through the cut-over, which means
the legacy `nvarchar` joins have to keep working until we strangler-fig them
out. Doing this before all the slices land would slow the revamp and bake
unfinished work into the schema.

**Side benefit.** This also removes the `Guid.Parse(userId)` hack in
`ListInboxQueryHandler` — instead of comparing `WorkflowInbox.IdentityId`
(Guid) to a parsed user id, the new shape joins on `int` directly, and
`WorkflowInbox.IdentityId` becomes a string/int matching `User_Id`'s new shape
(this also requires touching OptimaJet's `IdentityProvider` config — the
engine accepts either string or Guid for IdentityId depending on persistence
provider, so this is configurable, not a vendor change).

**Dependencies.** Wait for Slices 6 + 7 to ship (full transaction CRUD +
reports) so we have the complete picture of every JOIN that needs migrating.
Then plan the FK migration in three waves: (1) auth/RBAC tables we own,
(2) workflow tables (OptimaJet config tweak), (3) transaction + audit tables.

### `📥 Open` Lock the Option C overall sequence as a roadmap doc (slice-N / S)

- One-page "we are here, here's the full path" with phase dependencies.
- Useful for stakeholder review.

### `📥 Open` Delete the orphaned files left from earlier refactors (slice-1 / XS)

```powershell
cd "D:\ICBC - Latest\FSI.Trade.Compliance"
Remove-Item -Force "Services\FSI.Trade.Compliance.Application\Contracts\Identity\IIdentityService.cs"
Remove-Item -Force "Services\FSI.Trade.Compliance.Infrastructure\Identity\IdentityService.cs"
Remove-Item -Force "Services\FSI.Trade.Compliance.Infrastructure\Identity\PasswordPolicyOptions.cs"
Remove-Item -Force "Services\FSI.Trade.Compliance.Application\Common\Models\TokenIssuanceResult.cs"
Remove-Item -Force "Services\FSI.Trade.Compliance.Infrastructure\Identity\AuthOptions.cs"
```

### `📥 Open` Delete legacy projects once new backend is stable in prod for 30 days

- `tdl-authserver/` → archive
- `tmx-finance-backend/` → archive after each strangler-fig feature is migrated
- `tmx-finance-integrations/` → archive after CBS + KYC ports
- `Integration KYC/` → archive after FCCM port
- DB: drop `AspNet*` tables, drop `ICBC_DEMO_AUTH` database

### `📥 Open` Replace `TmxUserStore` + `UserManager<>` with direct `PasswordHasher<>` use? (slice-N / M)

- Optional. Removes one abstraction layer. Loses Identity's built-in lockout policy enforcement.
- Trade-off: more code we own vs less indirection. Defer until after slice 2 lands and we know whether UserManager's lockout helpers are actually pulling weight.

---

## ❓ Open questions (need stakeholder input)

### `📥 Open` `WFKey` license — does it cover OptimaJet 8.x?

- Procurement / OptimaJet sales question.
- Affects slice-5 plan. If new SKU needed, alternative is Workflow Core (open-source) or Elsa.

### `📥 Open` FE Process Designer page — is it actually used by business?

- If yes: 8.x port keeps the editing surface.
- If no: hide the page in FE and simplify.

### `📥 Open` Rule inventory — how many `<guid>.config` files in `~/BizTalkRule/Rules/`?

- Drives the rule-engine migration effort (currently slated as removable per Decision 9).

### `📥 Open` Self-service rule editor in FE — yes / no?

- If yes, slice that adds an admin rule-editor page.

### `📥 Open` Admin email for "new device" notifications

- Need an email-sending service (SendGrid / Office 365 SMTP). Procurement decision.

---

<!-- Newer items go ABOVE this line. Move "Done" items to a `## ✅ Done` section
     at the bottom (or delete them) once they ship. -->

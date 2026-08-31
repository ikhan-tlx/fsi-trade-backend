# Configuration migration — appsettings → Configuration Management DB (UAT)

Validate the current environment's configuration, then migrate the UAT
`appsettings.json` values into the Configuration Management database, with a
controlled, reversible, before/after‑validated process.

## How values resolve (so the migration targets the right thing)

Operational settings live in `dbo.TmX_Configuration_Definition` (catalogue) +
`dbo.TmX_Configuration_Value` (Global value). At runtime `DbConfigurationProvider`
publishes each key into `IConfiguration` (so every `IOptions`/`IOptionsMonitor`
consumer reads the DB value, hot‑reload, zero call‑site change) using exactly:

```
EffectiveValue = COALESCE(active Global Value, Definition Default_Value)
bridged when   Is_Active = 1 AND Is_Sensitive = 0
               AND Data_Type <> 'LogModule' AND Category_Key <> 'logging'
```

DB values override `appsettings.json` for the same key. **Secrets** stay out of
the DB store (kept encrypted, read via `ISecretConfigReader`). **Logging** keys
are stored but not yet bridged (Phase 2). This is the rule the report and the
migration mirror, so "applicable settings" == the bridgeable set.

## Deliverables & run order

| # | File | Purpose |
|---|------|---------|
| 1 | `01_config_current_env_report.sql` | Read‑only. Every setting + effective value + source + bridged flag; secrets presence; blanks. Use as the **BEFORE** and **AFTER** snapshot. |
| 2 | `02_migrate_appsettings_to_config.sql` | Controlled migration. Declare UAT values → pre‑flight completeness checks → dry‑run preview → apply (transaction + history) → after reconciliation. |
| 3 | `CONFIG_MIGRATION_INVENTORY.md` | This document — the appsettings ↔ Config mapping and the explicit‑value checklist. |

Procedure:
1. Run migrations `2026_07_036 … 048` (creates the catalogue + base values).
2. Run **(1)** → capture the BEFORE report.
3. Edit the `#Uat` block in **(2)** with the real UAT values (replace every `<<PROVIDE_UAT_VALUE>>`); run **(2)** as a **dry run** (`@Apply = 0`) — fix anything it flags.
4. Set `@Apply = 1`, run **(2)** → it commits only if the AFTER check is 0 mismatches.
5. Set the three secrets from the Configuration UI (below).
6. Run **(1)** again → the AFTER report should match your intended UAT values.

## A. appsettings keys that ARE DB‑managed (migrate these)

Bridged into `IConfiguration`; the migration provides/confirms a value for each.
Consumer = the `IOptions` class (section name in quotes) that binds it.

| Category | Configuration_Key | Type | Consumer (IOptions) | Env‑specific? |
|----------|-------------------|------|---------------------|:-:|
| security | `Jwt.AccessTokenDefaultMinutes`, `Jwt.RefreshTokenFactor`, `Jwt.RoleTtlMinutes` | Integer | `JwtOptions` ("Jwt") | |
| security | `Auth.RestrictConcurrentLogin`, `Auth.RequireDeviceIdHeader` (bool), `Auth.DeviceIdHeaderName` (string) | Bool/String | `AuthOptions` ("Auth") | |
| security | `TwoFactor.Enabled`, `TwoFactor.Method` | Bool/Enum | `TwoFactorOptions` ("TwoFactor") | |
| password | `PasswordPolicy.MaxFailedAttempts`, `LockoutMinutes`, `ExpiryDays`, `DormancyDays` | Integer | `PasswordPolicyOptions` ("PasswordPolicy") | |
| password | `PasswordPolicy.EnforceExpiry`, `EnforceDormancy`, `EnforceFirstPasswordChange` (bool), `DisableExpiryForRoles` (json) | Bool/Json | `PasswordPolicyOptions` | |
| review | `PeriodicReview.Enabled`, `ScanInterval`, `BatchSize`, `LeadTimeDays`, `DefaultFrequency`, `Frequencies` | mixed | `PeriodicReviewOptions` ("PeriodicReview") | |
| insight | `CustomerInsightSnapshot.Enabled`, `CheckInterval` | Bool/TimeSpan | `CustomerInsightSnapshotOptions` | |
| repo | `TradeRepository.SlaDays` | Integer | `TradeRepositoryOptions` ("TradeRepository") | |
| repo | `Sla.Enabled`, `Sla.DefaultHours`, `Sla.AtRiskPercent` | Bool/Int/Decimal | `SlaOptions` ("Sla") | |
| fccm | `Integration.FccmOnboardingUrl` | String(URL) | `IntegrationOptions` ("Integration") | ✅ |
| fccm | `Integration.CustomerMasterBaseUrl`, `Integration.FccmOracleSchema` | String | `IntegrationOptions` | ✅ |
| fccm | `Integration.PollingInterval`, `CaseIdTimeout`, `HttpTimeout` | TimeSpan | `IntegrationOptions` | |
| fccm | `Integration.FccmActionCodeStatusMap` | Json | `IntegrationOptions` | |
| fccm | `Integration.TradeCaseDefaults.*` (Jurisdiction, CaseType, OwnerOrg, OwnerId, EventType, CasePriority, CaseStatus, EventStatus, EventPriority, CaseSource, DataOrigin, ScenarioClass, BusinessDomain, ContractEventType, CustomerType, CustomerBusinessDomainList) | String/Enum | `IntegrationOptions` | `OwnerOrg`/`OwnerId` ✅ |
| fccm | `Integration.Onboarding.Username` | String | `FccmOnboardingOptions` ("Integration:Onboarding") | ✅ |
| onboarding | `Onboarding.RequireExternalVerification` (bool), `BaseUrl` (url), `ResponseMapping` (json), `FailureHandling` (enum), `CallTimeout` (timespan), `ProviderTitle`, `ProviderProfile` | mixed | `DynamicKycScreeningService` / provider profile | `BaseUrl`/`ProviderProfile` ✅ |
| reports | `Documents.BasePath` | String (Is_Editable=0) | `DocumentOptions` ("Documents") | ✅ (path) |
| reports | `Reports.ChromiumExecutablePath`, `ChromiumCachePath`, `FooterDisclaimer` | String/Text | `ReportOptions` ("Reports") | paths ✅ |
| email | `Email.Enabled`, `Host`, `Port`, `From`, `UseSsl`, `Username`, `MinSeverity`, `DeliveryMode`, `PickupDirectory` | mixed | `EmailOptions` ("Email") | ✅ |
| workflow | `Workflow.InboxMaxPageSize` | Integer | `WorkflowOptions` ("Workflow") | |
| onboarding/branding | `Branding.AppTitle`, `AppSubtitle`, `BrandColor`, `BrandGold`, `DefaultTheme` | String | `GET /Configuration/Branding` | optional |

> **Logging / Diagnostics** (`Logging.Modules.*`, `Logging.Directory`,
> `RetentionDays`, `ArchiveScanIntervalHours`) are **stored** in the catalogue
> but **NOT bridged** in Phase 1 — the file's `Integration:Diagnostics` block is
> still authoritative for the logging pipeline. The migration script therefore
> **excludes** the `logging` category; leave those in `appsettings.json`.

## B. appsettings keys that STAY in the file (do NOT migrate — keep declared)

Bootstrap/structural/secret/service‑identity keys the app must read before or
independently of the DB. These are **required to remain explicitly declared** in
`appsettings.json` (or env/secret store) for every environment including UAT.

| Key | Why it stays |
|-----|--------------|
| `ConnectionStrings:DefaultConnection` | Secret + bootstrap (needed to reach the DB before DB config exists) |
| `Jwt:Key` | Signing secret |
| `Jwt:Issuer`, `Jwt:Audience` | Structural (read directly at token setup) |
| `Api:RoutePrefix` | Structural/bootstrap (route wiring) |
| `Auth:WhitelistedPaths`, `Auth:DeviceCheckExemptPaths`, `Auth:BootstrapAdminRoles` | Security‑critical structural allow‑lists |
| `Workflow:LicenseKey` | Secret |
| `Workflow:RuntimeId` | Structural |
| `PeriodicReview:ReviewerUserId` | Service identity (not migrated) |
| `CustomerInsightSnapshot:WriterUserId` | Service identity (not migrated) |
| `Integration:FccmCallbackSharedSecret` | Secret (currently blank/unenforced) |
| `Integration:Onboarding:ApplicationIdFormat`, `MappingDirectory`, `MappingHotReload` | Mapping‑pipeline wiring (file‑driven) |
| `Integration:Diagnostics.*` | Logging pipeline (Phase 2 — not bridged) |
| `Integration:Brains.*` (`BaseUrl`, `ZoneNo`, `AdjustType`) | Legacy provider alias + provider extras (superseded by `Onboarding.*`) |
| `AllowedHosts`, `Logging:LogLevel` | Host structural/logging |

## C. Secrets — migrated to the ENCRYPTED store, set via the UI (never by SQL)

Do **not** put these in the migration `#Uat` block (it rejects them). Set each
from Configuration → the relevant category; they are stored encrypted at rest.

| Configuration_Key | Set at |
|-------------------|--------|
| `Integration.FccmOracleConnection` | Configuration → FCCM / Integration |
| `Integration.Onboarding.Password` | Configuration → FCCM / Integration |
| `Email.Password` | Configuration → Notifications / Email |

## D. "Explicitly declare for UAT" checklist (env‑specific — must be provided)

These genuinely differ between dev and UAT and are pre‑listed (with sentinels)
in the migration's `#Uat` block — provide the real UAT value for each:

- `Integration.FccmOnboardingUrl`, `Integration.CustomerMasterBaseUrl`, `Integration.FccmOracleSchema`
- `Integration.TradeCaseDefaults.OwnerOrg`, `Integration.TradeCaseDefaults.OwnerId`
- `Onboarding.BaseUrl` (KYC provider)
- `Reports.ChromiumExecutablePath` (server path), `Documents.BasePath` (if not `ICBC_Data`)
- `Email.Enabled`, `Host`, `Port`, `From`, `UseSsl`, `Username`
- (secrets) `Integration.FccmOracleConnection`, `Integration.Onboarding.Password`, `Email.Password` → UI

## Validation guarantees (maps to the request)

- **All applicable appsettings identified** — the plan enumerates every bridgeable, non‑secret Definition (the exact runtime rule), so scope can't drift.
- **Each mapped to its config entry** — `#Uat.Configuration_Key` joins the `Definition` by canonical dotted key; unmatched keys abort.
- **No value missed** — pre‑flight lists any bridgeable key with no value anywhere (MISSING → abort) and any unreplaced sentinel (→ abort).
- **Before/after comparison** — the script prints appsettings(target) vs current Config value before applying, and effective vs target after, committing only on 0 mismatches (else it rolls back).

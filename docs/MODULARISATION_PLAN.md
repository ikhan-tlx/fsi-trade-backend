# Modularisation Plan — FSI.Trade.Compliance

**Status**: deferred. To be executed post-Slice 7 once the core revamp is
stable in production for 60–90 days.

**Purpose**: capture the architectural decision and implementation recipe
for turning the new backend into a **modular monolith** so different
banking customers can install only the modules they're licensed for —
just like Oracle OFSAA, NICE Actimize, FIS / Fiserv, SAS AML, etc.

**Why this doc exists now (even though execution is later)**: the
discipline points in §6 are zero-cost to maintain through the rest of
the revamp, and they make the eventual refactor mechanical instead of
surgical. Future joiners need to see the destination so they don't
unknowingly cross module boundaries.

---

## Table of contents

1. The decision and why
2. Sales scenarios this enables
3. The four candidate approaches and what we picked
4. Target shape (post-revamp)
5. The `IComplianceModule` API
6. Discipline points to maintain through the rest of the revamp
7. License file format + signing
8. Per-module migrations
9. Cost estimate
10. Risks and mitigations
11. Comparable products in the market
12. When to revisit

---

## 1. The decision and why

**Decision**: when the revamp completes (Slice 7) and we have ~60–90 days of
production-stable monolith, refactor into a **modular monolith** where
each slice becomes its own module project, the host loads modules
based on a signed license file at startup, and unlicensed module
endpoints don't exist (404 at routing — not "exist but return 403").

**Why now (the planning, not the execution)**: the discipline points
in §6 are free to maintain during the remaining slices. Without them,
cross-module coupling sneaks in over years and the eventual refactor
becomes a hard rewrite. With them, the refactor is "extract code into
a project per slice" — mechanical and ~7-8 weeks.

**Why a modular monolith (not microservices, not feature flags)**:

- **Banking customers want one deployable** to put behind their firewall.
  Microservices add operational complexity (multi-process, service
  discovery, distributed tx) that small banks can't manage.
- **Compliance audits are simpler** with one process boundary.
- **Latency**: in-process is faster than network. Trade-finance volumes
  are not internet-scale; we don't need horizontal scale-out per module.
- **Feature flags ship dead code** to every install. The licensing
  boundary becomes honor-system. Modular monolith gives us a hard
  boundary: if Workflow isn't licensed, the assembly doesn't load.

---

## 2. Sales scenarios this enables

```
Tier 1 — Compliance Lite              Auth + RBAC + AppInit + Customer
                                      "Just give us KYC lookups."
                                      Cheap; entry-level.

Tier 2 — Compliance Standard          + KycCase + Workflow
                                      "We need full case management."
                                      Mid-tier.

Tier 3 — Trade Compliance Full        + Transaction + Reports
                                      "We need the full trade workflow + reports."
                                      Flagship.

Tier 4 — Enterprise                   + Audit retention, GeoIP, admin cross-user
                                          session view, advanced analytics, etc.
                                      Premium add-ons.
```

Customers upgrade by getting a new license file. Same artefact, more
modules activate. That operational simplicity is what sells in the
banking compliance market.

---

## 3. The four candidate approaches and what we picked

| Approach | Pros | Cons | Verdict |
|---|---|---|---|
| **Feature flags (config-driven)** | Simplest. One artefact. | Dead code in every install. Attack surface stays wide. License enforcement is honor-system. | ❌ Reject |
| **Modular monolith** | One deployable. Hard module boundary at assembly load. License-gated. | Discipline required to keep modules independent. | ✅ **Picked** |
| **Microservices** | True module independence. Independent scaling. | Operational overhead. Distributed tx pain. Most regional banks can't run this. | ❌ Reject |
| **Build-time profiles** | Smallest binary per customer. | Maintenance nightmare as profiles diverge. Combinatorial test surface. | ❌ Reject |

---

## 4. Target shape (post-revamp)

```
FSI.Trade.Compliance.sln

├── Core/
│   ├── FSI.Compliance.Domain/                    shared domain primitives
│   ├── FSI.Compliance.Application/               shared abstractions, base contracts
│   └── FSI.Compliance.Host/                      API host + module loader + license validation
│
├── Modules/
│   ├── FSI.Compliance.Module.Auth/               REQUIRED — every install gets this
│   ├── FSI.Compliance.Module.Rbac/               REQUIRED
│   ├── FSI.Compliance.Module.AppInit/            REQUIRED — Lookup, Configurations
│   ├── FSI.Compliance.Module.Customer/           OPTIONAL — Customer master + BRAINS KYC lookup
│   ├── FSI.Compliance.Module.KycCase/            OPTIONAL — depends on Customer
│   ├── FSI.Compliance.Module.Workflow/           OPTIONAL — OptimaJet engine
│   ├── FSI.Compliance.Module.Transaction/        OPTIONAL — depends on Workflow + Customer
│   ├── FSI.Compliance.Module.Reports/            OPTIONAL — DotLiquid + PDF + Excel
│   └── FSI.Compliance.Module.Audit/              OPTIONAL — retention + GeoIP + admin views
│
└── License/
    └── FSI.Compliance.License/                   signed module activation file + verifier
```

**Required modules**: Auth, Rbac, AppInit. Without these, no install works.

**Optional modules**: everything else. Sold à la carte. Most have explicit
dependencies (`KycCase` requires `Customer`; `Transaction` requires
`Workflow` and `Customer`).

---

## 5. The `IComplianceModule` API

Each module project exposes one type:

```csharp
namespace FSI.Compliance.Core.ModuleSystem;

public interface IComplianceModule
{
    /// <summary>Stable module name. Goes in the license file.</summary>
    string Name { get; }                                  // "Workflow", "KycCase", etc.

    /// <summary>SemVer for this module assembly.</summary>
    string Version { get; }                               // "1.4.2"

    /// <summary>Names of modules this depends on. Topologically sorted at startup.</summary>
    IReadOnlyList<string> DependsOn { get; }              // ["Customer"]

    /// <summary>Register this module's services with the host's DI container.</summary>
    void RegisterServices(IServiceCollection services, IConfiguration cfg);

    /// <summary>Map this module's HTTP endpoints onto the host's routing.</summary>
    void RegisterEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Run this module's database migrations. Called at startup before endpoints register.</summary>
    Task RunMigrationsAsync(string connStr, CancellationToken ct);

    /// <summary>One-time initialisation after DI is built (warm caches, etc.). Optional.</summary>
    Task OnInitializeAsync(IServiceProvider sp, CancellationToken ct);
}
```

**Host startup pseudocode**:

```csharp
// 1. Load + verify the license file. HMAC-signed JSON.
var license = LicenseLoader.LoadAndVerify("license.dat");
if (license is null) throw new HostInitException("Invalid or missing license.");

// 2. Discover module assemblies. Either explicit DLL paths configured per
//    install, or AssemblyLoadContext scan over a /modules folder.
var modules = ModuleLoader.Discover(license.EnabledModules);

// 3. Validate dependency closure: every module's DependsOn list must be
//    in the licensed set. Reject startup if a module depends on something
//    the customer didn't license.
ModuleDependencyValidator.AssertValid(modules);

// 4. Topologically sort by DependsOn — Auth loads before Rbac, Rbac before Customer, etc.
modules = ModuleLoader.TopologicalSort(modules);

// 5. Each module registers its DI + runs its own migrations (idempotent).
foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
    await module.RunMigrationsAsync(connStr, ct);
}

var app = builder.Build();

// 6. Each module maps its endpoints. After this, /api/v1/Workflow/* exists
//    iff Workflow module is loaded — truly absent (404 at routing) for
//    unlicensed customers, not "exists but returns 403".
foreach (var module in modules)
    module.RegisterEndpoints(app);

// 7. One-time init pass.
foreach (var module in modules)
    await module.OnInitializeAsync(app.Services, ct);
```

---

## 6. Discipline points to maintain through the rest of the revamp

These are **zero-cost** if applied as we build remaining slices, and they
make the eventual modularisation a mechanical refactor rather than a
surgical rewrite. Add to API_GUIDELINES.md as part of the convention list.

1. **No cross-slice handler imports.** A handler in
   `Application/Features/Transaction/...` must not `using
   FSI.Trade.Compliance.Application.Features.Auth.Login`. Cross-slice
   data access goes through Application contracts (`IRoleQueryService`,
   `IKycScreeningService`, etc.), never through another slice's
   handlers.

2. **EF configurations don't span slices.** Each slice owns its
   `*Configuration.cs` files. Today they all sit under
   `Infrastructure/Persistence/Configurations/` — fine for the monolith,
   but they should be tagged with their owning slice in comments so the
   refactor knows which module they move to.

3. **Migration scripts are slice-tagged.** We're already doing this:
   `2026_05_001` is auth, `2026_05_004` is auth+device, `2026_05_007` is
   KYC case. Keep the convention. Each module will own its migration
   sequence post-modularisation.

4. **`IApplicationDbContext` is the natural seam.** Today it has 12+
   DbSets across all slices. Plan to split it into per-module contexts
   during modularisation: each module brings its own
   `IModuleDbContext` (e.g. `IWorkflowDbContext`, `IKycDbContext`). The
   shared `IApplicationDbContext` shrinks to the core entities only
   (`ApplicationUser`, `RefreshToken`, `Role`, `Privilege`, etc.).

5. **API_GUIDELINES rule for new endpoints**: ask "which module would
   this belong to?" before placing a controller. If it doesn't fit
   cleanly in one, it's probably crossing a boundary and the design
   needs a second look.

6. **No Domain entity references another slice's entity directly.** If
   a Transaction needs Customer info, it goes through
   `ICustomerMasterService`, not a `Transaction.Customer` navigation
   property.

7. **`InfrastructureServiceRegistration.cs` is divisible.** It already
   has a "Slice X:" comment per block. When a module is extracted, that
   block lifts cleanly into the module's `RegisterServices(...)` impl.

If a code review spots a violation of any of these, fix at review time —
the cost of fixing during the slice is ~15 minutes; the cost of fixing
during the modularisation refactor is ~2 hours per case.

---

## 7. License file format + signing

**Shape** (signed JSON):

```json
{
  "header": {
    "format_version": 1,
    "issued_at":      "2026-05-10T08:00:00Z",
    "issued_by":      "FSI License Authority",
    "issued_to":      "ICBC Pakistan",
    "customer_id":    "icbc-pk-001"
  },
  "license": {
    "expires_at":     "2027-05-10T08:00:00Z",
    "max_users":      500,                       // optional limit
    "max_branches":   200,                       // optional limit
    "modules": [
      { "name": "Auth",        "version_constraint": ">=1.0" },
      { "name": "Rbac",        "version_constraint": ">=1.0" },
      { "name": "AppInit",     "version_constraint": ">=1.0" },
      { "name": "Customer",    "version_constraint": ">=1.0" },
      { "name": "KycCase",     "version_constraint": ">=1.0" },
      { "name": "Workflow",    "version_constraint": ">=1.0" },
      { "name": "Transaction", "version_constraint": ">=1.0" }
      // Reports module deliberately absent — this customer didn't license it
    ]
  },
  "signature": "<HMAC-SHA256(canonical_json(header + license), our_signing_key)>"
}
```

**Verification flow at host startup**:

1. Load `license.dat` from a configured path (default: alongside the binary).
2. Strip the `signature` field; canonicalise the remaining JSON
   (sorted keys, no whitespace, UTF-8).
3. Compute HMAC-SHA256 with our signing key (stored encrypted on the
   host or pulled from a config secret).
4. `CryptographicOperations.FixedTimeEquals` against the signature.
5. If mismatch → host refuses to start with a clear error.
6. If `expires_at < now` → host starts in a 30-day grace window, logging
   a daily warning. After grace, refuses to start.
7. If a module's actual version doesn't satisfy the `version_constraint`
   → host refuses to start ("license requires Workflow >=1.0; have 0.9").

**Signing key management**:

- We hold the private signing key. Customer never sees it.
- License files are issued by us via a small CLI tool
  (`fsi-license-issue --customer icbc-pk-001 --modules Auth,Rbac,...`).
- Renewals = re-issuing with a later `expires_at`.
- Revocation = honour-system; we don't ship a CRL. If a customer
  defaults, we don't issue them new licenses; the current one expires
  on its own. Adding a real revocation list (online phone-home) is
  premature and customers hate it.

**What this doesn't protect against**: a determined attacker can
reverse-engineer the verification logic and forge licenses. The license
is a **commercial / contractual artefact**, not a security boundary.
Banking customers don't pirate; they're regulated and audited. Don't
spend engineering effort on anti-piracy.

---

## 8. Per-module migrations

Two options, in increasing rigour:

### Option A — single combined migration set (simpler, current pattern)

All migrations live in the host's `database/migrations/` folder. Each
module's tables are created by host-owned scripts. The host runs all
scripts at startup; modules don't run migrations themselves.

Pros: Today's pattern. Zero new code.
Cons: Customer who isn't licensed for Workflow still gets the Workflow
tables created in their DB. Wasteful but harmless.

### Option B — per-module migration runner (cleaner)

Each module owns a `module/migrations/` subfolder. The module's
`RunMigrationsAsync(connStr, ct)` method discovers and applies its own
scripts (idempotent, guarded by `IF NOT EXISTS`).

Pros: Customer's DB only contains tables for licensed modules.
Cleaner deinstall (drop module → drop its tables).
Cons: Deinstall isn't quite that clean (FK references from data linger);
slightly more code per module.

**Recommendation**: start with Option A in the modularisation refactor
(less code, faster ship). Move to Option B if customer feedback
demands it (probably never — DBAs don't care about a few unused
tables).

---

## 9. Cost estimate

```
Refactor each slice into its own project              1-2 days × 8 slices = ~2 weeks
IComplianceModule interface + module loader           ~1 week
License file format + signing/verification            ~1 week
Per-module DI registration (carve up
   InfrastructureServiceRegistration)                 ~3 days
Per-module endpoint registration (controllers
   move into modules; host gets a minimal MapControllers) ~3 days
Topological sort + dependency validation              ~1 day
Per-module migration runner (Option B if chosen)      ~1 week
End-to-end testing across module combinations
   (minimal / full / a few business-critical combos)  ~2 weeks
Documentation + customer-facing licensing UX          ~1 week
                                                       ───────────
                                                       ~7-8 weeks total
```

Done by one senior + one mid, sequentially. Cuttable to ~5 weeks if we
descope per-module migrations and stick with Option A.

**Calendar caveat**: this estimate assumes the codebase has stayed
clean per §6's discipline points. If it hasn't, add 2-3 weeks of
"untangle the cross-slice imports" work upfront.

---

## 10. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Cross-module coupling sneaks in over years | Medium | High — refactor balloons | §6 discipline + pre-merge lint rule that flags cross-module imports |
| Per-module migrations create ordering surprises (Module A's table FKs Module B's; B not licensed) | Medium | Medium — install fails | Declared `DependsOn` enforces load + migration order; validation refuses startup if dependencies are missing |
| License file is reverse-engineerable | High | Low — banks don't pirate | Don't engineer for it. License is a commercial artefact. |
| Module versioning surprises | Medium | Medium | Per-customer host pin. One customer = one host version. No "mix v1 of Workflow with v2 of Customer". |
| Per-module test surface multiplies combinatorially | High | Medium | Don't try to exhaust the matrix. Test 3 baselines: minimal install (Tier 1), full install (Tier 4), and 1-2 business-critical combos (Tier 2 + Tier 3). |
| Migration of an existing monolith customer to modular | Low (initially) | High when it happens | First customers get the modular version. Upgrade path for existing monolith installs is its own slice — likely a "migration utility" tool. |
| Module developer accidentally uses another module's `DbSet` directly | Medium | Medium | EF model split across `IModuleDbContext`s, not a single `IApplicationDbContext`. Reaching across requires explicit cross-module reference, which the lint rule catches. |

---

## 11. Comparable products in the market

This pattern is well-established. References to lift design ideas from:

- **Oracle Financial Services Analytical Applications (OFSAA)** —
  modular: AML, KYC, Trade Surveillance, Capital Adequacy, etc. Each
  module separately licensed. Banks pick what they need.
- **NICE Actimize** — modular compliance product family. Customers buy
  individual products; common platform underneath.
- **SAS AML / SAS Compliance** — same pattern.
- **FIS Prophet, Fiserv DNA, Temenos T24** — banking core systems with
  optional add-on modules.
- **Camunda** — workflow engine with pluggable extensions.
- **OptimaJet itself** (irony noted) — sells base WorkflowEngine + add-on
  packages (Designer, Server, Forms).
- **Atlassian** (Jira / Confluence) — apps marketplace as the modular
  surface; same architectural shape, B2B variant.
- **Microsoft Power Platform** — modular by design (Power Apps, Power
  Automate, Power BI, Power Virtual Agents) on a shared substrate.

When the time comes, study Camunda's plugin architecture and OptimaJet's
package boundaries — both are nearest analogues for a workflow-centric
compliance product.

---

## 12. When to revisit

**Trigger conditions** (any one):

1. Revamp completes (Slice 7 ships) AND production-stable for 60 consecutive days.
2. A second prospective customer asks for a different module subset than
   ICBC — that's market evidence we're leaving money on the table by
   shipping monolithic.
3. The codebase shows enough cross-slice coupling that we can't change
   one slice without surveying others. (This means §6 discipline failed;
   we should refactor before it gets worse.)

**At that point**:

1. Re-read this doc.
2. Audit the slice boundaries: are they clean? If not, do the cleanup
   first, before adding the modularisation machinery.
3. Decide single-customer vs multi-customer first deploy. Single is
   safer; we can iterate the licensing UX.
4. Kick off the 7-8 week modularisation slice using §9's plan.
5. Customer-zero is the modular install. Existing monolith installs
   keep running until natural upgrade.

**Don't trigger early on** "we should be modular for theoretical
flexibility" — wait for actual customer evidence. Premature
modularisation is more expensive than late modularisation.

---

## Open decisions (revisit at trigger time)

- **Module-discovery mechanism**: explicit list in license file
  (rigid), or assembly scan over a `/modules` folder (flexible)? Recommend
  start with the explicit list; reduce attack surface.
- **Hot-swap modules at runtime?** Probably no — banking customers
  prefer scheduled restarts for any module install.
- **License-server vs license-file**: phone-home vs offline. Offline
  for now (banking customers often have air-gapped environments).
  Revisit if SaaS deployment becomes a thing.
- **Per-module configuration UI**: do we ship an admin web page for
  enabling/disabling features within a licensed module? Or is the
  license file the only knob? Start with file-only; add UI when a
  customer asks.

---

*Last updated: 2026-05-06 — captured from brainstorming session after Slice 5 shipped.
Revisit when revamp completes (post-Slice 7) + ~60 days production-stable.*

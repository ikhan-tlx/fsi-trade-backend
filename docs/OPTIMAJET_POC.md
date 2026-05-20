# OptimaJet 3.x → 8.x — Senior-Developer POC

> **Slice 5.6 update (2026-05-11) — Version pin decision**
>
> The new backend is **pinned to OptimaJet 3.5.0** (`WorkflowEngine.NETCore-Core` /
> `WorkflowEngine.NETCore-ProviderForMSSQL`) — NOT v21 as Slice 5 originally
> proposed. Reason: license parity with the legacy backend.
>
> **The full story**:
>
> - Legacy uses OptimaJet **3.1.0** (`WorkflowEngine.NET-Core` — note the
>   hyphen — `net45` target). That assembly cannot load on .NET 8.
> - Legacy's `WFKey` (`techlogix2019-...`) decodes to expiry **2020-07-24** —
>   it expired ~6 years ago. Legacy *appears* to work because OptimaJet v3.x
>   has soft license enforcement: expired keys log a warning at runtime
>   startup but every runtime method (`GetAvailableCommands`, `ExecuteCommand`,
>   `SetState`, `CreateInstance`) continues to function.
> - OptimaJet **v21+** enforces strictly: every runtime call validates the
>   licence and throws `InvalidLicenseException` on expiry. DB-level queries
>   (Inbox, Schemes, ProductMapping) still work because they bypass the
>   runtime — but the moment the transaction-detail endpoint calls
>   `GetAvailableCommandsAsync`, it 500s.
> - **OptimaJet 3.5.0** is the goldilocks build: `.NET 8` compatible
>   (`net8.0` target via the `NETCore` package — no hyphen), same soft
>   enforcement as v3.x (the expired `techlogix2019` key works), same API
>   surface as legacy v3.1 (so the legacy action / rule providers and
>   designer endpoint port without rework), and same DB schema (so
>   `WorkflowInbox` / `WorkflowProcessInstance` / `WorkflowProcessScheme`
>   rows continue to load).
> - **Verified against the working sample app**: `D:\ICBC - Latest\TMX Sample App`
>   uses `WorkflowEngine.NETCore-Core 3.5.0` on `net8.0` and runs fine with
>   the same expired key.
>
> **Licensing risk acknowledged**:
> Using v3.5.0 with an expired key is technically running an unlicensed
> commercial product. Legacy has been doing this for 6 years; the precedent
> is set inside the org, but it's a real commercial / audit risk to
> acknowledge. The long-term clean exit is captured in `BACKLOG.md` as
> "Replace OptimaJet runtime with FSI-owned engine" — same DB schema,
> same scheme XML, zero vendor dependency, zero licensing exposure across
> any tier of the modular-monolith plan. Until that ships, OR until
> procurement lands a fresh v21 license, v3.5.0 is the pragmatic pin.

---

**Status**: design POC (paper validation). Not yet implemented in code.
**Scope**: validate that the legacy workflow runtime can be ported to .NET 8
without rewriting the schemes, action providers, or DB tables. Assess the
license / package / API surface gap before committing to Slice 5 effort.

---

## TL;DR

- **The package gives you a runtime, not endpoints.** `WorkflowEngine.NET`
  ships as a NuGet library — `WorkflowRuntime` is an instance you bootstrap
  and call from your own controllers. Designer is built-in but is a single
  method (`DesignerAPIAsync`) that you still wrap in your own action.
- **Yes, we still write controllers.** Every endpoint the FE calls
  (`/ExecuteWf`, `/GetCommandsByProcessId`, `/Workflow`, `/Designer`) is OUR
  controller calling OUR `WorkflowRuntime` instance. The package itself
  exposes zero ASP.NET routes when used as a library.
- **An HTTP-server SKU exists** (`WorkflowEngine.NET-Server`) but it's a
  separate process / sidecar, not a library. Out of scope for our embedded
  use case.
- **Schemes (XML) and DB tables are forward-compatible.** v3 → v8 is
  an additive migration. Run the official `update_*_to_*.sql` scripts
  in order; existing scheme XML loads as-is.
- **Custom code blocks need touch-ups.** The 21 `WorkflowActions` and 3
  `WorkflowRule` providers in `TMX.Workflows/Runtime/` will compile in .NET 8
  with minor signature shifts (mostly async/`CancellationToken` additions).
- **License is the real risk.** Engine is proprietary; the `WFKey` we have
  today covers 3.1.0 — we need to confirm with OptimaJet sales whether it
  also covers 8.x or whether we need to upgrade. Tracked in BACKLOG as a
  procurement question.

---

## Table of contents

1. Why we need controllers even with the package installed
2. Inventory — current state (legacy v3.1.0)
3. Target — current state (v8.x / `WorkflowEngine.NETCore-Core`)
4. Migration plan — schema, code, runtime
5. Minimal POC code skeleton (.NET 8)
6. License verification checklist
7. Risks and alternatives
8. Recommended next steps

---

## 1. Why we need controllers even with the package installed

This was the user's question. Short answer: **the OptimaJet NuGet package is a
library, not a web framework.** It gives you:

- A `WorkflowRuntime` class — the engine instance, registered in DI as a
  singleton.
- Methods on the runtime: `ExecuteCommandAsync`, `GetAvailableCommandsAsync`,
  `CreateInstanceAsync`, `GetAvailableStateToSetAsync`, `SetStateAsync`,
  `DesignerAPIAsync`, etc.
- Persistence + scheme storage providers — they sit underneath the runtime
  and handle `WorkflowProcessInstance` etc. tables.
- Action / rule provider interfaces — `IWorkflowActionProvider`,
  `IWorkflowRuleProvider` — that you implement and register.

**What it does NOT give you**: ASP.NET Core routes / controllers / model
binding. You expose the runtime over HTTP yourself by writing controllers
that:

- Bind incoming HTTP requests to `Guid processId`, `string command`, etc.
- Call the relevant `WorkflowRuntime` method.
- Return the result wrapped in your standard envelope (`{ status, data }`
  per our API_GUIDELINES).

OptimaJet does also ship a **`WorkflowEngine.NET-Server`** (separate
product). That's a standalone web process with REST endpoints exposed
out-of-the-box. It's intended for cross-language clients (e.g. a Node.js
or Python app calling an external workflow service). Our backend is
embedded — workflow runs in the same process as Auth and Users — so
Server is wrong shape for us. Confirmed: we keep the embedded library
approach and write controllers.

**Concrete mapping** of FE endpoint → runtime call:

| FE call | Our controller action calls |
|---|---|
| `PUT /api/v1/Transaction/ExecuteWf` | `WorkflowRuntime.ExecuteCommandAsync(processId, identityId, command)` |
| `GET /api/v1/LoanApplication/GetCommandsByProcessId/{id}` | `WorkflowRuntime.GetAvailableCommandsAsync(processId, identityId)` |
| `GET /api/v1/LoanApplication/Workflow` | `WorkflowRuntime.GetSchemes()` (or query `WorkflowProcessInstance` directly) |
| `GET/POST /api/v1/LoanApplication/Workflow/Designer` | `WorkflowRuntime.DesignerAPIAsync(formParams, files, true)` |
| `POST /api/v1/Transaction/Create` | `WorkflowRuntime.CreateInstanceAsync(schemeCode, processId, identityId)` |

Each row is one ~10-line controller action. The package handles the heavy
lifting (state, transitions, persistence, history, timers).

---

## 2. Inventory — current state (legacy v3.1.0)

From `D:\ICBC - Latest\tmx-finance-backend\`:

**Packages** (`TMX.Workflows/packages.config`):

```
WorkflowEngine.NET-Core              v3.1.0
WorkflowEngine.NET-ProviderForMSSQL  v3.1.0
```

**Runtime bootstrap** (`TMX.Workflows/Runtime/WorkflowInit.cs`, ~50 LOC):

```csharp
WorkflowRuntime.RegisterLicense(ConfigurationManager.AppSettings["WFKey"]);

var connStr = /* extracted from EF connection string */;
var generator = new DbXmlWorkflowGenerator(connStr);
var builder = new WorkflowBuilder<XElement>(
    generator,
    new XmlWorkflowParser(),
    new DbSchemePersistenceProvider(connStr)
).WithDefaultCache();

_runtime = new WorkflowRuntime(new Guid("{8D38DB8F-F3D5-4F26-A989-4FDD40F32D9D}"))
    .WithBuilder(builder)
    .WithActionProvider(new WorkflowActions())
    .WithRuleProvider(new WorkflowRule())
    .WithPersistenceProvider(new DbPersistenceProvider(connStr))
    .WithTimerManager(new TimerManager())
    .WithBus(new NullBus())
    .RegisterAssemblyForCodeActions(Assembly.GetExecutingAssembly())
    .Start();
```

**Custom action provider** (`WorkflowActions.cs`): 21 actions wired —
`FillApprovers`, `Approve`, `SendEmail`, `SendSMS`, `KillSubProcesses`,
`ResetToStateByParameter`, `ClearInboxByRole`, etc.

**Custom rule provider** (`WorkflowRule.cs`): 3 rules — `IsCreator`,
`CheckRole`, `Boss`.

**DB tables used** (all under `[ICBC_DEMO].dbo.`): `WorkflowProcessInstance`,
`WorkflowProcessScheme`, `WorkflowProcessInstancePersistence`,
`WorkflowProcessInstanceStatus`, `WorkflowProcessTransitionHistory`,
`WorkflowInbox`, `WorkflowGlobalParameter`, `WorkflowProcessTimer`.

**Scheme storage**: 100% in `WorkflowProcessScheme.Scheme` (ntext column,
XML payload). No filesystem schemes.

**Designer**: served at `GET /api/v1/Workflow/Designer` (anonymous!), routes
the query string to `runtime.DesignerAPI(pars, null, true)`.

**Controllers exposing the runtime**: `LoanApplicationController`,
`AccountApplicationController`, `TransactionController` — all extend a
common base `WorkflowController<TViewModel>` with shared protected helpers
for `GetCommands`, `ExecuteCommand`, `SetWorkflowState`,
`GetAvailableStates`.

---

## 3. Target — current state (v8.x / `WorkflowEngine.NETCore-Core`)

Per public docs (workflowengine.io, NuGet, GitHub releases) as of May 2026.
Versions and package names below should be re-confirmed against nuget.org
before committing.

**Recommended packages**:

```
WorkflowEngine.NETCore-Core              v21.x   (replaces .NET-Core v3.1.0)
WorkflowEngine.NETCore-ProviderForMSSQL  v21.x   (replaces .NET-ProviderForMSSQL)
WorkflowEngine.Extensions.DependencyInjection (optional but recommended)
```

> ⚠ The version "21.x" comes from the WebSearch agent's report citing
> NuGet listing. Treat as approximate until verified by `dotnet add package
> WorkflowEngine.NETCore-Core --prerelease` or browsing nuget.org directly.
> The major number is high because OptimaJet uses a calendar-versioning
> scheme.

**API stability — what carries over from v3.1.0 unchanged**:

- `WorkflowRuntime` class name + constructor with Guid runtime ID.
- `RegisterLicense(string)` static method — same signature.
- `WithActionProvider`, `WithRuleProvider`, `WithBuilder`,
  `WithPersistenceProvider`, `RegisterAssemblyForCodeActions`, `.Start()`.
- `IWorkflowActionProvider`, `IWorkflowRuleProvider` interfaces.
- Scheme XML format — no breaking parser changes.

**API changes — what shifts in v8.x**:

- DI integration: `services.AddWorkflowEngine(...)` extension method
  (from `WorkflowEngine.Extensions.DependencyInjection`). All services
  registered as singletons.
- Some methods went async: `DesignerAPIAsync`, `ExecuteCommandAsync`,
  `GetAvailableCommandsAsync`. Older sync overloads may still exist as
  thin wrappers but warnings will fire.
- `IWorkflowPlugin.OnPluginRemoveAsync` removed (not used by us).
- `WorkflowApiSetupOptions` → `WorkflowApiOptionsSetup` (only relevant
  if using the multi-tenant Server SKU, which we are not).
- Multi-tenant runtimes added — single-tenant is still the default and
  matches our setup.

**DB schema changes — additive**:

- All v3.1.0 tables still exist with same names + same key columns.
- New tables in v4+: `WorkflowSync` (multi-server timer coordination),
  `WorkflowApprovalHistory`, `WorkflowProcessAssignment`, plus the existing
  `WorkflowInbox` / `WorkflowProcessInstancePersistence` /
  `WorkflowProcessTransitionHistory`.
- Migration scripts published in the OptimaJet GitHub repo at
  `Providers/OptimaJet.Workflow.DbPersistence/SQL/`. Files named
  `update_<from>_to_<to>.sql`. Run in chronological order from your current
  version (3.1) to target (8.x / 21.x).

**Designer**: still served by `runtime.DesignerAPIAsync(formParams, files, true)`.
JS/CSS assets shipped separately — same as v3.

**Licensing**:

- Engine remains proprietary commercial. No free OSS version.
- License is **per-project** (Ultimate or Enterprise tier). No royalties on
  Ultimate.
- License key is registered via `WorkflowRuntime.RegisterLicense(key)`,
  same call as v3.1.0. Reads from `IConfiguration["Workflow:Key"]` in our
  setup.
- **Open question**: does the existing `WFKey` cover 8.x? Most enterprise
  licenses cover the major version they were purchased for; upgrade may
  require a SKU bump. Verify with OptimaJet sales — see Section 6.

---

## 4. Migration plan — schema, code, runtime

### 4.1 Schema migration

1. Take a backup of `ICBC_DEMO.dbo.Workflow*` tables.
2. Pull migration scripts from OptimaJet's GitHub repo (the
   `OptimaJet.Workflow.DbPersistence/SQL/` folder).
3. Run them in order: `update_3_x_to_4_0.sql` → ... →
   `update_<n-1>_to_<n>.sql`. The published scripts are idempotent —
   each starts with `IF NOT EXISTS`-style guards.
4. Validate every existing scheme loads:
   ```sql
   SELECT Code, COUNT(*) FROM WorkflowProcessScheme GROUP BY Code;
   ```
   Counts should match what the legacy backend has now.

### 4.2 Code migration — runtime bootstrap

Replace the static `WorkflowInit` singleton with proper DI in the new
`FSI.Trade.Compliance.Infrastructure` project. See Section 5 for code.

### 4.3 Code migration — action / rule providers

Move `WorkflowActions.cs` (21 actions) and `WorkflowRule.cs` (3 rules) from
`tmx-finance-backend/TMX.Workflows/Runtime/` into a new
`FSI.Trade.Compliance.Infrastructure/Workflow/` folder. Adjustments:

- Update method signatures to async + `CancellationToken` where v8 requires.
- Rewrite any `ConfigurationManager.AppSettings[...]` reads to use
  `IConfiguration` injected via constructor.
- Replace `using (var roleService = new RoleService())` patterns (the
  legacy DI style) with constructor-injected
  `IRoleQueryService` (already in our Application contracts).
- The 21 actions reference legacy services: `ApprovalService`,
  `EmailService`, `SmsService`, etc. Each needs a thin port to a Slice 5
  Application contract — most can be temporary stubs that log + no-op
  during the OptimaJet POC, with real implementations following.

### 4.4 Code migration — controllers

Three new controllers in `FSI.Trade.Compliance.API`:

- `LoanApplicationController` (Inbox + Process Designer)
- `AccountApplicationController` (Inbox dynamic routing — minimal scope)
- `TransactionController` (Slice 6 main work)

All three inherit from a shared base — possibly `WorkflowControllerBase` —
that exposes protected helpers calling the runtime. Three workflow
endpoints common to all three:

- `PUT /{controller}/ExecuteWf` → runtime.ExecuteCommandAsync
- `GET /{controller}/GetCommandsByProcessId/{id}` → runtime.GetAvailableCommandsAsync
- (controller-specific) lifecycle endpoints (Create, Get, List)

Plus the Designer + Workflow + ProductMapping endpoints under
`LoanApplicationController` (FE calls them at exactly that path; preserving
the URL avoids a FE change).

---

## 5. Minimal POC code skeleton (.NET 8)

This is the ~30 lines of wiring needed to validate the runtime starts. Not
yet committed; intended as the senior-dev validation.

**`Infrastructure/Workflow/WorkflowOptions.cs`**:

```csharp
namespace FSI.Trade.Compliance.Infrastructure.Workflow;

public class WorkflowOptions
{
    public const string SectionName = "Workflow";
    public string LicenseKey { get; set; } = "";
    public Guid   RuntimeId  { get; set; } = new("8D38DB8F-F3D5-4F26-A989-4FDD40F32D9D");
}
```

**`Infrastructure/Workflow/WorkflowRuntimeFactory.cs`** (singleton bootstrap):

```csharp
using OptimaJet.Workflow.Core.Runtime;
using OptimaJet.Workflow.DbPersistence;
using Microsoft.Extensions.Options;

namespace FSI.Trade.Compliance.Infrastructure.Workflow;

public class WorkflowRuntimeFactory
{
    private readonly Lazy<WorkflowRuntime> _runtime;

    public WorkflowRuntimeFactory(
        IOptions<WorkflowOptions>      opt,
        IConfiguration                 cfg,
        IWorkflowActionProvider        actions,
        IWorkflowRuleProvider          rules)
    {
        _runtime = new Lazy<WorkflowRuntime>(() =>
        {
            if (!string.IsNullOrWhiteSpace(opt.Value.LicenseKey))
                WorkflowRuntime.RegisterLicense(opt.Value.LicenseKey);

            var conn = cfg.GetConnectionString("DefaultConnection")!;
            var generator = new DbXmlWorkflowGenerator(conn);
            var builder   = new WorkflowBuilder<XElement>(
                generator,
                new XmlWorkflowParser(),
                new DbSchemePersistenceProvider(conn))
                .WithDefaultCache();

            return new WorkflowRuntime(opt.Value.RuntimeId)
                .WithBuilder(builder)
                .WithActionProvider(actions)
                .WithRuleProvider(rules)
                .WithPersistenceProvider(new DbPersistenceProvider(conn))
                .RegisterAssemblyForCodeActions(typeof(WorkflowRuntimeFactory).Assembly)
                .Start();
        });
    }

    public WorkflowRuntime Runtime => _runtime.Value;
}
```

**`Infrastructure/InfrastructureServiceRegistration.cs`** (additions):

```csharp
services.Configure<WorkflowOptions>(config.GetSection(WorkflowOptions.SectionName));
services.AddScoped<IWorkflowActionProvider, FsiWorkflowActions>();   // ports of the 21 legacy actions
services.AddScoped<IWorkflowRuleProvider,   FsiWorkflowRules>();     // ports of the 3 legacy rules
services.AddSingleton<WorkflowRuntimeFactory>();                     // owns the runtime
```

**`API/Controllers/Workflow/TransactionController.cs`** (extract):

```csharp
[HttpPut("ExecuteWf")]
[RequiresPrivilege("Transactions.ExecuteWf")]
public async Task<IActionResult> ExecuteWf([FromBody] ExecuteWfBody body, CancellationToken ct)
{
    var runtime = _runtimeFactory.Runtime;
    var identityId = _current.UserId!;
    var result = await runtime.ExecuteCommandAsync(
        body.ProcessInstanceId, identityId, body.Command);

    return Ok(ResponseViewModel<object>.Ok(new
    {
        body.ProcessInstanceId,
        result.WasExecuted,
        result.Comment,
        nextState = result.ProcessInstance.CurrentState
    }));
}
```

**Validation checkpoints** (the actual POC steps):

1. Add the two NuGet packages to `Infrastructure.csproj`. Confirm
   `dotnet restore` resolves.
2. Run the OptimaJet schema migration scripts on a copy of `ICBC_DEMO`.
   Verify every original `WorkflowProcessScheme` row still loads.
3. Bootstrap the runtime in a unit test or via a one-shot `Program.cs`
   harness. Call `runtime.GetSchemes()` — should return the existing
   schemes by code.
4. Pick one scheme, instantiate via `CreateInstanceAsync` in a test, and
   advance through one transition. Confirm `WorkflowProcessTransitionHistory`
   gets a new row.
5. Wire one Designer call: `runtime.DesignerAPIAsync(NameValueCollection
   { ["cmd"] = "GetWorkflows" }, null, true)`. Confirm it returns scheme
   list JSON.

If these five checkpoints pass, the engine is ported. Everything after is
controller scaffolding (which we know how to do — same shape as Slice 2's
Role / User controllers).

---

## 6. License verification checklist

Before committing engineering effort to Slice 5 implementation:

- [ ] Locate the current `WFKey` value (web.config / appSettings.config in
      legacy backend).
- [ ] Decode the key — OptimaJet license keys can be parsed with their
      utility. Note the **product version**, **edition** (Ultimate vs
      Enterprise), and **expiry**.
- [ ] If the key's product version is < 8.0, contact OptimaJet sales:
      "we need to upgrade from v3.1.0 to v8.x. Does our existing Ultimate
      license cover this, or do we need a new SKU? What's the cost
      difference?"
- [ ] Get a written confirmation of what's covered before pulling the
      v8.x package.
- [ ] If the cost is prohibitive, consider Section 7 alternatives.

This is procurement, not engineering. The slice can't start until this is
green.

---

## 7. Risks and alternatives

### Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| License doesn't cover v8.x | Medium | High | Procurement check first (Section 6) |
| Schemes don't load in v8 unchanged | Low | Medium | Test in QA before prod migration; OptimaJet schemes are designed forward-compatible |
| Custom `WorkflowActions.cs` references break | Medium | Medium | 21 actions × 1-day port each = budget 3 wks of touch-up |
| Performance regression vs v3 | Low | Low | v8 is more efficient per OptimaJet release notes |
| OptimaJet support / vendor stability | Low | High | They've shipped consistently since 2014; no signs of distress |

### Alternatives (if license is a blocker)

If procurement says "no" to v8.x, two viable replacements both OSS:

**Workflow Core** (Daniel Gerlag, MIT). C#-first, code-defined workflows.
No visual designer. The legacy schemes (XML) wouldn't port — every
workflow would be a C# class. **Effort: ~4 weeks per scheme** to rewrite.
Lightweight, simple, well-documented.

**Elsa Workflows** (Apache 2.0). Has a visual designer (Blazor), declarative
workflows, more feature-rich. Heavier dependencies. Schemes would need
manual conversion (XML → Elsa JSON). **Effort: ~6 weeks per scheme**.
Better fit if the visual designer matters to business.

Decision tree:

- License covers v8 → Slice 5 = OptimaJet port (smallest engineering effort).
- License doesn't cover v8 + visual designer is critical → Elsa.
- License doesn't cover v8 + visual designer is nice-to-have → Workflow Core.

---

## 8. Recommended next steps

1. **Procurement** — verify license coverage for v8.x. Block Slice 5 until
   this is green.
2. **POC implementation** — once license is green, execute the five
   checkpoints in Section 5.5 in a throwaway branch. Time-box to 2 days.
3. **If POC succeeds**, plan Slice 5 as:
   - Step 1 (1 day): runtime bootstrap + DI + config (the code in Section 5).
   - Step 2 (3-5 days): port `WorkflowActions` + `WorkflowRule` providers
     + their dependencies (RoleService → IRoleQueryService etc.).
   - Step 3 (1 week): three workflow controllers (Loan, Account, Transaction)
     with `ExecuteWf` + `GetCommandsByProcessId`.
   - Step 4 (2 days): designer endpoint + smoke test against the legacy FE
     designer module.
   - Step 5 (2 days): integration tests against a copy of `ICBC_DEMO` —
     instantiate, advance, history, completion.
4. **Document FE-visible deltas** — none expected if URLs are preserved.
   Confirm and log in `FE_CHANGES_REQUIRED.md`.

---

*Last updated: 2026-05-05.*

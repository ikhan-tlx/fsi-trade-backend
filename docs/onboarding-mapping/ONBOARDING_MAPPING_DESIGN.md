# Configurable Onboarding Payload Mapping Framework — Design

**Status:** Proposal (pre-implementation)
**Scope:** KYC → FCCM onboarding submission (`InitiateOnboardingService/OB/Initiate`)
**Author:** BE/Full-stack
**Related code:** `FccmKycCaseService`, `KycOnboardingFieldResolver`, `FccmOnboardingPayload`, `FccmHttpClient`, `FccmOnboardingOptions`

---

## 0. TL;DR

Today the onboarding payload is a hardcoded C# object graph (`FccmOnboardingPayload` /
`FccmOnboardingCustomer`) populated by hardcoded field assignments in
`FccmKycCaseService.SubmitAsync`, with the risk-parameter derivations hardcoded in
`KycOnboardingFieldResolver`. Every environment that wants a different payload shape
(the attached FCCM-v2 payload is radically different from the legacy one) requires a
**code change + redeploy**.

This document proposes an **externalized, declarative mapping framework**: each
environment ships a mapping profile (JSON) that describes *how to build its onboarding
payload from the same internal KYC data*. A runtime **mapping loader/switcher** selects
the active profile by configuration, and a generic **payload mapper engine** walks the
profile to emit the payload. Adding/adjusting a client's payload becomes a **config
change, not a code change**.

---

## 1. Analysis of the provided onboarding payload

The attached FCCM-v2 payload differs from the legacy `KYCRequestModel` in every
structural dimension:

| Dimension | Legacy payload | Attached FCCM-v2 payload |
|---|---|---|
| Top-level keys | `applicationId`, `requestUserId`, `OnboardingCustomer` | adds `infodom`, `RequestID`, `PMFProcess`, `SyncAPIFlag`, `locale` |
| `OnboardingCustomer` shape | **flat**, ~32 scalar fields | **deeply nested**, ~60 scalars **plus 12+ child arrays** |
| Arrays | none | `OnboardingCustomerPhone[]`, `OnboardingCustomerAddress[]`, `OnboardingCustomerIdentification[]`, `OnboardingCustomerProduct[]`, `OnboardingCustomerCountry[]`, `OnboardingCustomerCreditRating[]`, `OnboardingCustomerAnticipatoryProfile[]`, `OnboardingCustomerEmailAddress[]`, `OnboardingCustSrcOfWth[]`, `OnboardingCustomerInitialAsset[]`, `OnboardingCustomerMarketServed[]`, … |
| Nested arrays-in-arrays | none | `OnboardingCustomerRelatedCustomer[]` — each element is itself a **full customer** with its **own** `OnboardingCustomerPhone[]`, `OnboardingCustomerAddress[]`, `OnboardingCustomerIdentification[]`, `OnboardingCustomerRelationship[]`, `OnboardingCustomerFinancials[]`, … |
| Value encodings | plain strings | mixed: strings, integers (`AuditCollectedFlag: 1`), Y/N flags (`PrimaryCustomer: "Y"`, `PEPFlag: "Y"`), booleans-as-int (`DomesticPEP: 1`, `ForeignPEP: 0`), formatted dates (`"12-26-2012"`, `MM-dd-yyyy`) |
| Field names | e.g. `OVER_IMP`, `HS_CD`, `DEF_SECT` (risk codes) | business-named (`CountryOfIncorporation`, `DateOfIncorporation`, `RiskRating`) |

Key structural challenges the mapping must handle:

1. **Arbitrary target JSON paths** — `OnboardingCustomer.OnboardingCustomerAddress[0].Country`.
2. **Array construction from two different sources** — some arrays are one synthesized
   element (a single address), others are one element **per source grid row**
   (`OnboardingCustomerCountry[]` from a country-screening grid).
3. **Recursive nesting** — related customers are full customer sub-documents; the same
   element rules must apply at any depth.
4. **Type coercion & encoding** — internal values are strings/lookup-ids; the target
   wants ints, `Y`/`N`, `1`/`0`, and specific date formats.
5. **The internal *source* of truth is identical across environments** — it is always the
   same SQL Server KYC storage (`TmX_Transaction_Detail.udf_data`,
   `TmX_Customer_Master.udf_data` grids, `TmX_Lookup.Hidden_Value`) plus the FCCM Oracle
   max-score lookup and a few static constants. **Only the output shape varies.** That is
   precisely the property that makes a declarative mapping viable.

---

## 2. Why hardcoded mappings become a maintenance problem

- **Every client is a code fork.** The legacy payload and the FCCM-v2 payload cannot both
  be produced by one hardcoded `FccmOnboardingPayload` class. A third client with a fourth
  shape means a third code path, more `if (env == …)` branches, and combinatorial toggles
  (we already have `IncludeCustomerName`, `IncludeNameParts`, `IncludeRiskParameterFields`
  — this pattern does not scale).
- **Ship cycle coupling.** A field rename or a new array on the FCCM side today requires a
  recompile, PR, and redeploy of the whole backend — for what is really a data-shape config
  change owned by integrations/implementation teams, not developers.
- **Risk of regression.** Editing the shared builder for client B can break client A. There
  is no isolation between environments.
- **Poor auditability.** "What exactly do we send to FCCM in prod X?" is answered by reading
  C# across three files and mentally evaluating toggles, instead of reading one declarative
  file.
- **Duplication.** `FccmOnboardingCustomer` (DTO) + `KycOnboardingFieldResolver` (derivation)
  + `FccmKycCaseService` (assignment) all encode the same field list three times; they drift.
- **No safe backward-compat path.** Turning the risk fields on/off is a boolean; there is no
  clean way to say "prod A gets the exact legacy shape, prod B gets FCCM-v2."

---

## 3. Configurable mapping architecture

The internal derivation logic stays; only the *assembly of the output* becomes data-driven.

```
                       ┌─────────────────────────────────────────────┐
                       │        Mapping profiles (on disk)            │
                       │  onboarding.legacy.mapping.json              │
                       │  onboarding.fccm-v2.mapping.json             │
                       │  onboarding.<clientX>.mapping.json           │
                       └───────────────┬─────────────────────────────┘
                                       │ (selected by config)
                                       ▼
 Integration:Onboarding:MappingProfile ── IOnboardingMappingProvider (loader/switcher)
                                       │  • resolve profile name → file
                                       │  • validate vs mapping.schema.json
                                       │  • resolve "extends" (inheritance)
                                       │  • cache + optional hot-reload (file watcher)
                                       ▼
                              MappingProfile (in-memory model)
                                       │
   ┌───────────────────────────────┐  │   ┌───────────────────────────────────────┐
   │  MappingContext (per submit)  │  │   │  Source resolvers (reused existing code)│
   │  • customer snapshot row      │──┼──▶│  const | context | snapshot            │
   │  • detail udf JsonDocument    │  │   │  udf.direct | udf.hidden               │
   │  • customer udf JsonDocument  │  │   │  grid.maxScore (Oracle) | grid.first   │
   │  • runtime ctx (actor, reqId, │  │   │  grid.rows (for array-per-row)         │
   │    applicationId, infodom…)   │  │   │  expression (optional)                 │
   └───────────────────────────────┘  │   └───────────────────────────────────────┘
                                       ▼
                          OnboardingPayloadMapper (engine)
                          • walk field rules → set target JSON paths
                          • walk array rules → build elements (single / per-row / nested)
                          • apply transforms (dateFormat, valueMap, cast, default, trim)
                          • honor omitWhenNull / emitNulls
                                       ▼
                            System.Text.Json JsonObject
                                       ▼
                    FccmHttpClient.SubmitCaseAsync(json)  (unchanged)
```

### Components

- **Mapping profile** — a JSON document describing the target payload declaratively
  (metadata, settings, constants, `fields[]`, `arrays[]`). One per environment/contract.
- **`IOnboardingMappingProvider`** (loader/switcher) — resolves the active profile by
  config, loads + schema-validates + caches it, resolves inheritance (`extends`), and
  exposes the parsed `MappingProfile`. Supports hot-reload via `IOptionsMonitor`/file watch.
- **`MappingContext`** — everything one submission can read from: the customer snapshot
  row, the two `udf_data` JSON docs (detail + customer), and runtime context values
  (`actor`, `fccmRequestId`, `applicationId`, `infodom`, `locale`, `transactionId`).
- **Source resolvers** — the existing derivation primitives (already implemented in
  `KycOnboardingFieldResolver` / `FccmOracleReader`) re-exposed as named "source kinds"
  the engine can invoke: `const`, `context`, `snapshot`, `udf.direct`, `udf.hidden`,
  `grid.maxScore`, `grid.first`, `grid.rows`, `expression`.
- **`OnboardingPayloadMapper`** — the generic engine that turns a `MappingProfile` +
  `MappingContext` into a `JsonObject`. No field names are hardcoded here.
- **`FccmHttpClient`** — unchanged; it just serializes and POSTs the produced JSON.

### The mapping DSL (concepts)

A **field rule** binds one target path to one source, optionally transformed:

```jsonc
{
  "target": "OnboardingCustomer.CountryOfResidence", // dotted path; [] for arrays
  "from":   { "kind": "grid.maxScore", "grid": "TmxKYCSPOR", "param": "KycParam29" },
  "transform": [ { "op": "default", "value": "PAK" } ],
  "omitWhenNull": true
}
```

An **array rule** builds an array-valued target from either a single synthesized element
or one element per source grid row; elements are themselves `fields[]` + `arrays[]`
(so nesting is recursive):

```jsonc
{
  "target": "OnboardingCustomer.OnboardingCustomerCountry",
  "mode": "perRow",                 // "single" | "perRow"
  "rows": { "grid": "TmxKYCSPOR", "hydrate": "hidden" },   // source of rows (perRow)
  "element": {
    "fields": [ { "target": "Country", "from": { "kind": "row.value" } } ],
    "arrays": []                    // nested arrays allowed here
  }
}
```

**Source kinds** (map 1:1 to the derivation mechanisms already in the codebase):

| kind | meaning | backing code |
|---|---|---|
| `const` | literal value | — |
| `context` | runtime value: `actor`, `fccmRequestId`, `applicationId`, `infodom`, `locale`, `transactionId` | `FccmKycCaseService` context |
| `snapshot` | column from `TmX_Customer_Master` snapshot (`CustomerCode`, `CustomerName`, …) | snapshot row |
| `udf.direct` | raw `KycParam*` from `TmX_Transaction_Detail.udf_data` | `KycOnboardingFieldResolver.Raw` |
| `udf.hidden` | detail `KycParam*` → `Lookup_ID` → `TmX_Lookup.Hidden_Value` | `.HiddenOf` |
| `grid.maxScore` | grid rows → hidden values → Oracle `FCC_OB_RISK_PRMS_CODE_SCRE_MAP` max SCORE | `FccmOracleReader.GetMaxScoreCodeValueAsync` |
| `grid.first` | first grid row's hidden value | HS_CD path |
| `grid.rows` / `row.value` | iterate grid rows to build array elements | (new thin wrapper over grid enumeration) |
| `expression` | small safe expression over already-resolved values (optional, phase 2) | — |

**Transforms** (composable list, applied in order): `default`, `trim`, `upper`, `lower`,
`dateFormat` (`from`/`to` patterns), `valueMap` (dictionary, e.g. `{ "Yes":"Y","No":"N" }`),
`cast` (`int`/`bool`/`string`), `const` (override).

---

## 4. JSON vs XML for the mapping definitions — recommendation: **JSON** (YAML optional)

**Recommendation: JSON**, for these reasons, with YAML as an optional author-friendly
superset if human editing comfort matters.

| Criterion | JSON | XML |
|---|---|---|
| Matches the target artifact | ✅ target payload **is JSON** — mapping-in-JSON keeps one mental model; sample snippets paste directly | ❌ impedance mismatch (mapping in XML, output in JSON) |
| .NET tooling | ✅ first-class `System.Text.Json`; already used everywhere in this codebase | ✅ mature (`System.Xml`, XSD) but heavier |
| Validation | ✅ JSON Schema (`mapping.schema.json`) for editor IntelliSense + CI gate | ✅ XSD (also good) |
| Verbosity / hand-editing | ✅ concise; arrays are natural | ❌ verbose; arrays via repeated elements |
| Path expressions | ✅ JSONPath / dotted paths are idiomatic for a JSON target | ⚠️ XPath targets a JSON doc awkwardly |
| Transformation power | ⚠️ needs a small custom transform vocabulary (we define it) | ✅ XSLT is powerful but a heavy, separate skillset |
| Comments | ❌ standard JSON has none → mitigate with `"description"` fields, or adopt **JSONC/JSON5/YAML** | ✅ native comments |
| Diff / review | ✅ clean line diffs | ⚠️ noisier |

**Net:** JSON aligns with the payload, the stack, and the team's tooling. XML's only real
edges (comments, XSLT) are outweighed by the JSON-to-JSON alignment and lower friction. If
comments and terser authoring are important, store the profiles as **YAML** (a JSON
superset) and parse to the same model — the engine is format-agnostic below the loader. We
add a **JSON Schema** so profiles get editor validation and a CI check regardless.

---

## 5. Mapping loader / switcher — runtime selection across environments

**Selection:** a single config key chooses the active profile:

```jsonc
"Integration": {
  "Onboarding": {
    "MappingProfile": "fccm-v2",            // logical name → onboarding.fccm-v2.mapping.json
    "MappingDirectory": "Mappings/onboarding", // relative to ContentRoot, or absolute
    "MappingHotReload": true                 // watch file, reload on change (non-prod)
  }
}
```

**`IOnboardingMappingProvider` responsibilities:**

1. **Resolve** `MappingProfile` → file `MappingDirectory/onboarding.{profile}.mapping.json`
   (or an absolute path override for per-tenant deployments).
2. **Load + parse** with `System.Text.Json` into the strongly-typed `MappingProfile` model.
3. **Validate** against `mapping.schema.json`; on failure → structured log + fail fast at
   startup (so a bad profile never silently ships a wrong payload), or fall back to a
   pinned default profile (configurable `OnFatal: FailFast | FallbackToLegacy`).
4. **Resolve inheritance** — if the profile declares `"extends": "legacy"`, deep-merge this
   profile *over* the base (child field rules override base rules with the same `target`).
   This gives DRY reuse and clean backward-compat overrides.
5. **Cache** the compiled profile (singleton). With `MappingHotReload`, watch the file and
   atomically swap the cached instance on change (great for tuning a new client without
   redeploys; disable in prod for immutability).
6. **Expose** `MappingProfile GetActive()` (and `Get(string name)` for A/B or per-tenant).

**Multi-environment / multi-tenant:** the profile name can come from static config
(per deployment) **or** be resolved per request (e.g., `profile = tenant.MappingProfile`)
for a shared multi-tenant host — the provider caches by name, so N tenants ⇒ N cached
profiles. `FccmKycCaseService.SubmitAsync` changes from "new up a hardcoded object" to:

```csharp
var profile = _mappingProvider.GetActive();               // or Get(tenant.Profile)
var ctx     = await _contextBuilder.BuildAsync(req, ct);  // snapshot + udf + runtime ctx
var json    = _payloadMapper.Map(profile, ctx);           // JsonObject
var (rid, err) = await _http.SubmitCaseAsync(json, req.CustomerId, ct);
```

---

## 6. Example mapping configuration

Two complete, runnable-shaped starter profiles ship with this design:

- **`onboarding.legacy.mapping.json`** — reproduces the legacy `KYCRequestModel` payload
  exactly (flat, 32 `OnboardingCustomer` fields + top-level `applicationId`/`requestUserId`),
  using the exact source bindings from `SetRealtimeValues`. This is the backward-compat
  baseline.
- **`onboarding.fccm-v2.mapping.json`** — a representative mapping for the attached rich
  payload: top-level envelope, nested `OnboardingCustomer`, a **single-element** array
  (`OnboardingCustomerAddress`), a **per-row** array (`OnboardingCustomerCountry`), a
  transform example (dateFormat, valueMap Y/N, cast int), and a **nested array-in-array**
  (`OnboardingCustomerRelatedCustomer[] → OnboardingCustomerPhone[]`).

Representative excerpt (see the files for the full set):

```jsonc
// scalar with default + omit-when-null
{ "target": "OnboardingCustomer.CountryOfResidence",
  "from": { "kind": "grid.maxScore", "grid": "TmxKYCSPOR", "param": "KycParam29" },
  "transform": [ { "op": "default", "value": "PAK" } ], "omitWhenNull": true },

// Y/N flag from a lookup value + cast
{ "target": "OnboardingCustomer.DomesticPEP",
  "from": { "kind": "udf.direct", "key": "KycParam40" },
  "transform": [ { "op": "valueMap", "map": { "Yes": "1", "No": "0" }, "default": "0" },
                 { "op": "cast", "to": "int" } ] },

// single-element array (one address)
{ "target": "OnboardingCustomer.OnboardingCustomerAddress", "mode": "single",
  "element": { "fields": [
    { "target": "AddressPurpose", "from": { "kind": "const", "value": "P" } },
    { "target": "Country",        "from": { "kind": "grid.maxScore", "grid": "TmxKYCSPOR", "param": "KycParam29" } },
    { "target": "City",           "from": { "kind": "udf.direct", "key": "KycParam50" } }
  ] } },

// per-row array (one element per grid row)
{ "target": "OnboardingCustomer.OnboardingCustomerCountry", "mode": "perRow",
  "rows": { "grid": "TmxKYCSPOR", "hydrate": "hidden" },
  "element": { "fields": [ { "target": "Country", "from": { "kind": "row.value" } } ] } }
```

> Note: FCCM-v2 fields with no legacy equivalent (addresses, identification, dates) use
> **placeholder** `KycParam*`/grid names annotated `TODO: bind`. The real bindings come from
> that environment's KYC form field map — which is exactly the point: they're set in the
> config file, never in code.

---

## 7. Best practices — extensibility, maintainability, backward compatibility

**Backward compatibility**

- Ship `onboarding.legacy.mapping.json` reproducing the *exact* current legacy payload;
  set `MappingProfile: legacy` for legacy environments → **zero behavioral change**.
- Keep the existing hardcoded path behind a `MappingProfile: none` (or feature flag) for
  one release as a safety net, then remove once the mapper is proven in each env.
- Profile **`version`** + **`targetContract`** fields; the loader records both in logs and
  in the FCCM diagnostics so "what shape did we send" is always answerable.

**Extensibility**

- **`extends` inheritance:** a new client that is "legacy plus 3 fields" declares
  `"extends": "legacy"` and only lists its overrides.
- **Open source-kind registry:** source kinds and transforms are registered handlers
  (`IReadOnlyDictionary<string, ISourceResolver>` / `ITransform`), so adding a new
  derivation (e.g., a new upstream) is a small handler + a schema enum entry — no engine
  rewrite.
- **`expression` escape hatch** (phase 2, sandboxed) for the rare field that needs light
  computation, so 95% stays declarative and the 5% doesn't force a code change.

**Maintainability & safety**

- **JSON Schema (`mapping.schema.json`)** → editor IntelliSense + a CI gate that fails the
  build on an invalid profile.
- **Fail-fast validation at startup** for the active profile; never silently emit a wrong
  payload.
- **`omitWhenNull` / `emitNulls` per profile** to match each target's strictness (the
  current env 400s on unknown/extra properties — the mapping controls exactly what is sent).
- **Golden-file tests:** for each profile, a fixture of sample KYC data → expected payload
  JSON, asserted in unit tests (this replaces the hand-verified toggles with regression
  coverage per environment).
- **Verbose diagnostics:** the mapper logs the resolved payload (already wired via
  `Integration:Diagnostics:FccmVerbose`) with the active profile name + version.
- **Separation of concerns:** derivation logic (how to read KYC data) stays in code and is
  reused; *shape* lives in config. Don't push heavy business logic into the mapping DSL —
  keep it declarative.
- **Secrets never in mappings:** auth/URLs stay in `Integration:Onboarding` options; the
  mapping file is pure shape/binding and safe to store in source control per client.

---

## Appendix — files in this proposal

| File | Purpose |
|---|---|
| `ONBOARDING_MAPPING_DESIGN.md` | this document |
| `mapping.schema.json` | JSON Schema for the mapping DSL (validation + IntelliSense) |
| `onboarding.legacy.mapping.json` | legacy `KYCRequestModel` payload as a mapping profile (backward-compat baseline) |
| `onboarding.fccm-v2.mapping.json` | representative mapping for the attached FCCM-v2 payload (nested + arrays + transforms) |

Intended runtime location for the profiles: `FSI.Trade.Compliance.API/Mappings/onboarding/`
(copied to output; overridable via `Integration:Onboarding:MappingDirectory`).

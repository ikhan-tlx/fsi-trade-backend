# REST API Guidelines — FSI.Trade.Compliance

This document is the **single source of truth** for how endpoints are
designed, secured, validated, documented, and shipped on the
`FSI.Trade.Compliance` backend. Every new endpoint MUST follow these rules.
When in doubt, do what the existing endpoints do — they were built to set
the precedent.

If something here ever conflicts with what you find in code, the code wins
and this document gets updated. Don't perpetuate drift.

> **DB schema discipline.** Before writing any DB-touching code — new entity,
> new EF config, new LINQ over an existing table or view — read
> [`docs/SCHEMA_REFERENCE.md`](./docs/SCHEMA_REFERENCE.md) and confirm column
> shapes against `D:\ICBC - Latest\ICBC_DEMO-Schema.sql`. Legacy EF6 boilerplate
> can drift from the live schema — the schema export wins.

---

## Table of contents

1. URL design and HTTP method semantics
2. HTTP status codes
3. Response envelope and JSON conventions
4. Pagination, sorting, filtering
5. Authentication and authorisation
6. API versioning
7. Validation
8. Error handling
9. Auditing and idempotency
10. Performance
11. Security
12. Documentation
13. RBAC seed file discipline
14. FE↔backend alignment discipline
15. Checklist for adding a new endpoint

---

## 1. URL design and HTTP method semantics

### URL structure

```
/api/v{version:apiVersion}/[Resource]/[id]/[Sub-Resource-or-Action]
```

Concrete examples:

```
GET    /api/v1/Role
GET    /api/v1/Role/123
POST   /api/v1/Role
PUT    /api/v1/Role/123
POST   /api/v1/Role/123/Activate
GET    /api/v1/Role/123/Privileges
PUT    /api/v1/Role/123/Privileges
```

Rules:

- **Resources are nouns**, not verbs. `Role`, not `GetRole` or `ManageRole`.
- **Singular** resource names. Existing convention: `/Role`, `/User`,
  `/Privileges` (the last is plural because the resource is the catalog,
  not an individual privilege). When in doubt, follow the existing
  controller naming.
- **PascalCase** controller-derived segments — that's what
  `[controller]` produces, and the FE is built against this. ASP.NET
  Core routing is case-insensitive, so the FE can call `/role` or `/Role`
  and both work; the canonical form in docs and code is PascalCase.
- **Sub-resources** for things owned by a parent: `/Role/{id}/Privileges`,
  `/User/{id}/Sessions`.
- **Verb-style sub-paths** are ONLY for state transitions that don't fit
  REST (`/Activate`, `/Deactivate`, `/UnlockUser`). Prefer model fields
  + PUT when possible; reach for verb sub-paths when the action is more
  natural (ergonomics > purity).

### HTTP method semantics

| Verb | Use for | Idempotent? | Body? |
|---|---|---|---|
| `GET` | Read a resource or list | Yes | No (query params only) |
| `POST` | Create a resource OR perform a non-idempotent action | No (typically) | Yes |
| `PUT` | Full replace OR idempotent state transition (e.g. Activate) | Yes | Yes (sometimes empty) |
| `PATCH` | Partial update (rarely used today; prefer PUT) | Yes | Yes |
| `DELETE` | Revoke / soft-delete a resource | Yes | No (typically) |

Resource lifecycle conventions for **this project**:

- **Activate / Deactivate**: `POST /Resource/{id}/Activate` and
  `POST /Resource/{id}/Deactivate`. Idempotent — calling Activate on an
  already-active resource returns 200 with no error.
- **Bulk replace** (e.g. role's privilege grants):
  `PUT /Resource/{id}/Sub-Resources` with the full desired set in the body.
  Server diffs and applies additions + removals atomically.
- **Soft delete**: prefer `Active_Flag = 0` over actual `DELETE` for any row
  with FK references. True `DELETE` only for transient resources (refresh
  tokens, audit log archival).

---

## 2. HTTP status codes

| Code | When | Envelope |
|---|---|---|
| **200 OK** | Success with body. Default for our `return Ok(ResponseViewModel<T>.Ok(...))`. | `{ status: { code: 200, ... }, data: ... }` |
| **201 Created** | Reserved for future use; today `POST` returns 200. | n/a |
| **204 No Content** | Reserved; we always return 200 with at least a minimal `data`. | n/a |
| **400 Bad Request** | Validation failure, malformed input, AuthenticationException with non-credential code. | `{ status, data: { success: 0, code: "...", errors? } }` |
| **401 Unauthorized** | No valid JWT, or middleware-level auth failure. | `{ status, data: { success: 0, code: "unauthorized" } }` |
| **403 Forbidden** | Authenticated but missing required privilege. From `RequiresPrivilegeAttribute`. | `{ status, data: { success: 0, code: "forbidden_privilege", required: "..." } }` |
| **404 Not Found** | `NotFoundException` thrown — resource doesn't exist. | `{ status, data: { success: 0, code: "role_not_found", ... } }` |
| **409 Conflict** | `ConflictException` thrown — uniqueness or state violation. | `{ status, data: { success: 0, code: "role_name_taken", ... } }` |
| **500 Internal Server Error** | Unhandled exception. Generic envelope. | `{ status, data: { success: 0, code: "internal_error" } }` |

`ExceptionHandlingFilter` enforces every entry in this table — new
exception types should add a `case` there, not be handled ad-hoc in
controllers.

---

## 3. Response envelope and JSON conventions

### The envelope

Every response (success OR failure) carries this single shape:

```json
{
  "status": {
    "code": 200,
    "message": "OK",
    "description": null
  },
  "data": {
    "...feature-specific payload..."
  }
}
```

**Nothing** lives at the top level outside `status` and `data`. No
top-level tokens, pagination, errors, or metadata.

On failure, `data.success === 0` is set so the FE's
`isExplicitValidationFailure` check works for every error class
uniformly.

### JSON conventions

- **camelCase** field names always: `userId`, `createdDate`, not `UserID`
  or `created_date`. ASP.NET Core's default serializer handles the
  PascalCase-in-C# → camelCase-on-wire conversion automatically.
- **Dates**: ISO 8601 UTC with `Z` suffix —
  `"2026-05-05T14:23:11Z"`. `DateTime.UtcNow` everywhere; never
  `DateTime.Now`.
- **Numbers**: numeric types, not strings. Exception: financial amounts
  with sub-cent precision — wrap in `decimal` and accept that JS
  precision applies on the wire.
- **Booleans**: `true` / `false`, never `0` / `1`. The DB's
  `Active_Flag bit` maps cleanly to `bool` in EF.
- **Nulls**: explicit `null` for "no value", not empty string. Don't
  serialize `Optional<T>` or sentinel values.
- **IDs**: integer for surrogate keys (`roleId`, `userId` when GUID-less),
  string for natural keys (`userId` when nvarchar in TmX_User).

---

## 4. Pagination, sorting, filtering

Every list endpoint takes `PagedQuery` (in
`Application/Common/Models/PagedResult.cs`):

```csharp
public class PagedQuery
{
    public int     Page     { get; set; } = 1;     // 1-based
    public int     PageSize { get; set; } = 10;
    public string? Sort     { get; set; }          // "field-direction" e.g. "createdDate-desc"
    public string? Filter   { get; set; }          // free-text substring
}
```

Response wraps the items in `PagedResult<T>`:

```json
"data": {
  "items":   [ ... ],
  "total":   42,
  "page":    1,
  "pageSize": 10
}
```

Rules:

- **Always cap pagination**. `PageSize` is clamped to `[1, 1000]`. Never
  return unbounded result sets.
- **Sort syntax** is Kendo-compatible: `field-direction`. Multiple sorts
  comma-separated. Only the first is honoured today; document anything
  else.
- **Whitelist sortable fields** in the handler's `ApplySort` switch.
  Unrecognised fields fall back to a sensible default
  (`createdDate-desc`).
- **Filter** is a free-text substring match across whitelisted columns
  (LIKE `%filter%`). Operator-based filters (eq, gt, in) come later;
  not in scope today.
- The FE may send redundant `take/skip` params alongside `page/pageSize`
  (Kendo grid does this). Read `page/pageSize` and ignore the redundant
  pair.

---

## 5. Authentication and authorisation

### Authentication

- **JWT Bearer** in `Authorization: Bearer <token>` header.
- **X-Device-Id** required on every authenticated request after the FE
  has one (issued on first login). Configured in
  `Auth.RequireDeviceIdHeader` and enforced by `DeviceTrackingMiddleware`.
  Login / Refresh / ResetExpiredPassword / Health are exempt.

### Authorisation — three layers

1. **`[AllowAnonymous]`**: explicit, only for endpoints that genuinely
   shouldn't require auth (Login, Refresh, Health,
   ResetExpiredPassword). Add a comment justifying why.
2. **`[Authorize]`**: applied at the controller level for any controller
   serving authenticated users. Requires a valid JWT. **No exceptions**
   — any controller without `[AllowAnonymous]` MUST have `[Authorize]`.
3. **`[RequiresPrivilege("Module.Action")]`**: per-action authz. Reads
   role-name claims from the JWT, resolves the privilege set via
   `IPrivilegeService`, caches the set in `HttpContext.Items`, and
   returns 403 if the required code isn't granted. The bootstrap escape
   hatch (`Auth:BootstrapAdminRoles`) lets named roles bypass the check
   until grants are wired — see `RequiresPrivilegeAttribute.cs`.

### Privilege code naming

```
Module.Action
```

Examples: `Users.View`, `Users.Create`, `Roles.Manage`,
`Privileges.View`. Modules are PascalCase plural nouns; actions are
PascalCase verbs. The dot is the entity-grouping separator parsed by
`/api/v1/Privileges/Entities`. Don't use spaces, slashes, or other
delimiters.

---

## 6. API versioning

Configured via `Asp.Versioning.Mvc` in `Program.cs`. Three readers, in
priority order:

1. **URL segment (canonical)**: `/api/v1/Auth/Login`. The FE calls this.
2. **`X-API-Version` header**: optional, useful for client-level
   pinning.
3. **`?api-version` query string**: optional, useful for debugging.

If none are supplied, the server assumes `1.0` (back-compat).

Server reports `api-supported-versions: 1.0` on every response. When v2
ships, the value becomes `1.0, 2.0` and the FE can detect proactively.

When introducing v2 of an endpoint:

```csharp
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]                              // ADD this
[Route("api/v{version:apiVersion}/[controller]")]
public class FooController : ControllerBase
{
    [HttpGet]                                    // existing v1 action
    [MapToApiVersion("1.0")]
    public IActionResult GetV1() { ... }

    [HttpGet]                                    // new v2 action, same URL
    [MapToApiVersion("2.0")]
    public IActionResult GetV2() { ... }
}
```

**Never** copy-paste a controller for a new version.

---

## 7. Validation

### FluentValidation pipeline

Every command / query that takes input has a sibling `*Validator.cs`
inheriting `AbstractValidator<TCommand>`. Auto-registered by
`ApplicationServiceRegistration.cs`:

```csharp
services.AddValidatorsFromAssembly(asm);
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
```

`ValidationBehaviour` runs every validator before the handler executes.
Failures throw `ValidationException`, caught by
`ExceptionHandlingFilter` → HTTP 400 with structured errors:

```json
{
  "status": { "code": 400, "message": "validation_failed", ... },
  "data": {
    "success": 0,
    "code": "validation_failed",
    "errors": {
      "RoleName": [ "'Role Name' must not be empty." ],
      "RoleDescription": [ "'Role Description' must be 200 characters or fewer." ]
    }
  }
}
```

### Validator rules

- **Length limits everywhere**. Match the DB column
  (`MaximumLength(100)` for `nvarchar(100)`).
- **NotEmpty on required fields**. Use `NotNull` only for nullable types
  where the difference matters.
- **GreaterThan(0) for IDs** that come from the URL or body.
- **Cardinality caps on collections**. The privilege bulk-replace rejects
  > 500 IDs; pick a sane upper bound for every list parameter.
- **Don't duplicate logic the handler will check**. Validators check
  shape; handlers check business invariants (e.g. uniqueness, state
  transitions).

---

## 8. Error handling

### Exception types

| Exception | HTTP | Use case |
|---|---|---|
| `ValidationException` (Application.Common.Exceptions) | 400 | FluentValidation failure |
| `AuthenticationException` | 400 | Login/credential/token issue with a `Code` |
| `NotFoundException` | 404 | Targeted resource doesn't exist |
| `ConflictException` | 409 | Uniqueness or state violation |
| `UnauthorizedAccessException` | 401 | Generic auth failure |
| (anything else) | 500 | Unhandled — gets logged with stack trace |

### Adding a new exception type

1. Create the class in
   `Application/Common/Exceptions/<Name>Exception.cs`. Carry a `string Code`
   property like the existing ones.
2. Add a `case` in `ExceptionHandlingFilter.cs` mapping to the right HTTP
   code + envelope.
3. Throw from handlers with a stable, snake_case code:
   `throw new NotFoundException("role_not_found", "Role 42 not found.");`
4. Document the code in `FE_CHANGES_REQUIRED.md` so the FE engineer can
   wire UX for it.

**Don't** swallow exceptions in handlers or controllers. The filter is the
ONE place that translates exceptions to HTTP responses.

---

## 9. Auditing and idempotency

### Audit columns

Every entity that persists business data has:

```
Created_By           (NOT NULL, set on insert)
Created_Date         (NOT NULL, set on insert, UTC)
Last_Updated_By      (nullable, set on every update)
Last_Updated_Date    (nullable, set on every update, UTC)
```

In handlers:

```csharp
var actor = _current.UserName ?? _current.UserId ?? "unknown";
var now   = DateTime.UtcNow;
entity.LastUpdatedBy   = actor;
entity.LastUpdatedDate = now;
```

Some legacy tables have `Last_Updated_*` as NOT NULL (`TmX_Privilege`).
Match the DB constraint — populate on every INSERT in addition to UPDATE.

### Idempotency

- **GET, PUT, DELETE** must be idempotent. Calling them N times must
  produce the same final server state as calling them once.
- **POST** is non-idempotent by default. For our verb-style sub-paths
  (`/Activate`, `/Deactivate`), make them idempotent by checking current
  state and returning 200 (not 409) if already in the requested state.
- **Bulk-replace endpoints** (e.g. PUT privilege matrix) MUST be
  idempotent — sending the same set twice is a no-op.

### Append-only audit logs

For events that need to be replayed or reviewed (login, role grant
changes, password resets):

- Insert into a dedicated `*_Audit` table — never UPDATE.
- See `TmX_Login_Audit` and `LoginAuditService` for the pattern.
- Never join from audit log to the current state — audit is a record of
  what happened, not a view of what is.

---

## 10. Performance

### Reads

- **`AsNoTracking()` on every read query**. EF change-tracking is dead
  weight on read paths; we explicitly opt in only on the rare update
  flow.
- **Project to DTOs in the query**, not after. EF Core 8 generates
  narrower SQL when projection is part of the LINQ.
- **Per-request caching** for expensive lookups. See
  `RequiresPrivilegeAttribute` for the pattern (HashSet stored in
  `HttpContext.Items`).
- **Index hot paths** in EF configurations:

  ```csharp
  b.HasIndex(x => new { x.RoleId, x.PrivilegeId });
  ```

### Writes

- **Single `SaveChangesAsync` per handler** when possible. Multiple writes
  in the same scope = single DB transaction by default.
- **Don't load to update**: when you can express the change in a single
  UPDATE, prefer EF's `ExecuteUpdateAsync` for hot bulk paths. (Today we
  load + modify + save for clarity; revisit if hot paths show up.)

### Pagination is required

No list endpoint may return unbounded results. Always paginate, even when
the dataset is small today — defensive against unexpected growth.

---

## 11. Security

### Always

- **HTTPS in prod**. Configured via `RequireHttpsMetadata = true` in
  prod-environment Program.cs (TODO: today it's hardcoded false in the
  dev-only block).
- **JWT signing key** at least 32 bytes. Rotated via configuration.
- **Parameterised queries everywhere**. No string concatenation into SQL.
  EF Core does this by default; manual `FromSqlRaw` calls must use
  parameter placeholders.
- **Validate all input** at the validator layer. Trust nothing from the
  wire.
- **Rate limit**: TODO (not implemented today; tracked in BACKLOG).
- **Cookies are not used**. Bearer in header only.
- **Refresh-token rotation with reuse detection**. Already implemented
  in `RefreshTokenStore`.

### Per-action

- **Don't echo secrets** in responses (passwords, JWT keys, hashed values).
- **Don't log secrets** in Serilog output.
- **Sanitize error messages** in production — `internal_error` for the
  generic 500 path, never the exception's actual message.

---

## 12. Documentation

### Code-level

- **XML doc comment on every controller action** describing the URL,
  privilege, and a 1-line summary of what it does.
- **XML doc on every handler class** explaining the use case and any
  non-obvious decisions (e.g. why we ignore `Active_Flag`).
- **DTO classes have field-level XML docs** for non-obvious fields.

### API-level

- **Swagger** at `/swagger` in Development. Adds Bearer security
  definition automatically.
- **`FE_CHANGES_REQUIRED.md`** is the single source of truth for FE
  contract changes. Backend NEVER edits the FE repo.
- **`BACKLOG.md`** for "we'll do this later" — never lose a deferred
  decision in chat.

### Slice-level

When a slice ships, the README's "Endpoints in this slice" section
should reflect what was delivered. The FE_CHANGES_REQUIRED entry
mirrors what's documented for clients.

---

## 13. RBAC seed file discipline

Single rule: every new endpoint with `[RequiresPrivilege("X")]` requires
two appends to `database/seed/rbac_grants.sql`:

1. Section 1: privilege definition `(N'X', N'description')`.
2. Section 2: IT Admin grant `(N'X')`.

Re-run the file on each environment. Idempotent. See
`database/seed/rbac_grants.sql` for the full convention.

---

## 14. FE↔backend alignment discipline

Every backend slice that diverges from a current FE call site logs an
audit-table entry in `FE_CHANGES_REQUIRED.md`:

| FE call (current) | Backend | Action |
|---|---|---|

Three columns. The FE engineer scans this table to know what changed.
Going-forward divergences are tracked in the same format — never use
prose where a row works.

## 14a. Module-boundary discipline (forward-compat with future modularisation)

The product is **planned to become a licensed modular monolith** post-revamp
(see `docs/MODULARISATION_PLAN.md`). Every slice today should respect future
module boundaries so the eventual refactor is mechanical, not surgical.

When adding code, ask: "if this slice were extracted into its own
project tomorrow, would this work?" If the answer is no, the design
needs a second look. Concrete rules:

1. **No cross-slice handler imports.** A handler in
   `Application/Features/Transaction/...` MUST NOT
   `using FSI.Trade.Compliance.Application.Features.Auth.Login`.
   Cross-slice data access goes through Application contracts
   (`IRoleQueryService`, `IKycScreeningService`, etc.), never through
   another slice's handlers.

2. **Domain entities don't navigation-property across slices.** A
   `Transaction` entity does not own a navigation property to
   `Customer`. Use the customer's ID and call
   `ICustomerMasterService` when you need the data.

3. **EF configurations are slice-tagged.** Every `*Configuration.cs`
   file's class XML doc should mention which slice owns it. Today they
   live in one folder; post-modularisation, they move to their owning
   module.

4. **`InfrastructureServiceRegistration` blocks are slice-segmented.**
   Each `// ---------- Slice X: ... ----------` block is the unit
   that lifts cleanly into a module's `RegisterServices(...)` impl
   later. Don't intermix slice registrations.

5. **`IApplicationDbContext` is shared today, fragmented later.** Don't
   add new domain-specific DbSets to it casually — when modularisation
   ships, each module brings its own `IModuleDbContext`. The shared
   one should shrink to core entities only (User, Role, Privilege,
   RefreshToken, etc.).

If a code review spots a violation, fix at review time. Cost during
the slice: ~15 minutes. Cost during the modularisation refactor: ~2
hours per case.

---

## 15. Checklist for adding a new endpoint

Copy this into your PR description:

```
## New endpoint: <METHOD> <URL>

- [ ] Route uses /api/v{version:apiVersion}/[controller]/...
- [ ] Controller has [ApiController], [ApiVersion("1.0")], [Authorize]
      (or [AllowAnonymous] with one-line justification)
- [ ] Action has [RequiresPrivilege("Module.Action")] if protected
- [ ] CQRS command/query in Application/Features/<Module>/<Action>/
- [ ] FluentValidation validator in same folder
- [ ] Returns standard ResponseViewModel<T>.Ok(...) envelope
- [ ] Field naming: camelCase
- [ ] Dates: ISO 8601 UTC
- [ ] List endpoints: extends PagedQuery, returns PagedResult<T>
- [ ] List endpoints: ApplySort whitelist, filter handled
- [ ] All reads: .AsNoTracking()
- [ ] All writes: audit columns populated (CreatedBy/Date,
      LastUpdatedBy/Date)
- [ ] Idempotent verbs (GET/PUT/DELETE) are actually idempotent
- [ ] XML doc on the action — URL, privilege, 1-line summary
- [ ] FE_CHANGES_REQUIRED.md updated with any FE-visible delta
- [ ] database/seed/rbac_grants.sql appended (privilege row +
      IT Admin grant) IF [RequiresPrivilege] was added
- [ ] Errors: throw NotFoundException / ConflictException /
      ValidationException — never return raw 4xx from the action
- [ ] No raw SQL — LINQ via EF Core only
- [ ] No secrets in responses or logs
- [ ] Swagger renders the endpoint correctly (visit /swagger)
```

---

## Appendix A — Where things live

| Concern | Project | Folder |
|---|---|---|
| Domain entities | `FSI.Trade.Compliance.Domain` | `Entities/` |
| Application contracts (interfaces) | `FSI.Trade.Compliance.Application` | `Contracts/Identity/`, `Contracts/Persistence/` |
| Application options (config POCOs) | `FSI.Trade.Compliance.Application` | `Common/Options/` |
| Application models (DTOs, envelopes) | `FSI.Trade.Compliance.Application` | `Common/Models/` |
| Application exceptions | `FSI.Trade.Compliance.Application` | `Common/Exceptions/` |
| Application features (CQRS) | `FSI.Trade.Compliance.Application` | `Features/<Module>/<Action>/` |
| EF configuration | `FSI.Trade.Compliance.Infrastructure` | `Persistence/Configurations/` |
| EF DbContext | `FSI.Trade.Compliance.Infrastructure` | `Persistence/ApplicationDbContext.cs` |
| Infrastructure services (impl) | `FSI.Trade.Compliance.Infrastructure` | `Identity/`, `Persistence/Repositories/`, `Services/` |
| Controllers | `FSI.Trade.Compliance.API` | `Controllers/` |
| Action filters | `FSI.Trade.Compliance.API` | `Filters/` |
| Middlewares | `FSI.Trade.Compliance.API` | `Authentication/` |
| Migrations | `database/migrations/` | timestamped, write-once |
| Seed files | `database/seed/` | living, re-runnable |

---

## Appendix B — Glossary

- **Slice**: a self-contained deliverable batch. Slice 1 was auth, slice
  2 is user/RBAC.
- **`{ status, data }` envelope**: every response wraps in this.
- **Bootstrap escape hatch**: `Auth.BootstrapAdminRoles` short-circuits
  privilege checks for named roles. Temporary.
- **Pattern A Identity**: ASP.NET Identity with a custom `IUserStore`,
  no `AspNet*` tables. Reads/writes `TmX_User` directly.
- **Active_Flag is currently ignored**: per FSI direction (May 2026),
  the new backend doesn't filter `Active_Flag = 1` on RBAC reads. Every
  row is treated as live until lifecycle semantics are formalised.
  Tracked in BACKLOG.

---

*Last updated: 2026-05-05 (Slice 2.2 — versioning + alignment audit).*

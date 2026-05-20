# FSI.Trade.Compliance — Auth Slice

New .NET 8 backend for ICBC trade-compliance. Follows the FSI.Trade.Application
boilerplate layout. Contains only the auth slice for now (5 endpoints). The
legacy projects under `D:\ICBC - Latest\` are reference and remain untouched.

## Layout

```
FSI.Trade.Compliance/
├── FSI.Trade.Compliance.sln          (created by bootstrap.ps1)
├── bootstrap.ps1
└── Services/
    ├── FSI.Trade.Compliance.Domain/
    │   ├── Entities/                 ApplicationUser, RefreshToken
    │   └── Enums/                    UserStatus
    ├── FSI.Trade.Compliance.Application/
    │   ├── ApplicationServiceRegistration.cs
    │   ├── Common/                   Behaviours, Exceptions, Models
    │   ├── Contracts/                Identity, Persistence interfaces
    │   └── Features/Auth/            Login, Refresh, Logout, ChangePassword (CQRS)
    ├── FSI.Trade.Compliance.Infrastructure/
    │   ├── InfrastructureServiceRegistration.cs
    │   ├── Identity/                 TmxUserStore, JwtTokenService, RefreshTokenStore, IdentityService, *Options
    │   ├── Persistence/              ApplicationDbContext, Configurations
    │   └── Services/                 CurrentUserService
    └── FSI.Trade.Compliance.API/
        ├── Program.cs
        ├── appsettings.json / appsettings.Development.json
        ├── Authentication/           CustomAuthorizationMiddleware
        ├── Filters/                  ExceptionHandlingFilter
        └── Controllers/              HealthController, UserController, AuthController
```

## One-time scaffold

DB prerequisites — these must already be done:

1. `2026_05_001_MergeAspNetUsersIntoTmxUser.sql`  (committed)
2. `2026_05_002_CreateRefreshTokens.sql`           (committed)
3. `2026_05_003_MovePasswordChangeAuditTrail.sql`  (committed; can run in source-missing mode)

Then:

```powershell
cd "D:\ICBC - Latest\FSI.Trade.Compliance"
powershell -File .\bootstrap.ps1
```

This creates the .sln, the four projects with the right `dotnet new` templates,
adds project references, and pulls all NuGet packages. The .cs / appsettings
files in this repo replace the template stubs.

## Configuration to edit before running

`Services/FSI.Trade.Compliance.API/appsettings.json`:

- `ConnectionStrings:DefaultConnection` — point at your `ICBC_DEMO` SQL Server.
- `Jwt:Key` — replace the placeholder with at least 32 random bytes (base64 or
  hex). One-liner: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`
- `Jwt:RoleTtlMinutes` — confirm role names and TTLs match the legacy
  `AppSettings.config` `TokenExpiryInMinutes` values.

## Run

```powershell
cd "D:\ICBC - Latest\FSI.Trade.Compliance\Services\FSI.Trade.Compliance.API"
dotnet run
```

Swagger opens at `https://localhost:<port>/swagger`.

## Verify

| Endpoint                                                  | Expected |
| --------------------------------------------------------- | -------- |
| `GET  /api/v1/Health`                                     | 200, `{ data: { dbReachable: true, userCount: N } }` |
| `POST /api/v1/Auth/Login` (form-urlencoded)               | 200 with `access_token` + `refresh_token`; legacy hashes verify (V2 → silent V3 upgrade) |
| `POST /api/v1/Auth/Refresh` (json `{ refresh_token }`)    | 200 with new pair; old refresh token is now revoked |
| `POST /api/v1/Auth/Logout` (Bearer)                       | 200, refresh token(s) revoked |
| `POST /api/v1/Auth/ChangePassword` (Bearer + body)        | 200, hash updated, audit row written, all refresh tokens revoked |

## Endpoints in this slice

```
GET  /api/v1/Health
POST /api/v1/Auth/Login              (form-urlencoded; OAuth-shape body) -- the token endpoint
POST /api/v1/Auth/Refresh            (json)
POST /api/v1/Auth/Logout             (Bearer)
POST /api/v1/Auth/ChangePassword     (Bearer)
```

All four auth-lifecycle operations live on `AuthController` (single responsibility).
`UserController` is reserved for slice-2 user CRUD.

All response bodies are wrapped in the legacy `ResponseViewModel<T>` shape:

```json
{
  "status":  { "code": 200, "message": "OK", "description": null },
  "data":    { "...feature-specific payload..." },
  "access_token":  "<jwt-only on auth endpoints>",
  "refresh_token": "<opaque-only on auth endpoints>"
}
```

On failure (`AuthenticationException` / `ValidationException`) the same envelope
is returned with `status.code` 4xx and `data.Success = 0` (matches the FE's
`isExplicitValidationFailure` check).

## Conventions (apply to every controller / handler going forward)

> **Read [API_GUIDELINES.md](./API_GUIDELINES.md) before adding a new endpoint.** That document is the
> authoritative guide — URL design, status codes, envelope shape, paging, versioning, validation,
> auditing, performance, security, and a copy-pasteable PR checklist. The conventions below are
> the short version; the guidelines are the long version with examples and reasoning.


1. **Single envelope shape** — `{ status, data }`. No top-level fields outside that. Tokens, pagination, errors all live inside `data`.
2. **JSON casing** is camelCase (ASP.NET Core 8 default). Source-side use Pascal-case (`AccessToken`); serializer converts at runtime.
3. **`data.success === 0`** on **every** failure response — auth, validation, unauthorized, and generic. The `ExceptionHandlingFilter` enforces this; new error paths must follow.
4. **No `[Consumes]` attribute** unless a route truly needs to refuse a content-type. `[FromForm]` / `[FromBody]` alone is enough.
5. **Auth-lifecycle endpoints live on `AuthController`** (`Login`, `Refresh`, `Logout`, `ChangePassword`). User CRUD goes on `UserController` (slice 2+).
6. **Handlers orchestrate** (Application layer); **Infrastructure exposes generic primitives** via Application-defined ports (`IUserAuthenticationService`, `IRoleQueryService`, `IPasswordChangeAuditService`, `IJwtTokenService`, `IRefreshTokenStore`, `ITwoFactorVerifier`). No use-case-specialised methods on those ports.
7. **No raw SQL** in handlers or services. Map every entity in Infrastructure/Persistence/Configurations and query via LINQ.
8. **Configuration POCOs** (anything bound from `appsettings.json` and used by Application) live in `Application/Common/Options/`.
9. **Generic results from Infrastructure ports** (e.g. `(bool ok, IReadOnlyList<string> errors)`); the handler decides whether to throw or surface.
10. **FE-visible contract changes** are logged in `FE_CHANGES_REQUIRED.md` at the top, in reverse chronological order. Backend never edits files under `tmx-finance-frontend-revamp/`.
11. **`[RequiresPrivilege("X")]` on a new endpoint?** Append two blocks to `database/seed/rbac_grants.sql` — one in the privilege table (Section 1), one in the IT Admin grants table (Section 2). Re-run the file on every environment as part of deploying the endpoint. The file is idempotent and re-runnable forever; new entries always go alongside the existing ones, never replacing them.
12. **API versioning** — every controller declares `[ApiVersion("1.0")]` and uses `[Route("api/v{version:apiVersion}/[controller]")]`. The URL `/api/v1/...` is identical for clients (URL-segment is the canonical reader). Other readers — `X-API-Version` header and `?api-version` query string — are also configured for future flexibility. When introducing v2 of an endpoint, decorate the new action with `[MapToApiVersion("2.0")]` and update the controller's `[ApiVersion(...)]` attribute to list both. **Never** copy-paste a controller for a new version.
13. **FE↔backend endpoint divergences** — every time a backend slice ships endpoints that don't match a FE call site 1:1, document the delta in a `FE_CHANGES_REQUIRED.md` entry. The audit table (FE call vs Backend) is the canonical format.

## Database files — two conventions

| Folder | What goes here | Run frequency |
|---|---|---|
| `database/migrations/` | Schema-altering scripts (CREATE/ALTER TABLE, FKs, indexes). Timestamped: `YYYY_MM_NNN_*.sql`. | Once per file. Never modified after applying to prod. |
| `database/seed/` | Living, re-runnable, idempotent data baselines. Today: `rbac_grants.sql`. | Re-run on every deployment. Grows over time as new endpoints / roles / lookups are added. |

> **Looking for which tables we use vs which can be dropped?** [docs/DB_ENTITY_USAGE.md](./docs/DB_ENTITY_USAGE.md)
> catalogs every entity, the slice that introduced it, and the cleanup candidates for after stable prod deploy.

> **Modularisation roadmap**: [docs/MODULARISATION_PLAN.md](./docs/MODULARISATION_PLAN.md). Post-revamp,
> we'll refactor into a licensed modular monolith so banking customers can pick the modules they need
> (Compliance Lite → Enterprise tiers). Discipline points to maintain through the remaining slices are in §6
> of that doc and codified in `API_GUIDELINES.md` §14a.

## Out of scope this slice

User CRUD, admin reset-password, forgot-password (email link), 2FA enrolment,
concurrent-login enforcement, claims for privileges, and every non-auth feature
(Lookup, Configurations, LoanApplication, etc.). All scheduled for later slices.

## Cleanup

The earlier scaffold at `D:\ICBC - Latest\src\ICBC.Backend.*` is orphaned. Safe
to delete:

```powershell
Remove-Item -Recurse -Force "D:\ICBC - Latest\src"
```

Nothing in `FSI.Trade.Compliance` references it.

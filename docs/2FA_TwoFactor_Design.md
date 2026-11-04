# Two-Factor Authentication (TOTP + Email) — End-to-End Design

**Scope**: system-wide, admin-configured second factor enforced at login for the
ICBC Trade Compliance system. One method is active at a time — **TOTP** (authenticator
app, with forced first-time setup) or **Email** (one-time code mailed to the user).
This document defines the complete flow: configuration, login enforcement, OTP
generation/delivery, verification, error handling, and access control.

Status: BE + FE implemented, **deployed and validated end-to-end (2026-09-28)**. Migration
`2026_08_063_TwoFactorEmailOtp.sql` applied (OTP table + Email-OTP config rows verified);
BE rebuilt (`CI_BUILD_OK`) and running. The migration required `SET QUOTED_IDENTIFIER ON`
(the `TmX_Configuration_*` tables carry filtered/computed indexes).

Validation (against the live API on localhost, test admin):
- **TOTP**: reset user → login forced `totp_setup_required` with a valid `otpauth://…SHA1`
  provisioning URI + secret; a computed TOTP confirmed setup (token issued, `TwoFactorEnabled`
  set); enrolled login → `otp_required` (no secret re-exposed); correct code → token; wrong
  code → `invalid_otp`. No token is issued until verification — access control confirmed.
- **Email**: switching `TwoFactor.Method` to `Email` via `PUT /Configuration/{key}` hot-reloads
  live (the `IConfigurationRefresher` path; a raw SQL value change alone does NOT reload).
  Send creates a hashed row (`SHA-256`, ~5-min expiry, `attempts=0/5`); a successful verify
  issues a token and marks the row consumed (replay → `otp_required`); wrong code →
  `invalid_otp` + attempt increment; past-expiry → `otp_expired`; attempts at ceiling →
  `otp_locked`; resend resets the row.

**Operational note**: Email OTP delivery reuses the SMTP config, which is currently **not
enabled**, so a live Email login fails closed with `email_otp_unavailable`. To use the Email
method, enable SMTP (`Email.Enabled` + Host/From/credentials) in Configuration Management.
(Minor: `SendAsync` persists the OTP row before attempting delivery, so a row exists even when
delivery fails — harmless; the next send upserts it.)

---

## 1. Design decisions

Three decisions shaped the implementation:

1. **Single method, admin-chosen.** The admin turns 2FA on (`TwoFactor.Enabled`) and
   picks exactly one method system-wide (`TwoFactor.Method` = `Totp` or `Email`), using
   the existing metadata-driven Configuration Management. There is no per-user opt-out for
   the enforced factor.
2. **Inline on the login screen.** The QR setup (TOTP) and the code entry (both methods)
   happen on the login page itself. No restricted/interim token is issued — the login
   endpoint simply withholds the access token until the factor is satisfied. Because route
   protection is token-presence based, the app is inherently inaccessible until then; no
   new router gate was needed.
3. **Hashed OTP with expiry + attempts.** The Email OTP is never stored in plaintext. A
   single current row per user holds the SHA-256 hash, an expiry, and an attempt counter,
   validated server-side in constant time.

---

## 2. Configuration

All keys live in Configuration Management (metadata-driven; hot-reloaded into
`IOptionsMonitor` via the DB→`IConfiguration` bridge, dot-notation key → colon section).

| Key | Type | Default | Meaning |
|---|---|---|---|
| `TwoFactor.Enabled` | Boolean | `true` | Master on/off for the login-time 2FA gate. |
| `TwoFactor.Method` | Enum (`Email`/`SMS`/`Totp`/`None`) | `Totp` | Active factor. `SMS` is reserved but not implemented (treated as skip). |
| `TwoFactor.Email.OtpLength` | Integer (4–8) | `6` | Digits in the emailed code. |
| `TwoFactor.Email.OtpExpiryMinutes` | Integer (1–30) | `1` | Validity window (default 1 min). |
| `TwoFactor.Email.MaxAttempts` | Integer (1–10) | `5` | Wrong-code attempts before the code locks. |

`TwoFactor.Enabled` / `TwoFactor.Method` were seeded by `2026_07_036`; the three
`TwoFactor.Email.*` tuning rows are seeded by `2026_08_063`. The service clamps each tuning
value to its safe range at point of use, so a bad config can't produce a 2-digit or
never-expiring code.

**Email source.** Email OTP delivery reuses the existing SMTP configuration
(`Email.Host`, `Email.Port`, `Email.From`, `Email.Username`, `Email.UseSsl`) and the
encrypted `Email.Password` secret (read via `ISecretConfigReader`, never bridged into
`IConfiguration`). There is no separate mail account for OTPs — the operator configures SMTP
once in Configuration Management. If SMTP is not enabled/configured, an Email-OTP login fails
closed with a clear message (see §6).

---

## 3. Data model

`dbo.TmX_User_Otp` — one current OTP per `(User_Id, Purpose)` (Purpose = `Login`), enforced by
a unique index. `send` upserts this row.

| Column | Notes |
|---|---|
| `Otp_Id` | BIGINT identity PK |
| `User_Id` | FK to `TmX_User.Id` (GUID string) |
| `Purpose` | default `Login` |
| `Otp_Hash` | SHA-256 hex of the code — never the plaintext |
| `Expires_At` | UTC expiry |
| `Attempts` / `Max_Attempts` | wrong-code counter + ceiling |
| `Sent_At` / `Consumed_At` | issued / spent timestamps |
| `Created_Date` / `Last_Updated_Date` | audit |

TOTP needs no new table: the shared secret lives on the user
(`TmX_User.TwoFASecretKey` → `ApplicationUser.TwoFactorAuthenticatorKey`), and
`TwoFactorEnabled` flips true once enrolment is confirmed.

---

## 4. Login enforcement flow

`POST /api/v1/Auth/Login` runs its existing gates (credentials, active, lockout, password
policy). Then, when `TwoFactor.Enabled`, `LoginCommandHandler` calls
`ITwoFactorLoginEnforcer.EnforceAsync(user, otp)` **before** any token is issued:

- returns `null` → the factor is satisfied; the handler proceeds to device binding + token.
- returns an `AuthResponse` **challenge** (`Success = 0`, with a `Code`) → returned verbatim
  as **HTTP 200**; no token. The FE shows the second-factor screen.
- throws `AuthenticationException` → a hard verify failure; surfaces as **HTTP 400** with
  `data.code` (the existing exception path). The FE keeps the screen up and shows the reason.

The enforcer branches on `TwoFactor.Method`:

### TOTP (forced setup + verify)
- **Not enrolled, no code** → generate a pending secret (persisted; `TwoFactorEnabled` stays
  false), build the `otpauth://` provisioning URI, and challenge
  `totp_setup_required` + `provisioningUri` + `secret`.
- **Not enrolled, code supplied** → verify against the pending secret. On success set
  `TwoFactorEnabled = true` and return `null` (proceed). On failure throw `invalid_otp`.
- **Enrolled, no code** → challenge `otp_required` (method `Totp`).
- **Enrolled, code supplied** → verify current TOTP; `null` or throw `invalid_otp`.

### Email OTP
- **No code** → `IEmailOtpService.SendAsync` generates a code, upserts the hashed row (expiry,
  attempts reset), emails it, and the enforcer challenges `otp_required` + `maskedEmail`.
- **Code supplied** → `VerifyAsync` returns one of: `Success` (→ `null`, proceed),
  `Expired` (→ throw `otp_expired`), `TooManyAttempts` (→ throw `otp_locked`),
  `Missing` (→ throw `otp_required`), `Invalid` (→ throw `invalid_otp`).

`None` (or the unimplemented `SMS`) → the gate is skipped (fail-open on method
misconfiguration, so a bad `Method` value can't lock every user out). The master
`TwoFactor.Enabled=false` skips 2FA entirely.

Every challenge and failure is written to the login audit trail (`OtpRequired` /
`OtpInvalid` with the code as detail).

---

## 5. OTP generation, delivery & verification (Email)

- **Generation**: uniform N-digit numeric code via `RandomNumberGenerator` with rejection
  sampling (byte values ≥ 250 discarded) so digits 0–9 are evenly distributed; leading zeros
  allowed. Length from config, clamped 4–8.
- **Validity + single-use**: codes expire after `OtpExpiryMinutes` (default **1 minute**) and
  are strictly **one-time**: a successful verify stamps `Consumed_At`, so the same code cannot
  be reused (a replay is rejected). Verified live end-to-end with a real SMTP send.
- **Storage**: only `SHA-256(code)` is stored, with `Expires_At = now + expiry`,
  `Attempts = 0`, `Max_Attempts` from config. Re-sending upserts the same row (fresh code,
  reset counter), so only the latest code is ever valid.
- **Delivery**: a direct `SmtpClient` send using the SMTP config + encrypted password.
  Unlike the best-effort notification sender, OTP delivery **throws on failure**
  (`email_otp_unavailable` when SMTP isn't configured, `email_otp_send_failed` on a send
  error) so the user is told rather than silently stranded.
- **Verification**: constant-time hash compare (`CryptographicOperations.FixedTimeEquals`).
  Expiry and attempt ceiling checked first; a wrong code increments `Attempts` (and locks at
  the ceiling); a correct code stamps `Consumed_At` so it can't be replayed.

TOTP verification is the existing `ITwoFactorVerifier` (Otp.NET), unchanged.

---

## 6. Error handling & codes

| Code | HTTP | Meaning | FE behaviour |
|---|---|---|---|
| `totp_setup_required` | 200 (`success=0`) | Forced TOTP enrolment | Show setup screen (QR + secret) |
| `otp_required` | 200 (`success=0`) | Enter a code (enrolled TOTP, or Email code sent) | Show verify screen (+ masked email for Email) |
| `invalid_otp` | 400 | Wrong code | Keep screen, inline error, clear the boxes |
| `otp_expired` | 400 | Email code past expiry | Inline error; prompt Resend |
| `otp_locked` | 400 | Attempt ceiling reached | Inline error; prompt Resend |
| `email_otp_unavailable` | 400 | SMTP not configured | Inline error (contact admin) |
| `email_otp_send_failed` | 400 | Delivery failed | Inline error (retry / contact admin) |
| `email_otp_no_address` | 400 | No email on file | Inline error (contact admin) |

The FE maps codes to friendly copy in `LOGIN_FAILURE_COPY`; unmapped codes fall back to the
server `message`.

---

## 7. Access control

No access token is issued until the factor is satisfied, and route protection is
token-presence based (`ProtectedRoute`). Therefore, during a challenge the user holds no
token and cannot reach any application route — the app is blocked without any new gate. A
forced-TOTP user literally cannot get a token until they have scanned the QR and entered a
valid code, satisfying the "no screen access until setup + verification complete" requirement.

---

## 8. Frontend

The login page swaps the sign-in form for `TwoFactorScreen` whenever a `challenge` is present.
The screen has three modes:

- **setup** (`totp_setup_required`): renders the provisioning URI as a QR (`qrcode.react`
  `QRCodeSVG`) plus the grouped secret for manual entry, then the confirming code input.
- **totp** (`otp_required`, method Totp): the enrolled authenticator-code entry.
- **email** (`otp_required`, method Email): shows the masked address and a **Resend** button
  (which re-runs login with no code, prompting a fresh mailed OTP).

Resubmitting the login mutation with the entered `otp` completes verification. `success===0`
challenges drive the screen; `400` verify failures keep it up with an inline error. No
request-shape change — the code rides the existing `otp` field.

---

## 9. Files

**Backend**
- `database/migrations/2026_08_063_TwoFactorEmailOtp.sql` — OTP table + Email-OTP config seeds.
- `Domain/Entities/UserOtp.cs`, `Infrastructure/Persistence/Configurations/UserOtpConfiguration.cs`,
  `IApplicationDbContext` / `ApplicationDbContext` (`UserOtps` DbSet).
- `Application/Common/Options/TwoFactorOptions.cs` (`EmailOtpOptions`).
- `Application/Contracts/Identity/IEmailOtpService.cs`,
  `Infrastructure/Identity/EmailOtpService.cs`.
- `Application/Common/Identity/ITwoFactorLoginEnforcer.cs` + `TwoFactorLoginEnforcer.cs`.
- `Application/Common/Models/AuthResponse.cs` (challenge fields).
- `Application/Features/Auth/Login/LoginCommandHandler.cs` (enforcer call).
- `Infrastructure/InfrastructureServiceRegistration.cs` (DI).

**Frontend** (`tmx-finance-frontend-revamp`)
- `src/features/auth/api/auth-api.ts`, `src/features/auth/pages/TwoFactorScreen.tsx`,
  `src/features/auth/pages/login-page.tsx`, `src/index.css`.

**Deploy**: `run_063_rebuild.ps1` (migration + verify + `ci_rebuild.ps1`).

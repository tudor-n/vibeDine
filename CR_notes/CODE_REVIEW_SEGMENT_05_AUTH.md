# Segment 05 — Authentication & token lifecycle: review findings

Scope review against the [Segment 05 checklist](CODE_REVIEW_ROADMAP.md#segment-05--authentication--token-lifecycle) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). This segment covers sign-up, sign-in, refresh, email confirmation, and invitation acceptance — the entire identity surface.

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**9 findings.** Two high-severity defects ([F1](#f1--high--refresh-tokens-are-stored-in-plaintext-), [F2](#f2--high--confirm-email-mints-a-fresh-token-pair-for-any-already-confirmed-user-without-validating-the-supplied-token)) directly contradict 🔥 checklist items and expose either every active session on a DB leak ([F1](#f1--high--refresh-tokens-are-stored-in-plaintext-)) or persistent post-mailbox-compromise account takeover ([F2](#f2--high--confirm-email-mints-a-fresh-token-pair-for-any-already-confirmed-user-without-validating-the-supplied-token)). Three medium-severity gaps remove standard defensive controls (no brute-force protection [F3](#f3--medium--no-brute-force-protection-on-login), no reuse-detection chain [F4](#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user), no logout [F5](#f5--medium--no-logout-endpoint--refresh-tokens-cannot-be-invalidated-on-demand)). The remaining four are lower-severity hygiene issues ([F6](#f6--low--registration-leaks-whether-a-confirmed-account-exists), [F7](#f7--low--default-jwt-signing-key-is-committed-in-appsettingsjson), [F8](#f8--low--refresh-rotation-is-not-atomic-with-new-token-issuance), [F9](#f9--low--no-password-reset-flow)).

What does check out: JWT key length is validated at startup, signing parameters are all on, `ClockSkew` is 1 minute, JWT claims do not leak server-only state, refresh tokens cascade-delete with their user, password length is enforced server-side, the SignalR `access_token` query-string extraction is scoped to `/hubs/*`, and the unconfirmed-account branch of registration correctly avoids existence disclosure.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 Refresh tokens stored hashed | ❌ Fail — see [F1](#f1--high--refresh-tokens-are-stored-in-plaintext-) | DB column is `text`, indexed by raw value. |
| Refresh tokens are single-use (rotated on refresh) | ✓ Pass | [AuthController.cs:162-166](backend/RestaurantApi/Controllers/AuthController.cs#L162-L166) revokes the used token and issues a fresh pair. |
| Reuse of a revoked token triggers revoke-all-for-user | ❌ Fail — see [F4](#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user) | Reuse returns 401 with no defensive action. |
| Logout revokes the refresh token | ❌ Fail — see [F5](#f5--medium--no-logout-endpoint--refresh-tokens-cannot-be-invalidated-on-demand) | No logout endpoint exists. |
| Password reset tokens short-lived & single-use | ❌ N/A — see [F9](#f9--low--no-password-reset-flow) | No password reset endpoint exists. |
| Email-confirmation tokens single-use & gate login | ⚠ Partial — see [F2](#f2--high--confirm-email-mints-a-fresh-token-pair-for-any-already-confirmed-user-without-validating-the-supplied-token) | Login is correctly gated on `EmailConfirmed`, but the confirm-email endpoint hands out tokens to already-confirmed users without validating the supplied token. |
| Identity uses ASP.NET defaults (PBKDF2) | ✓ Pass | `AddIdentityCore<AppUser>` + `AddEntityFrameworkStores`; no custom hasher. |
| Sign-up does not leak whether an email exists | ⚠ Partial — see [F6](#f6--low--registration-leaks-whether-a-confirmed-account-exists) | Unconfirmed branch is correctly disguised; confirmed branch returns 409. |
| Brute-force protection (lockout) | ❌ Fail — see [F3](#f3--medium--no-brute-force-protection-on-login) | Identity lockout columns exist but no policy is configured and `UserManager.CheckPasswordAsync` does not increment `AccessFailedCount`. |
| JWT claims include `sub`, `jti`; no server-only state | ✓ Pass | [JwtTokenService.cs:21-28](backend/RestaurantApi/Auth/JwtTokenService.cs#L21-L28). No `password_hash`, no `email_confirmed`. |
| `AccessTokenMinutes ≤ 60` | ✓ Pass | 60 in both `appsettings.json` and compose. |
| `RefreshTokenDays = 30` | ✓ Pass per checklist (acceptable). |
| Password complexity validated server-side | ✓ Pass | `RequiredLength = 8`; defaults for digit/lower/upper still in effect. |

---

## F1 — HIGH — Refresh tokens are stored in plaintext 🔥

The roadmap is explicit (Segment 05, 🔥 checklist item):
> Refresh tokens are stored **hashed** in DB (not plaintext). If plaintext, every DB leak is a session takeover.

The schema stores the raw token string and indexes it uniquely for lookup:

[backend/RestaurantApi/Models/RefreshToken.cs:11](backend/RestaurantApi/Models/RefreshToken.cs#L11)

```csharp
public string Token { get; set; } = string.Empty;
```

[backend/RestaurantApi/Data/AppDbContext.cs:260-271](backend/RestaurantApi/Data/AppDbContext.cs#L260-L271)

```csharp
modelBuilder.Entity<RefreshToken>(e =>
{
    e.ToTable("refresh_tokens");
    ...
    e.Property(t => t.Token).HasColumnName("token");
    ...
    e.HasIndex(t => t.Token).IsUnique();
});
```

And the migration writes the column as `text` (the column type stored in the model snapshot for `RefreshToken.Token` is `text` — no hash conversion).

The lookup at refresh time uses the raw value:

[backend/RestaurantApi/Controllers/AuthController.cs:152-153](backend/RestaurantApi/Controllers/AuthController.cs#L152-L153)

```csharp
var stored = await _db.RefreshTokens
    .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);
```

The token itself is 48 bytes of `RandomNumberGenerator` output, base64-encoded ([JwtTokenService.cs:46](backend/RestaurantApi/Auth/JwtTokenService.cs#L46)) — entropy is fine; storage is the problem.

**Impact.** Any read access to `refresh_tokens` — a leaked backup, an unprotected replica, a SQL injection elsewhere in the API, an over-permissioned analytics user, a developer dump pulled to a laptop — yields the bearer credentials for every active session at that point in time. Tokens are valid for 30 days and rotate only on use, so a quiet attacker can mint new pairs and stay resident until each victim logs in again (which itself does not revoke the stolen token — there is no logout, see [F5](#f5--medium--no-logout-endpoint--refresh-tokens-cannot-be-invalidated-on-demand)).

**Recommended fix.** Store `SHA-256(token)` in the indexed `Token` column (rename to `TokenHash` to make intent obvious), return the raw token to the client only once at issuance, and look up by hashing the inbound value:

```csharp
public string TokenHash { get; set; } = string.Empty;   // 64 hex chars from SHA-256
...
var inboundHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RefreshToken)));
var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == inboundHash);
```

Migration is mandatory and one-way: existing tokens will need to be invalidated (acceptable — users re-login). Pair with [F4](#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user) so a leaked hash can't be silently exploited if the DB is breached after the change.

---

## F2 — HIGH — `confirm-email` mints a fresh token pair for any already-confirmed user without validating the supplied token

[backend/RestaurantApi/Controllers/AuthController.cs:87-116](backend/RestaurantApi/Controllers/AuthController.cs#L87-L116)

```csharp
[HttpPost("confirm-email")]
public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
{
    var user = await _users.FindByIdAsync(request.UserId.ToString());
    if (user == null)
        return BadRequest(new { message = "Link de confirmare invalid." });

    if (user.EmailConfirmed)
    {
        // Idempotent: allow the user to log in via the same flow.
        return Ok(await BuildAuthResponseAsync(user));
    }
    ...
}
```

The idempotent branch returns `BuildAuthResponseAsync(user)` — which issues a fresh access token and persists a new refresh token — without inspecting `request.Token` at all. The only inputs that matter are: (a) a `UserId` that resolves to an existing user, (b) any non-empty string in the `Token` field to satisfy `[Required]`.

**Attack model.** UserId is a `Guid` (~122 bits) and not brute-forceable, but it is **not a secret** in this system. It travels through:

- The confirmation email URL: `…/confirm-email?userId={Guid}&token={…}` ([AuthController.cs:230](backend/RestaurantApi/Controllers/AuthController.cs#L230)).
- Anywhere that URL is captured: the SMTP server (in dev: Mailpit; in prod: whoever runs outbound mail), the recipient's mailbox (forwards, IT scanning, mailbox compromise), the recipient's browser history, the admin SPA's `Referer` header, server access logs.

Once the UserId is observable to any party other than the legitimate user, that party can:

1. POST `{ userId, token: "anything" }` to `/api/auth/confirm-email` after the legitimate user has confirmed.
2. Receive a valid 60-minute access token and a 30-day refresh token.
3. Repeat indefinitely — no password is ever checked, no rate limit applies (see [F3](#f3--medium--no-brute-force-protection-on-login)), no logout invalidates the chain ([F5](#f5--medium--no-logout-endpoint--refresh-tokens-cannot-be-invalidated-on-demand)), and a password change does not invalidate refresh tokens either.

The check at [AuthController.cs:155](backend/RestaurantApi/Controllers/AuthController.cs#L155) (`!stored.IsActive`) only catches expiry/revocation of an *existing* token — it does not block fresh issuance through this endpoint.

The original intent — "let the user reload the confirmation link without an error" — is fine; the implementation conflates "you already confirmed" with "I trust you enough to issue session tokens." Those are different statements.

**Recommended fix.** In the already-confirmed branch, return a benign instruction to log in normally instead of issuing tokens:

```csharp
if (user.EmailConfirmed)
{
    return Ok(new { status = "already_confirmed", message = "Contul este deja confirmat. Te poți autentifica." });
}
```

The frontend should redirect to `/login` on `already_confirmed`. If the team wants frictionless "click link → land logged in" for the *first* confirmation, that flow already works on the un-confirmed path; the idempotent path only matters for repeat clicks where requiring a password login is acceptable.

---

## F3 — MEDIUM — No brute-force protection on login

Roadmap checklist item:
> Brute-force protection: lockout after N failed attempts, configurable in Identity options.

Two conditions need to hold and neither does:

1. **Lockout policy is not configured.** [Program.cs:27-36](backend/RestaurantApi/Program.cs#L27-L36) sets `RequireUniqueEmail`, `RequiredLength`, and `RequireNonAlphanumeric`, but nothing under `options.Lockout`. The Identity defaults (5 attempts → 5-minute lockout) would be in effect *if* failed attempts were being tracked.

2. **Failed attempts are never tracked.** Login uses `UserManager.CheckPasswordAsync` ([AuthController.cs:133](backend/RestaurantApi/Controllers/AuthController.cs#L133)) — that method validates the password but does **not** increment `AccessFailedCount`, **does not** consult `LockoutEnd`, and **does not** apply the lockout policy. Tracking requires `SignInManager.CheckPasswordSignInAsync` (with `lockoutOnFailure: true`), which the project does not register (`AddIdentityCore` deliberately omits `SignInManager`).

Compounding this, there is no rate limiting middleware: a `grep` for `AddRateLimiter`/`UseRateLimiter` across the backend returns no matches.

**Impact.** A network-adjacent attacker can issue unlimited password guesses against a known email. With weak passwords (the policy only requires 8 chars + digit/lower/upper) a targeted attack against an admin account is well within reach. Combined with [F2](#f2--high--confirm-email-mints-a-fresh-token-pair-for-any-already-confirmed-user-without-validating-the-supplied-token), the same lack of throttling means an attacker who has captured a UserId can also burn through refresh-token issuance at machine speed without being slowed down.

**Recommended fix.** Either (a) swap to `SignInManager` and configure `options.Lockout` (cleanest), or (b) add a per-email + per-IP rate limiter on `/api/auth/login` and `/api/auth/confirm-email` with explicit decay. Both is better.

---

## F4 — MEDIUM — Reuse of a revoked refresh token does not trigger revoke-all-for-user

Roadmap checklist item:
> Refresh token revocation: logout marks the token revoked; reuse of a revoked token triggers revoke-all-for-user.

[AuthController.cs:152-167](backend/RestaurantApi/Controllers/AuthController.cs#L152-L167) handles reuse only by returning 401:

```csharp
var stored = await _db.RefreshTokens
    .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);

if (stored == null || !stored.IsActive)
    return Unauthorized(new { message = "Invalid or expired refresh token." });
```

The `RefreshToken` entity also lacks a `ReplacedByToken`/chain field ([RefreshToken.cs:1-18](backend/RestaurantApi/Models/RefreshToken.cs#L1-L18)) — there is no way to walk from a stolen-and-replayed token back to its successor.

**Impact.** Refresh-token theft becomes silent. The standard mitigation is: if a token that has been rotated comes back, **somebody has the wrong copy** — either the legitimate client cached an old token, or an attacker is replaying a stolen one. Revoking the user's whole chain forces a re-login and contains the breach. Without it, an attacker who steals a token and rotates it once is now the only holder of the active session — the legitimate user just becomes a curiosity in the logs.

**Recommended fix.** Add `ReplacedByTokenHash` to `RefreshToken` (paired with [F1](#f1--high--refresh-tokens-are-stored-in-plaintext-)'s hashed-storage migration). On `/refresh`, if a revoked token is presented, set `RevokedAt` on every active token for that user and return 401.

---

## F5 — MEDIUM — No logout endpoint — refresh tokens cannot be invalidated on demand

Roadmap verification step 6:
> POST /api/auth/logout → revokes refresh

No such endpoint exists. A `grep` for `logout|Logout` over the backend returns zero matches.

**Impact.** Three concrete scenarios:

- A user "logs out" from the admin SPA. The SPA clears local storage. The refresh token remains valid for up to 30 days. Whoever has the token (shared laptop, residual browser session, exfiltrated copy) can keep the session alive.
- A user changes their password. Refresh tokens are unaffected. Compromised refresh tokens issued before the change continue to work.
- An admin needs to force-revoke a user's session (lost device, employee offboarding). There is no operational lever; only deleting the user works (cascade removes the tokens, per [migration](backend/RestaurantApi/Migrations/20260519152754_InitialCreate.cs)).

**Recommended fix.** Add `POST /api/auth/logout` (authenticated, body: `{ refreshToken }`) that marks the supplied token revoked. Optionally a "log out everywhere" variant that revokes all of the caller's active tokens. Wire it to the SPA's logout button (segment 21).

---

## F6 — LOW — Registration leaks whether a confirmed account exists

Roadmap checklist item:
> Sign-up does not leak whether an email exists (response is identical for "created" vs "already exists, check your inbox").

[AuthController.cs:44-85](backend/RestaurantApi/Controllers/AuthController.cs#L44-L85) handles three cases but the responses diverge:

| Case | Response |
|---|---|
| New email | `200 { status: "confirmation_sent", … }` |
| Existing & unconfirmed | `200 { status: "confirmation_sent", … }` (correctly disguised) |
| Existing & confirmed | `409 { message: "An account with this email already exists." }` |

The unconfirmed branch was clearly designed with this concern in mind (the comment at [AuthController.cs:50-51](backend/RestaurantApi/Controllers/AuthController.cs#L50-L51) is explicit). The confirmed branch was not updated to match.

**Impact.** An attacker can enumerate which addresses belong to active accounts by submitting fake registration attempts and watching for 409 vs 200. The difference is small relative to enumeration via login (which also returns a generic 401, but timing differs anyway) — but the checklist explicitly calls this out and the unconfirmed branch already does the right thing.

**Recommended fix.** Return the same 200 + "we sent you a confirmation link if you didn't already have an account; otherwise we sent you a password reset link" shape regardless of branch. (Adding password reset is [F9](#f9--low--no-password-reset-flow); until that exists, the alternative is to suppress the 409 and only log it server-side.)

---

## F7 — LOW — Default JWT signing key is committed in `appsettings.json`

[backend/RestaurantApi/appsettings.json:9](backend/RestaurantApi/appsettings.json#L9)

```json
"Key": "dev-only-super-secret-signing-key-change-me-32+chars",
```

The startup validator at [Program.cs:42-47](backend/RestaurantApi/Program.cs#L42-L47) only rejects keys shorter than 32 bytes:

```csharp
if (System.Text.Encoding.UTF8.GetByteCount(jwt.Key) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 bytes. Set it via the Jwt__Key environment " +
        "variable (appsettings.Development.json is excluded from the Docker image by .dockerignore).");
}
```

The committed key passes (54 bytes). The exception message is also misleading: it references `appsettings.Development.json`, but that file does not exist in the repo — the public default lives in `appsettings.json`, which **is** shipped in the Docker image (it is not listed in [backend/RestaurantApi/.dockerignore](backend/RestaurantApi/.dockerignore)).

**Impact.** A real deployment that forgets to set `Jwt__Key` boots with the committed key. Anyone with read access to the public repo can forge tokens for any user. The compose file does set it ([docker-compose.yml:30](docker-compose.yml#L30)), so dev is fine — but the safety net is one missing env var away from disappearing.

**Recommended fix.** Make the dev key blatantly unusable in non-Dev environments. Either change the committed value to `""` (forces every environment to supply a key explicitly), or extend the validator:

```csharp
if (!builder.Environment.IsDevelopment() &&
    jwt.Key.StartsWith("dev-only-", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Refusing to start with the development JWT key in a non-Development environment.");
}
```

Also worth correcting the error message to mention `appsettings.json` (the actual source).

---

## F8 — LOW — Refresh rotation is not atomic with new-token issuance

[AuthController.cs:149-167](backend/RestaurantApi/Controllers/AuthController.cs#L149-L167) commits the revocation, then constructs the response (which adds and commits the new refresh token) in a separate `SaveChangesAsync`:

```csharp
stored.RevokedAt = DateTime.UtcNow;
await _db.SaveChangesAsync();

return Ok(await BuildAuthResponseAsync(user));   // <-- second SaveChangesAsync inside
```

If the process dies between the two saves (DB blip, container kill, deploy), the user is left with a revoked token and no replacement — they have to log in again with a password. Not exploitable, but the [F4](#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user) fix turns this into a slightly worse user experience: a confused client retrying with the old (now revoked) token would also trigger the future "revoke-all-for-user" alarm.

**Recommended fix.** Wrap the rotation in a single transaction (`db.Database.BeginTransactionAsync`) or add the new token in the same `ChangeTracker` and call `SaveChangesAsync` once.

---

## F9 — LOW — No password reset flow

Roadmap checklist item:
> Password reset tokens: short-lived (≤1h), single-use, and tied to a specific user+timestamp.

`grep "password.*reset|reset.*password|ForgotPassword"` over the backend returns no matches. The Identity stack supports it (`GeneratePasswordResetTokenAsync`/`ResetPasswordAsync` are available out of the box and the default token provider is registered at [Program.cs:36](backend/RestaurantApi/Program.cs#L36)), but no controller wires it up.

**Impact.** This is a product gap, not a defect. A user who forgets their password has no recovery path other than admin intervention (and there is no admin tool for it either). Until added, the [F6](#f6--low--registration-leaks-whether-a-confirmed-account-exists) fix has a smaller surface: telling a user "we sent you a reset link" when they don't have an account doesn't pair with a real reset flow.

**Recommended fix.** Add `POST /api/auth/forgot-password` (always returns 200, emails a reset link if the user exists) and `POST /api/auth/reset-password` (consumes the token, sets a new password). Identity handles the token lifecycle if `Identity.DataProtectionTokenProviderOptions.TokenLifespan` is set to ≤1h.

---

## Appendix

### Bypass / sensitive-claim audit

JWT claims emitted ([JwtTokenService.cs:21-28](backend/RestaurantApi/Auth/JwtTokenService.cs#L21-L28)):

| Claim | Source | Sensitive? |
|---|---|---|
| `sub` | `AppUser.Id` (Guid) | No |
| `email` | `AppUser.Email` | Marginal — but already known by the client |
| `nameid` (`ClaimTypes.NameIdentifier`) | duplicate of `sub` | No |
| `name` | `AppUser.FullName` | No |
| `jti` | fresh `Guid.NewGuid()` | No |

`MapInboundClaims = false` is set ([Program.cs:53](backend/RestaurantApi/Program.cs#L53)), so the long-form ClaimTypes URIs do not pollute server-side `User.Claims`.

`jti` is generated but never recorded. It currently serves no purpose — either persist it for audit/revocation correlation (paired with [F4](#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user)) or drop it.

### Refresh-token storage at a glance

| Property | Value | Note |
|---|---|---|
| Generator | `RandomNumberGenerator.GetBytes(48)` | ✓ Crypto-strong, ~384 bits |
| Encoding | Standard base64 | Acceptable; URL-safe variant not required because it travels in JSON bodies, not URLs |
| Storage | Plaintext `text` column, unique index | ❌ See [F1](#f1--high--refresh-tokens-are-stored-in-plaintext-) |
| Lookup | `WHERE token = $1` | Lookup style is fine; the value being raw is the problem |
| Lifetime | `RefreshTokenDays = 30` | Per checklist |
| Revocation | `RevokedAt` timestamp | One-way revoke; no `ReplacedByToken` chain — see [F4](#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user) |
| Cascade on user delete | `OnDelete(Cascade)` | ✓ — refresh tokens removed with user |

### `/api/auth/*` endpoint inventory

| Endpoint | Auth | Notes |
|---|---|---|
| `POST /register` | anonymous | See [F6](#f6--low--registration-leaks-whether-a-confirmed-account-exists). |
| `POST /confirm-email` | anonymous | See [F2](#f2--high--confirm-email-mints-a-fresh-token-pair-for-any-already-confirmed-user-without-validating-the-supplied-token). |
| `POST /resend-confirmation` | anonymous | Existence-safe response. ✓ |
| `POST /login` | anonymous | No lockout, no rate limit. See [F3](#f3--medium--no-brute-force-protection-on-login). |
| `POST /refresh` | anonymous (token-bearer) | Rotates correctly but not atomic ([F8](#f8--low--refresh-rotation-is-not-atomic-with-new-token-issuance)); no reuse-detection ([F4](#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user)). |
| `GET /me` | `[Authorize]` | ✓ |
| `POST /accept-invite` | `[Authorize]` | Token is `Guid` (~122 bits, RFC4122 v4) in request body; checks status, expiry, and email match. ✓ |
| `POST /logout` | — | ❌ Does not exist. See [F5](#f5--medium--no-logout-endpoint--refresh-tokens-cannot-be-invalidated-on-demand). |
| `POST /forgot-password`, `POST /reset-password` | — | ❌ Do not exist. See [F9](#f9--low--no-password-reset-flow). |

### Identity options actually configured

[Program.cs:27-36](backend/RestaurantApi/Program.cs#L27-L36):

| Option | Value | Default | Comment |
|---|---|---|---|
| `User.RequireUniqueEmail` | `true` | `false` | ✓ |
| `Password.RequiredLength` | `8` | `6` | ✓ |
| `Password.RequireNonAlphanumeric` | `false` | `true` | Acceptable trade-off |
| `Password.RequireDigit` / `Uppercase` / `Lowercase` | default `true` | `true` | ✓ |
| `Lockout.*` | unset | 5 attempts / 5 min | Inert — see [F3](#f3--medium--no-brute-force-protection-on-login). |

### Follow-ups suggested by Segment 05 but out of scope here

- **Auth lifecycle tests are missing entirely.** [backend/RestaurantApi.Tests/](backend/RestaurantApi.Tests/) contains tests for menu/products/scan but nothing under an `Auth*` name (`Glob backend/RestaurantApi.Tests/**/Auth*.cs` → no files). The roadmap's Segment 13 also flags this as a likely gap. File as `[REV-05-T1]` covering: register-then-confirm happy path, refresh rotation, refresh-after-revoke 401, register-when-confirmed leak (after [F6](#f6--low--registration-leaks-whether-a-confirmed-account-exists) fix), and confirm-email idempotency contract (after [F2](#f2--high--confirm-email-mints-a-fresh-token-pair-for-any-already-confirmed-user-without-validating-the-supplied-token) fix).
- **Segment 11** should re-verify the confirmation email link's `link` substitution: it correctly uses `_appUrls.AdminBaseUrl` (good — no `Host` reflection), but the link encoding relies on the `Guid.ToString()` default and `Base64UrlEncode` — verify the admin frontend `/confirm-email` view consumes both query params correctly.
- **Segment 21** must verify that the admin SPA's logout actually calls the server once [F5](#f5--medium--no-logout-endpoint--refresh-tokens-cannot-be-invalidated-on-demand) is fixed, and that 401 responses from `/refresh` clear the auth store.
- **Segment 25** should pick up the rate-limiting decision from [F3](#f3--medium--no-brute-force-protection-on-login) and confirm `/auth/login`, `/auth/confirm-email`, `/auth/refresh`, `/auth/forgot-password` all have appropriate limits.

---

*Generated as part of the [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md) execution. No project code was modified during this review.*

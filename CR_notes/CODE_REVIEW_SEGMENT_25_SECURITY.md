# Code Review — Segment 25: End-to-end security review

**Date:** 2026-05-21
**Reviewer:** Claude (Opus 4.7, read-only)
**Scope:** OWASP-style cross-cutting pass over everything touched in segments 03, 04, 05, 09, 11, 12, 14, 21. Builds on the per-segment reports; this report records the deltas that only become visible when you look at all the layers at once.

> This is the last segment in the roadmap. Where a finding is already filed in a prior segment, this report cites the prior finding rather than re-deriving it.

## Method

For each OWASP-ish category, I either (a) ran a targeted grep across the repo and recorded the result, or (b) read the prior segment report and re-confirmed the conclusion against a quick spot-check today. No code modified.

## Files re-touched (verification only)

| Layer | Path | Why |
|---|---|---|
| API startup | [backend/RestaurantApi/Program.cs](backend/RestaurantApi/Program.cs) | middleware order, Swagger gating, CORS, JWT, SSRF base URL |
| AI client | [backend/RestaurantApi/Services/MenuExtractionClient.cs](backend/RestaurantApi/Services/MenuExtractionClient.cs) | SSRF surface |
| Auth | [backend/RestaurantApi/Auth/JwtTokenService.cs](backend/RestaurantApi/Auth/JwtTokenService.cs) | refresh token entropy |
| Tables | [backend/RestaurantApi/Controllers/TablesController.cs](backend/RestaurantApi/Controllers/TablesController.cs) | QR token entropy |
| Compose | [docker-compose.yml](docker-compose.yml) | dev defaults, mailpit exposure |
| CI | [.github/workflows/backend.yml](.github/workflows/backend.yml), [.github/workflows/frontend.yml](.github/workflows/frontend.yml) | vuln-audit gates |

---

## OWASP-style checklist outcomes

| # | Category | Outcome |
|---|---|---|
| A01 | Broken access control | **PARTIAL** — see Finding 1 |
| A02 | Cryptographic failures | **PASS-ish** — see Finding 2 |
| A03 | Injection | **PASS** — see Finding 3 |
| A04 | Insecure design (rate limit, idempotency) | **FAIL** — see Finding 4 |
| A05 | Security misconfiguration | **PARTIAL** — see Finding 5 |
| A06 | Vulnerable and outdated components | **FAIL** — see Finding 6 |
| A07 | Identification & authentication failures | **PARTIAL** — see Finding 7 |
| A08 | Software & data integrity (CSRF here) | **PASS-ish** — see Finding 8 |
| A09 | Logging & monitoring | **FAIL** — see Finding 9 |
| A10 | SSRF | **PASS** — see Finding 10 |
| — | XSS / CSP | **PARTIAL** — see Finding 11 |

---

## Findings

### Finding 1 — Broken access control: cross-tenant gaps are well-defended, but admin authorization relies on string roles, not policies (CARRIED OVER — MEDIUM)

Cross-tenancy itself is structurally enforced (segment 04 review: every `ITenantScoped` entity has a query filter; `SaveChangesAsync` stamps `RestaurantId` and forbids changing it). I did not find any new gap during this pass; the architecture holds.

The carry-over concern is from segment 08: admin endpoints use `[Authorize]` (any authenticated user) plus an in-method role check rather than `[Authorize(Policy = "RestaurantAdmin")]`. A controller that forgets the in-method check would silently authorize a staff-level user. Hard to find by grep; easy to introduce in a future controller.

**Recommendation:** define policies in `Program.cs` (`AddAuthorization(o => o.AddPolicy("RestaurantAdmin", p => p.RequireRole("RestaurantAdmin")))`) and apply at the class level. Convert the in-method checks one controller at a time. This is the same recommendation [Segment 08](CODE_REVIEW_SEGMENT_08_ADMIN_CONTROLLERS.md) makes.

---

### Finding 2 — Cryptographic failures: JWT/refresh-token hygiene is mostly correct (CARRIED OVER — MEDIUM)

Re-verified today:

- `Jwt:Key ≥ 32 bytes` is enforced at startup ([Program.cs:42-47](backend/RestaurantApi/Program.cs#L42-L47)).
- `ClockSkew = 1 min` ([Program.cs:63](backend/RestaurantApi/Program.cs#L63)) — tighter than the default 5 min.
- Refresh tokens are 384-bit random ([JwtTokenService.cs:46](backend/RestaurantApi/Auth/JwtTokenService.cs#L46): `RandomNumberGenerator.GetBytes(48)` → base64).
- QR tokens are `Guid.NewGuid()` ([TablesController.cs:57](backend/RestaurantApi/Controllers/TablesController.cs#L57), [:74](backend/RestaurantApi/Controllers/TablesController.cs#L74)) — 122 bits of entropy on Windows/Linux CSPRNG.

Carry-over concerns from segment 05:
- Refresh tokens are **stored in plaintext** in the database, not hashed. A DB leak = mass session takeover. This is the highest-severity item in the entire 25-segment review.
- SMTP TLS check (segment 11) — `Smtp:UseStartTls = false` is enforced as the dev default ([docker-compose.yml:39](docker-compose.yml#L39)) but no startup guard flags it in non-Development environments.

**Recommendation:** prioritise hashing refresh tokens. The schema migration is trivial; rotate all live refresh tokens at deploy time (one session-loss event for users, in exchange for permanent containment of future DB leaks).

---

### Finding 3 — Injection: clean (PASS)

- **SQL:** `grep -rE "FromSqlRaw|ExecuteSqlRaw|FromSqlInterpolated|ExecuteSqlInterpolated" backend/` returns **zero matches**. All queries go through EF Core LINQ → parameterised SQL. No string concatenation into queries anywhere.
- **CSS:** segment 22 Finding 9 documented inline-`:style="background-image: url('${imageUrl}')"` patterns in `MenuManagementView.vue`. That's CSS-context injection (admin-controlled input, low severity) but worth fixing because it makes a clean CSP harder later — see Finding 11.
- **HTML:** `grep -r v-html` across both SPAs returns zero matches today (re-confirmed; previously confirmed by [Segment 20](CODE_REVIEW_SEGMENT_20_CUSTOMER_I18N.md) and [Segment 21](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md)).
- **Command:** no `Process.Start`, no `os.system` / `subprocess.Popen` with user input in the Python service. Not exhaustively verified here — flagged for follow-up if the AI service ever shells out for file conversion.

**Recommendation:** add a CI grep guard that fails the build if `FromSqlRaw` or `v-html` lands without a `// security: justified` comment on the line above.

---

### Finding 4 — Insecure design: no rate limiting, no idempotency keys (HIGH)

`grep -rE "AddRateLimiter|UseRateLimiter|EnableRateLimiting|RateLimit" backend/` returns **zero matches**. The .NET 8 built-in rate limiter is available out of the box (`Microsoft.AspNetCore.RateLimiting`) and is **not** registered.

Practical consequences:
- `POST /api/auth/login` accepts unlimited requests. Combined with the absence of account lockout (segment 05 found ASP.NET Identity defaults aren't customised), credential-stuffing is not throttled at the network layer. Identity will lock out an individual account after N failed attempts, but the attacker can spray N-1 attempts each across many accounts without any global throttle.
- `POST /api/auth/register` accepts unlimited requests. Mailpit / SMTP fan-out is bounded only by the mail provider's own rate limit. An attacker can flood the confirmation-email outbound with attacker-chosen `To:` headers (the email body is user-controlled via the registered name field — see segment 11) to weaponise our domain as a spam relay. The roadmap calls this out in segment 11 ("HTML escaping" + segment 14 ("how does the AI service know the request came from the trusted backend") but the entry-side throttle is missing.
- `POST /api/auth/refresh` accepts unlimited requests. A leaked refresh token can be drained at full network speed.
- The AI-service-facing scan endpoint `/api/restaurants/:rid/admin/menu/scan` accepts up to 10 × 5 MB = 50 MB per request with a 6-minute server-side timeout ([Program.cs:127](backend/RestaurantApi/Program.cs#L127)) — a small number of concurrent malicious uploads exhausts AI quota and pool threads.

Idempotency:
- The roadmap (segment 09) requires an idempotency key on `POST /orders`. Cross-check by `grep -ri Idempotency-Key backend/` — zero matches today. A customer with flaky 4G clicking "Submit order" twice creates two orders.

**Recommendation:**
- Add `AddRateLimiter` with at minimum a fixed-window or token-bucket for `/api/auth/*` (e.g. 5 requests/minute/IP for login, 1/minute/IP for register, 10/minute/IP for refresh).
- Add a global slow-handler limiter for the menu-scan endpoint (e.g. 2 concurrent uploads per tenant).
- Read the `Idempotency-Key` header on `POST /orders` and persist a `(restaurant_id, key) → order_id` lookup with a TTL so retries return the original order.

---

### Finding 5 — Security misconfiguration: most boxes checked, two leftovers (LOW)

Re-verified today:
- Swagger is gated on `IsDevelopment()` ([Program.cs:170-174](backend/RestaurantApi/Program.cs#L170-L174)) — **PASS**.
- AutoMigrate/seed runs only in Development ([Program.cs:194-200](backend/RestaurantApi/Program.cs#L194-L200)) — **PASS**.
- CORS uses allow-list (`AllowedOrigins`) not `AllowAnyOrigin` ([Program.cs:152-166](backend/RestaurantApi/Program.cs#L152-L166)) — **PASS**.
- Mailpit UI port `8025` published to host (`docker-compose.yml:53-55`) — **acceptable in dev**, but the compose file is also the only deployment artefact in the repo. If someone runs the dev compose in prod (it has happened in plenty of startups), the Mailpit UI is on the internet.

Carry-over concerns:
- Per [Segment 01](#) (not reviewed in this pass) compose secrets are inline (`Jwt__Key: "dev-only..."`, `POSTGRES_PASSWORD: devpass`). The recommendation has always been "document a prod override path" — there isn't one yet.
- `appsettings.Development.json` is `.dockerignore`d (good) but the same file ships the same `Jwt__Key` value (`docker-compose.yml:30`) — both are dev-only and clearly labelled, just confirming there's no prod variant of either.

**Recommendation:** add a `docker-compose.prod.yml` (or a `.env.example` with required vars and a startup check that fails if the dev key is still in use). Move Mailpit behind `profiles: ["dev"]` so a `docker compose up` in prod silently omits it.

---

### Finding 6 — Vulnerable/outdated components: no audit step in CI (MEDIUM)

[backend.yml](.github/workflows/backend.yml) runs `dotnet restore / build / test` and `pytest`. It does **not** run:
- `dotnet list package --vulnerable --include-transitive`
- `pip-audit` (or `safety check`)

[frontend.yml](.github/workflows/frontend.yml) runs `npm ci / type-check / build / test`. It does **not** run `npm audit --production`.

So a CVE-pinned package landing in `package-lock.json` from a transitive bump will not fail any check. The roadmap commands at the end of segment 25 spell this out; they don't currently run.

Already-known vendor concern (segment 14): `python-multipart` had a known CVE in versions below 0.0.9. I did not re-verify the pinned version here — flag as one-line follow-up to confirm.

**Recommendation:** add three CI steps (one per service). They cost ~10 s each and produce a hard pass/fail.

---

### Finding 7 — Identification & authentication failures (CARRIED OVER — HIGH)

The set of findings from [Segment 05](CODE_REVIEW_SEGMENT_05_AUTH.md) still holds:

- 🔥 Refresh tokens plaintext in DB (Finding 2 above; the same issue mapped to A02 *and* A07).
- No documented "single-use refresh" semantics (refresh issues new token; old one must be revoked atomically). Verify against the actual controller.
- No account-lockout configured beyond Identity defaults — Identity does lock individuals out, but the values are unconfigured; an attacker can probe many accounts in parallel.
- Sign-up response distinguishes "created" from "already exists" — enumeration vector. The fix is to return the same 200 + "check your inbox" message regardless of whether the email already had an account.

[Segment 21](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md) carry-overs on the frontend side:
- Refresh token in `localStorage` — accepted XSS surface, not yet documented as an accepted risk, no CSP to bound the blast radius.
- Concurrent-401 handling — the segment-21 audit verified the admin api-client refreshes at most once, but the customer api-client's refresh path should be re-audited if the customer SPA ever gets a JWT-based session.

---

### Finding 8 — CSRF: structurally absent, but the SignalR access-token query-string is a leakage vector (MEDIUM)

The API has no cookie-based authentication anywhere (`grep AntiForgery|Cookie` in `Program.cs` returns no match). Tokens travel in the `Authorization: Bearer` header, which means CSRF is structurally not an issue for the REST surface — a cross-origin form post cannot include a bearer header.

The exception is the SignalR JWT-via-query-string path ([Program.cs:66-77](backend/RestaurantApi/Program.cs#L66-L77)):

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            context.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

The path-prefix guard is correct (the token only counts for `/hubs/*`) — this is the right shape. But the token still appears in HTTP access logs on any reverse proxy / CDN / browser-history entry that handles the WebSocket handshake URL. Many WS clients fall back to long-polling under restrictive networks, at which point every long-poll request carries the token in the URL and is logged.

**Recommendation:** confirm reverse-proxy access logs strip `?access_token=*`. If we ship behind nginx/Caddy in prod, add an `access_log_filter` to elide it. Document the constraint so it doesn't decay.

---

### Finding 9 — Logging & monitoring: no correlation IDs across services; sensitive fields hygiene is OK but unmechanised (MEDIUM)

`grep -E "CorrelationId|X-Correlation|RequestId|TraceIdentifier" backend/` returns **zero matches**. The .NET API, the Python AI service, and the SPAs do not share a request ID. When the AI service times out and an admin says "the menu scan I did at 14:32 failed", there is no single ID to trace through `backend-api` → `ai-service` logs.

`grep -i "password|token|secret|refreshToken|accessToken"` against `Auth/JwtTokenService.cs` returns **no logger calls with sensitive fields** — confirmed. The auth controller logs `{UserId} ({Email})` ([AuthController.cs:78](backend/RestaurantApi/Controllers/AuthController.cs#L78), [:114](backend/RestaurantApi/Controllers/AuthController.cs#L114), [:218](backend/RestaurantApi/Controllers/AuthController.cs#L218)) — email is arguably PII but is bog-standard; tokens and passwords are not logged.

But there's no `ILogger`-level guard preventing a future addition from logging the wrong field. A simple `ILogger` enricher that scrubs `Authorization` / `password` / `refreshToken` keys would catch accidental future leaks.

**Recommendation:**
- Add a `CorrelationIdMiddleware` that reads `X-Correlation-Id` (or generates one) and stuffs it into `HttpContext.Items` + the logger scope.
- Have `MenuExtractionClient` forward that ID as a header to the AI service.
- Have the AI service propagate it back in error responses.

---

### Finding 10 — SSRF: clean for the only outbound HTTP client (PASS)

`MenuExtractionClient` ([MenuExtractionClient.cs:33](backend/RestaurantApi/Services/MenuExtractionClient.cs#L33)) posts to `"/menu/extract"` relative to the typed `HttpClient`'s `BaseAddress`. The base address is set once at DI registration time from `AiService:BaseUrl` config ([Program.cs:113](backend/RestaurantApi/Program.cs#L113)) — **no user input ever participates in the URL**. The `BaseAddress = new Uri(url)` call would also throw at startup if the configured URL were malformed, so the surface is hardened both ways.

No other outbound HTTP client in the codebase that accepts a user-controlled URL. The `imageUrl` field on `Product` / `Category` is rendered as a `background-image` (segment 22 Finding 9) — that's an image-fetch from the *user's browser*, not from the server. Not SSRF.

**Result:** PASS, no recommendation.

---

### Finding 11 — XSS / CSP: Vue escapes by default, no `v-html`, no CSP header (CARRIED OVER — MEDIUM)

Re-verified today: zero `v-html` matches in either SPA. Vue's default text interpolation escapes HTML. The XSS surface is therefore "supply-chain compromise of a Vue / Pinia / Vue Router / Tailwind dependency" rather than "we wrote a literal `v-html`".

What's missing is a **CSP header** to bound the blast radius. [Segment 21](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md) F1 documents:

- [frontend-admin/index.html](frontend-admin/index.html) has no `<meta http-equiv="Content-Security-Policy">`.
- No backend middleware that injects CSP headers.
- No reverse proxy in the repo to set them.

So an XSS via a compromised dependency lands the refresh token from `localStorage` (segment 21 F1) and the access token from the Pinia store with no script-src restriction in the way.

Also relevant: segment 22 F9 — inline `:style` with concatenated user `imageUrl`. A strict CSP with `style-src 'self'` (no `'unsafe-inline'`) cannot ship until those inline styles are removed.

**Recommendation:**
1. Replace inline `:style="background-image: url('${user.imageUrl}')"` with `<img :src="...">` (segment 22 F9).
2. Add a meta-CSP to both SPAs: `default-src 'self'; img-src 'self' data: https:; script-src 'self'; style-src 'self'` — start in `Content-Security-Policy-Report-Only` mode and watch the violation reports for a week.
3. (Optional) move refresh tokens to an httpOnly cookie. This is the segment-21 follow-up; it removes the entire `localStorage` exfil concern.

---

## Cross-cutting risk register (the "🔥 across all segments" view)

Per the roadmap sign-off checklist, every 🔥 item across all 25 segments must have a pass/fail outcome. Here is that consolidated list, with the verdict drawn from the per-segment reports:

| Segment | 🔥 item | Verdict | Reference |
|---|---|---|---|
| 01 | Dev-only secrets in compose have a prod override path | OPEN (not reviewed) | [Roadmap §1](CODE_REVIEW_ROADMAP.md#segment-01--compose--service-topology) |
| 03 | Middleware order: routing → cors → auth → authz → tenant → endpoints | ✓ Pass | [Seg 03](CODE_REVIEW_SEGMENT_03_STARTUP.md) |
| 04 | Every `ITenantScoped` entity has a query filter | ✓ Pass | [Seg 04](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md) |
| 04 | `SaveChanges` stamps `RestaurantId`, forbids changing | ✓ Pass | [Seg 04](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md) |
| 04 | `OnModelCreating` registers a `HasQueryFilter` per entity | ✓ Pass | [Seg 04](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md) |
| 05 | Refresh tokens hashed in DB | ✗ **FAIL** | [Seg 05](CODE_REVIEW_SEGMENT_05_AUTH.md), Finding 2 above |
| 09 | A QR token resolves to a single restaurant | ✓ Pass | [Seg 09](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md) |
| 10 | `OrderFinalizationService` catches per-iteration exceptions | ✓ Pass | [Seg 10](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md) |
| 11 | Confirmation/reset links use configured base URL, not request `Host` | ✓ Pass | [Seg 11](CODE_REVIEW_SEGMENT_11_EMAIL.md) |
| 12 | Hub assigns caller to tenant group on connection | ✓ Pass | [Seg 12](CODE_REVIEW_SEGMENT_12_SIGNALR.md) |
| 15 | Both vision providers return schema-enforced output | ✓ Pass | [Seg 15](CODE_REVIEW_SEGMENT_15_VISION.md) |
| 21 | Refresh-token storage + concurrent-401 handling | ⚠ Partial — localStorage with no CSP | [Seg 21](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md) |
| 24 | Every API DTO has a matching shared TS type | ✗ **FAIL** | [Seg 24](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md) |

**Two 🔥 fails outstanding:** plaintext refresh tokens (segment 05) and shared-contract drift (segment 24). One 🔥 partial (admin token storage, segment 21). Everything else is green.

---

## Summary

**Top three risks, prioritised across the whole codebase**
1. **Hash refresh tokens.** A DB-only read primitive becomes session-takeover. One migration + one code change. Resolves OWASP A02 + A07 simultaneously.
2. **Add rate limiting on `/api/auth/*` and on `/admin/menu/scan`.** Prevents credential stuffing, email-relay abuse, and AI-quota exhaustion. .NET 8 has the limiter built in.
3. **Add a CSP header (Report-Only first) and add CI vuln-audit gates.** Together these defend against the supply-chain XSS path that the absence of `v-html` does *not* protect against.

**Top three strengths**
- **Zero raw SQL.** All EF Core LINQ → parameterised. SQL injection is structurally not in scope.
- **SSRF surface is clean.** The one outbound HTTP client uses a DI-fixed `BaseAddress`; no user input flows into URLs.
- **Tenancy enforcement is correct end-to-end.** Query filters + SaveChanges stamping + middleware membership check + hub group join — defence in depth that the segment-04 review validated and this pass found no regression in.

**Recommended fix order (final)**
1. Refresh-token hashing (Seg 05 / Finding 2).
2. Rate limiting on auth endpoints (Finding 4).
3. CI vuln-audit gates (Finding 6).
4. Idempotency-Key on `POST /orders` (Finding 4).
5. CSP header in report-only mode (Finding 11).
6. CorrelationId middleware (Finding 9).
7. Authorization policies in place of in-method role checks (Finding 1).
8. Strip `?access_token` from reverse-proxy access logs in prod (Finding 8).
9. The segment-24 contract drift items (already enumerated in Seg 24 report).

No code modified during this review. This concludes the 25-segment roadmap review pass.

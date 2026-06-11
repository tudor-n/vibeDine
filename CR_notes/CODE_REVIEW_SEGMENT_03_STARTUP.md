# Segment 03 — API startup pipeline (`Program.cs`): review findings

Scope review against the [Segment 03 checklist](CODE_REVIEW_ROADMAP.md#segment-03--api-startup-pipeline-programcs) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). Startup is the most leveraged code in the API — a misordering or wrong DI lifetime here invalidates the assumptions [Segment 04](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md) and [Segment 05](CODE_REVIEW_SEGMENT_05_AUTH.md) rely on.

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**2 findings, both LOW severity.** The 🔥 checklist item (middleware order) and the most error-prone wiring items (JWT validation parameters, ClockSkew, SignalR `access_token` scope, DI lifetimes, typed `HttpClient` registration, dev-only Swagger and auto-migrate gating) all check out. The two findings are defense-in-depth opportunities — both currently work because hand-discipline at the controller level compensates, but neither is structural:

- [F1](#f1--low--no-fallback-authorization-policy--an-unannotated-controller-defaults-to-anonymous) — no `FallbackPolicy` on `AddAuthorization`, so a controller that forgets `[Authorize]` is implicitly anonymous.
- [F2](#f2--low--no-health-check-endpoint) — no `AddHealthChecks` / `/healthz`, so orchestrators have no liveness signal that is decoupled from a real endpoint.

One cross-reference: the committed dev `Jwt:Key` is the same root cause already documented in [Segment 05 F7](CODE_REVIEW_SEGMENT_05_AUTH.md#f7--low--default-jwt-signing-key-is-committed-in-appsettingsjson); the Segment 03 angle is recorded under [Cross-references](#cross-references) but not re-filed as a new finding.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 Middleware order: `UseRouting` → `UseCors` → `UseAuthentication` → `UseAuthorization` → `TenantResolutionMiddleware` → `MapControllers`/`MapHub` | ✓ Pass | [Program.cs:176-189](backend/RestaurantApi/Program.cs#L176-L189) matches exactly. |
| CORS uses allow-list, not `AllowCredentials()` + `AllowAnyOrigin()` | ✓ Pass | [Program.cs:152-166](backend/RestaurantApi/Program.cs#L152-L166): `WithOrigins(allowedOrigins)` from `AllowedOrigins__*`. |
| JWT validates issuer, audience, lifetime, signing key; `ClockSkew` ≤ 1 minute | ✓ Pass | [Program.cs:54-64](backend/RestaurantApi/Program.cs#L54-L64): all four validations on, `ClockSkew = TimeSpan.FromMinutes(1)`. |
| SignalR `access_token` query-string extraction scoped to `/hubs/*` only | ✓ Pass | [Program.cs:67-77](backend/RestaurantApi/Program.cs#L67-L77): `path.StartsWithSegments("/hubs")` guards the assignment. |
| `ICurrentTenant` is Scoped (never Singleton) | ✓ Pass | [Program.cs:17](backend/RestaurantApi/Program.cs#L17): `AddScoped<ICurrentTenant, CurrentTenant>()`. |
| `IEmailSender` and `MenuExtractionClient` correct lifetime | ✓ Pass | [Program.cs:85](backend/RestaurantApi/Program.cs#L85): `AddScoped<IEmailSender, SmtpEmailSender>()`; [Program.cs:109](backend/RestaurantApi/Program.cs#L109): `AddHttpClient<MenuExtractionClient>` registers it as Transient with a managed handler. |
| `HttpClient` for AI service uses `AddHttpClient` (no `new HttpClient()`) | ✓ Pass | [Program.cs:88-128](backend/RestaurantApi/Program.cs#L88-L128); a grep for `new HttpClient(` returns zero matches across the API. |
| Swagger gated behind `IsDevelopment()` | ✓ Pass | [Program.cs:170-174](backend/RestaurantApi/Program.cs#L170-L174). |
| `AutoMigrate` / seed runs only in Development | ✓ Pass | [Program.cs:194-200](backend/RestaurantApi/Program.cs#L194-L200); the prod plan is documented inline in the comment immediately above. |
| Env vars win over `appsettings.{Env}.json` for JWT key | ✓ Pass | Standard `WebApplication.CreateBuilder` config order (`appsettings.json` → `appsettings.{Env}.json` → env vars). `appsettings.Development.json` is also excluded from the Docker image — see [`.dockerignore:6`](backend/RestaurantApi/.dockerignore#L6) — so in-container `Jwt__*` env vars are the only source. |
| `Jwt__Key` validated to be ≥ 32 bytes at startup | ✓ Pass | [Program.cs:42-47](backend/RestaurantApi/Program.cs#L42-L47); throws `InvalidOperationException` before `app.Build()` returns. |
| Health-check endpoint exists, or absence is explicitly recorded | ⚠ Absent — see [F2](#f2--low--no-health-check-endpoint) | No `AddHealthChecks`, no `/healthz` mapping; nothing in `docker-compose.yml` either. |

---

## F1 — LOW — No fallback authorization policy: an unannotated controller defaults to anonymous

[Program.cs:80](backend/RestaurantApi/Program.cs#L80) calls `AddAuthorization()` with no arguments:

```csharp
builder.Services.AddAuthorization();
```

With ASP.NET Core's defaults, this means a controller (or action) without an `[Authorize]` attribute is implicitly anonymous. The Segment 08 roadmap item — "every action either has `[Authorize]` or is explicitly `[AllowAnonymous]` with a justifying comment" — is enforced today by per-controller hand discipline. A grep across [Controllers/](backend/RestaurantApi/Controllers/) confirms the current state: every admin/auth controller carries `[Authorize]` at the class level, and the two customer-facing controllers (`MenuController`, `CustomerController`) carry neither attribute — they are anonymous by omission.

This works while every contributor remembers to add `[Authorize]` to new admin controllers. It fails the day someone copy-pastes the `MenuController` template, drops in admin endpoints, and ships them — there is no compiler, lint, or test that catches the missing `[Authorize]`.

**Impact.** The current model is fail-open: forgetting `[Authorize]` leaks an endpoint anonymously. Inverting the default makes the model fail-closed: forgetting `[AllowAnonymous]` returns 401 for a customer route, which is loud and trivial to debug.

**Recommended fix.** Set a fallback policy that requires authentication, and explicitly mark the two customer-surface controllers (and the public `AuthController` endpoints for signup/login/refresh/confirm-email) as `[AllowAnonymous]`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

This is also the conventional posture for an API where the *exception* is anonymous traffic (QR-token customer routes + auth bootstrap), not the rule.

**Why this is LOW, not MEDIUM.** No anonymous-by-mistake endpoints exist *today* — Segment 08 will re-verify this controller by controller. The finding is about the next bug, not a current one.

---

## F2 — LOW — No health-check endpoint

A grep across the API for `healthz`, `MapHealthChecks`, and `AddHealthChecks` returns zero matches. [`docker-compose.yml:18-49`](docker-compose.yml#L18-L49) has no `healthcheck:` block for `backend-api` either (Segment 01 territory, but co-incident with this gap).

The Segment 03 verification command anticipates this:

> ```powershell
> Invoke-WebRequest http://localhost:5005/healthz  # if a health endpoint exists; otherwise note absence
> ```

Noted absent.

**Impact.** Three concrete consequences:

1. Any orchestrator (k8s readiness/liveness, ALB target group, Docker Swarm, Render) that wants a probe has to point at a real endpoint. Pointing at `/swagger/index.html` works only in Development ([Program.cs:170-174](backend/RestaurantApi/Program.cs#L170-L174) gates Swagger), and pointing at a tenant-scoped endpoint would fail the probe for the wrong reason (404 from tenant resolution).
2. Compose can't express `depends_on: condition: service_healthy` for `backend-api`, which other services (e.g., a future `frontend-*` server-side renderer, or an admin job runner) would benefit from.
3. There is no decoupled signal of "the process is up but the DB is gone" — important once the team operates this in any environment other than `docker compose up` on a laptop.

**Recommended fix.** Add the framework health-check package and map a liveness endpoint, plus an optional readiness endpoint that probes the DB:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("db");

// after app.UseAuthorization() but before MapControllers — or directly here,
// since /healthz should be reachable without auth:
app.MapHealthChecks("/healthz/live").AllowAnonymous();
app.MapHealthChecks("/healthz/ready").AllowAnonymous();
```

Pair this with a `healthcheck:` block in `docker-compose.yml` for `backend-api` (Segment 01 follow-up).

---

## Cross-references

### Committed dev `Jwt:Key` in `appsettings.json`

[appsettings.json:9](backend/RestaurantApi/appsettings.json#L9) ships:

```json
"Key": "dev-only-super-secret-signing-key-change-me-32+chars"
```

This is **53 bytes**, so the startup validator at [Program.cs:42-47](backend/RestaurantApi/Program.cs#L42-L47) — which only rejects keys shorter than 32 bytes — does not fire. A production deployment that forgets to set `Jwt__Key` boots silently with the committed value.

This is the same root cause as [Segment 05 F7](CODE_REVIEW_SEGMENT_05_AUTH.md#f7--low--default-jwt-signing-key-is-committed-in-appsettingsjson). The Segment 03 angle is that the validator could additionally reject known-public dev keys (e.g., reject when the key string contains `dev-only` or matches the shipped `appsettings.json` value when `app.Environment.IsProduction()`). Not re-filed as a separate finding here.

### `AddIdentityCore` without lockout configuration

[Program.cs:27-36](backend/RestaurantApi/Program.cs#L27-L36) registers Identity without configuring `options.Lockout`. This is the wiring side of [Segment 05 F3 — no brute-force protection on login](CODE_REVIEW_SEGMENT_05_AUTH.md#f3--medium--no-brute-force-protection-on-login). Not re-filed.

---

## What was double-checked but did not produce a finding

A handful of items looked suspect on first read and were verified out:

- **`tenant.SuppressResolution()` wrapping the `Restaurants` and `Tables` lookups in the middleware** — `Restaurant` and `RestaurantTable.QrToken` lookups are tenant-context-establishing queries; suppression is correct, the underlying entities are either tenants themselves (Restaurant) or read in a way where the global filter would self-block (Tables before tenant is set). Audit already covered under [Segment 04 — Bypass audit](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md).
- **`AllowCredentials()` combined with `AllowAnyHeader()` + `AllowAnyMethod()`** — the documented runtime trap is `AllowCredentials()` + `AllowAnyOrigin()`. Origins here are bounded ([Program.cs:161](backend/RestaurantApi/Program.cs#L161)), so this combination is supported.
- **`JwtTokenService` registered as Singleton** ([Program.cs:25](backend/RestaurantApi/Program.cs#L25)) — captures the startup `JwtOptions` snapshot; all dependencies (random generation, options) are thread-safe; no per-request state. Safe at Singleton.
- **`MenuExtractionClient` `Timeout = 6 minutes` vs the named `"AiService"` client at `10 seconds`** — the long timeout is for vision extraction (multi-page LLM calls), the short one is for synchronous recommendation/similarity calls. Both depart from the 100s default — i.e., the checklist concern ("default 100s is wrong for sync user flows") is correctly addressed in both directions.
- **`appsettings.Development.json` excluded from the Docker image** — confirmed at [`.dockerignore:6`](backend/RestaurantApi/.dockerignore#L6). The dev-only file exists on the host but never reaches the container; in-container config is `appsettings.json` + env vars from [`docker-compose.yml:22-42`](docker-compose.yml#L22-L42).
- **Tenant-aware authorization policies** — a grep for `Authorize(Policy` returns zero matches, so there is no policy that would need the tenant resolved *before* `UseAuthorization` runs. The middleware ordering (tenant resolution *after* `UseAuthorization`) is therefore safe.

---

## Verification commands run

```powershell
# Middleware ordering
Grep "UseMiddleware|MapControllers|MapHub|UseCors|UseRouting|UseAuthentication|UseAuthorization"
# → Program.cs:176-189, ordered exactly as the checklist requires.

# Health endpoint presence
Grep "healthz|/health|MapHealthChecks|AddHealthChecks"
# → No matches.

# Direct HttpClient construction (anti-pattern)
Grep "new HttpClient\("
# → No matches.

# Authorization policy fallback
Grep "Authorize\(Policy|FallbackPolicy|RequireAuthenticatedUser"
# → No matches.

# Anonymous-by-omission audit on customer surface
Grep "\[ApiController\]|\[Authorize|\[AllowAnonymous" Controllers/{Menu,Customer}Controller.cs
# → Only [ApiController]; no auth attribute on either class.
```

`dotnet ef`, `docker compose up`, and the `Invoke-WebRequest` probes from the roadmap were **not** executed in this review — those validate runtime behavior, and the findings above are all source-readable. Re-run them when closing the segment.

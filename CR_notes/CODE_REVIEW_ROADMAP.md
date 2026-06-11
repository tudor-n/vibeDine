# Code Review Roadmap — Smart Restaurant

A segmentation of the codebase into focused, reviewable units. Each segment is sized to be reviewed in a single session (1–3 hours) and is built around a **stability risk**, not a directory. Run them in the order listed: later segments lean on conclusions from earlier ones.

> **How to use this document**
> 1. Pick one segment per session — don't multi-task across segments, the cognitive context is too different.
> 2. Open the **Files in scope** in your editor; read them top-to-bottom before answering the checklist.
> 3. Work through the **Microscopic review checklist** in order. Each item is binary (pass/fail) — don't skip.
> 4. Run the **Verification commands** at the end and record output.
> 5. File issues against the checklist items that fail. Tag with the segment number (`[REV-04]`) so cross-segment patterns are easy to spot.
>
> **Conventions used below**
> - `↳` marks a follow-up question you should ask while reading.
> - 🔥 marks a high-stability-risk concern — do not close the segment without an explicit pass/fail decision on this item.

---

## Architecture at a glance

Microservices wired together via `docker-compose.yml`:

| Service | Tech | Port (host) | Purpose |
|---|---|---|---|
| `postgres` | PostgreSQL 16 | 5433 | System of record |
| `backend-api` | .NET 8 / ASP.NET Core | 5005 | REST + SignalR, multi-tenant |
| `ai-service` | Python 3.11 / FastAPI | 8000 | Vision extraction, recs, similarity |
| `mailpit` | Mailpit | 1025/8025 | Dev SMTP capture |
| `frontend-customer` | Vue 3 + Vite | 3000 | QR-token SPA for diners |
| `frontend-admin` | Vue 3 + Vite | 3001 | JWT-auth admin console |

Cross-cutting: [shared/](shared/) holds TypeScript contracts mounted into the admin container.

---

## Segment index

| # | Segment | Primary risk | Layer |
|---|---|---|---|
| 01 | [Compose & service topology](#segment-01--compose--service-topology) | Boot-time misconfiguration | Infra |
| 02 | [CI workflows & build hygiene](#segment-02--ci-workflows--build-hygiene) | Silent regressions reaching `main` | Infra |
| 03 | [API startup pipeline (`Program.cs`)](#segment-03--api-startup-pipeline-programcs) | Middleware order, DI lifetimes | Backend |
| 04 | [Multi-tenancy enforcement](#segment-04--multi-tenancy-enforcement) | 🔥 Cross-tenant data leaks | Backend |
| 05 | [Authentication & token lifecycle](#segment-05--authentication--token-lifecycle) | Session/refresh-token abuse | Backend |
| 06 | [Data layer & migrations](#segment-06--data-layer--migrations) | Irreversible schema mistakes | Backend |
| 07 | [Domain models & invariants](#segment-07--domain-models--invariants) | Inconsistent state across entities | Backend |
| 08 | [Controllers — admin surface](#segment-08--controllers--admin-surface) | Authorization gaps | Backend |
| 09 | [Controllers — customer surface](#segment-09--controllers--customer-surface) | QR-token abuse, IDOR | Backend |
| 10 | [Background & domain services](#segment-10--background--domain-services) | Race conditions, stuck orders | Backend |
| 11 | [Email subsystem](#segment-11--email-subsystem) | Token leakage, phishing surface | Backend |
| 12 | [SignalR hubs & realtime](#segment-12--signalr-hubs--realtime) | Cross-tenant broadcast, reconnect storms | Backend |
| 13 | [Backend tests (`RestaurantApi.Tests`)](#segment-13--backend-tests-restaurantapitests) | Coverage gaps that hide regressions | Backend |
| 14 | [AI service — bootstrap & routers](#segment-14--ai-service--bootstrap--routers) | Unhandled provider failures | AI |
| 15 | [AI service — vision extraction](#segment-15--ai-service--vision-extraction) | Prompt-injection, schema drift | AI |
| 16 | [AI service — recommendation/similarity/directives](#segment-16--ai-service--recommendationsimilaritydirectives) | Bad rules pushed to prod menus | AI |
| 17 | [AI service tests](#segment-17--ai-service-tests) | Untested provider switch path | AI |
| 18 | [Customer SPA — shell & state](#segment-18--customer-spa--shell--state) | Lost cart, stale session | Frontend |
| 19 | [Customer SPA — realtime client](#segment-19--customer-spa--realtime-client) | Reconnect/race, dropped events | Frontend |
| 20 | [Customer SPA — i18n (EN/RO)](#segment-20--customer-spa--i18n-enro) | Missing keys, mojibake | Frontend |
| 21 | [Admin SPA — auth store & guards](#segment-21--admin-spa--auth-store--guards) | 🔥 Token leakage / unauthorized views | Frontend |
| 22 | [Admin SPA — menu management & import](#segment-22--admin-spa--menu-management--import) | Lost work in multi-step wizard | Frontend |
| 23 | [Admin SPA — orders, metrics, AI config](#segment-23--admin-spa--orders-metrics-ai-config) | Stale data, bad directive submissions | Frontend |
| 24 | [Shared contracts (`/shared`)](#segment-24--shared-contracts-shared) | Frontend/backend type drift | Cross |
| 25 | [End-to-end security review](#segment-25--end-to-end-security-review) | OWASP top 10 | Cross |

---

## Segment 01 — Compose & service topology

**Goal:** Verify that `docker compose up` produces a working dev stack and that production-relevant settings aren't hidden behind dev-only defaults.

**Files in scope**
- [docker-compose.yml](docker-compose.yml)
- [backend/RestaurantApi/Dockerfile](backend/RestaurantApi/Dockerfile)
- [backend/ai-scripts/Dockerfile](backend/ai-scripts/Dockerfile)
- [frontend-customer/Dockerfile](frontend-customer/Dockerfile)
- [frontend-admin/Dockerfile](frontend-admin/Dockerfile)
- [.dockerignore](.dockerignore) for each service

**Microscopic review checklist**
- [ ] 🔥 Every secret defaulted in `docker-compose.yml` (`Jwt__Key`, DB password) is documented as **dev-only** and a production override path exists.
- [ ] `depends_on` with `condition: service_healthy` is set wherever a service makes calls on boot — verify `backend-api` waits for postgres *and* mailpit, and `frontend-*` are independent of API readiness.
- [ ] Volume mounts: `./backend/RestaurantApi:/src` mounts source into the container — confirm this is intended for hot-reload and not shipped to prod.
- [ ] `AllowedOrigins__0/1` matches the host ports actually exposed (3000, 3001).
- [ ] `VITE_API_URL` and `VITE_WS_URL` agree on host/port; `ws://` vs `wss://` matches the API scheme.
- [ ] `.dockerignore` excludes `bin/`, `obj/`, `node_modules/`, `.env` — otherwise local builds leak into images.
- [ ] Mailpit only exposes port 8025 to the host network (8025 is the UI — confirm it's not reachable from prod).
- [ ] `pgdata` is a named volume (not bind mount) — confirm and check what happens on `docker compose down -v`.
- [ ] Each Dockerfile pins base-image tags (no `:latest` except mailpit, which has been marked acceptable for dev).
- [ ] Healthcheck on `backend-api` exists, or there's an explicit decision recorded that it doesn't.

  ↳ Are there separate compose overrides for prod (e.g. `docker-compose.prod.yml`)? If not, how does the team deploy?

**Verification commands**
```powershell
docker compose config                       # validates and prints resolved compose
docker compose up -d --build                # full boot
docker compose ps                           # all services Up + healthy
docker compose logs backend-api --tail 50   # no fatal errors
Invoke-WebRequest http://localhost:5005/swagger/index.html  # API up
Invoke-WebRequest http://localhost:8000/health              # AI up
Invoke-WebRequest http://localhost:8025                     # Mailpit UI up
```

---

## Segment 02 — CI workflows & build hygiene

**Goal:** Confirm CI catches the failure modes a human reviewer would catch.

**Files in scope**
- [.github/workflows/backend.yml](.github/workflows/backend.yml)
- [.github/workflows/contracts.yml](.github/workflows/contracts.yml)
- [.github/workflows/frontend.yml](.github/workflows/frontend.yml)

**Microscopic review checklist**
- [ ] Each workflow runs on `pull_request` against `main` (not just `push`).
- [ ] Backend workflow runs: `dotnet restore` → `build` → `test` with a real Postgres (Testcontainers needs Docker; confirm CI runner supports it).
- [ ] Frontend workflow runs: `npm ci` (not `install`), type-check (`vue-tsc --noEmit`), lint, and tests for **both** `frontend-customer` and `frontend-admin`.
- [ ] Contracts workflow validates that shared types compile against both consumers — describe how, or flag absent.
- [ ] No `continue-on-error: true` on a step whose failure would matter.
- [ ] Secrets used by CI (if any) come from GitHub secrets, not committed env files.
- [ ] Caching: `~/.nuget/packages`, `~/.npm`, and pip caches restored across runs (perf only, not correctness).

  ↳ Is there a status check rule on `main` requiring all three workflows to pass before merge?

**Verification commands**
```powershell
gh workflow list
gh run list --workflow backend.yml --limit 5
gh run list --workflow frontend.yml --limit 5
gh run list --workflow contracts.yml --limit 5
```

---

## Segment 03 — API startup pipeline (`Program.cs`)

**Goal:** The middleware/DI graph is the most leveraged code in the API. A misordering here causes silent auth bypass or tenant leaks.

**Files in scope**
- [backend/RestaurantApi/Program.cs](backend/RestaurantApi/Program.cs)
- [backend/RestaurantApi/appsettings.json](backend/RestaurantApi/appsettings.json)
- [backend/RestaurantApi/appsettings.Development.json](backend/RestaurantApi/appsettings.Development.json)

**Microscopic review checklist**
- [ ] 🔥 Middleware order: `UseRouting` → `UseCors` → `UseAuthentication` → `UseAuthorization` → `TenantResolutionMiddleware` → `MapControllers`/`MapHub`. Tenant resolution **must** run after auth.
- [ ] CORS policy uses an allow-list (`AllowedOrigins__*` from env), not `AllowCredentials() + AllowAnyOrigin()` — that combination silently throws at runtime.
- [ ] `AddAuthentication(JwtBearerDefaults...)` validates issuer, audience, lifetime, and signing key. `ClockSkew` is ≤ 1 minute (not the default 5).
- [ ] SignalR JWT extraction from query string is **scoped to `/hubs/*` paths only** — otherwise tokens leak into access logs for other paths.
- [ ] DI lifetimes: `ICurrentTenant` is **Scoped**, never Singleton. `IEmailSender` and `MenuExtractionClient` are correct lifetime (typed HttpClient → transient handler, scoped or transient consumer).
- [ ] HttpClient for AI service uses `AddHttpClient` with a named/typed client (not `new HttpClient()`) — otherwise socket exhaustion under load.
- [ ] Swagger is gated behind `app.Environment.IsDevelopment()` (or equivalent) — not exposed in prod.
- [ ] `AutoMigrate` / seed runs **only in Development**, with a recorded plan for how prod migrations are applied (manual `dotnet ef database update`, init container, etc.).
- [ ] No `WebApplication.CreateBuilder` config layer overrides another silently (env vars must win over `appsettings.Development.json` for JWT keys).

  ↳ Where is `Jwt__Key` validated to be ≥32 bytes? If not validated, a short key throws at first request, not at startup.

**Verification commands**
```powershell
docker compose up -d backend-api
docker compose exec backend-api dotnet --info
Invoke-WebRequest http://localhost:5005/healthz  # if a health endpoint exists; otherwise note absence
docker compose logs backend-api 2>&1 | Select-String -Pattern "warn|error" -CaseSensitive:$false
```

---

## Segment 04 — Multi-tenancy enforcement 🔥

**Goal:** Every read and write must be scoped to the caller's restaurant. This is the highest-risk segment in the API.

**Files in scope**
- [backend/RestaurantApi/Tenancy/ICurrentTenant.cs](backend/RestaurantApi/Tenancy/ICurrentTenant.cs)
- [backend/RestaurantApi/Tenancy/TenantAccess.cs](backend/RestaurantApi/Tenancy/TenantAccess.cs)
- [backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs)
- [backend/RestaurantApi/Data/AppDbContext.cs](backend/RestaurantApi/Data/AppDbContext.cs) (global query filters + `SaveChanges` stamping)
- [backend/RestaurantApi/Models/ITenantScoped.cs](backend/RestaurantApi/Models/ITenantScoped.cs)

**Microscopic review checklist**
- [ ] 🔥 **Every** entity that holds tenant-owned data implements `ITenantScoped`. Grep `Models/*.cs` and produce the list — confirm: `Product`, `Category`, `Order`, `OrderItem`, `RestaurantTable`, `AIDirective`, `RecommendationDirective`, `SimilarityRules`, `FlashDealOffer`, `Invitation`. Anything missing is a bug.
- [ ] 🔥 `AppDbContext.OnModelCreating` registers a `HasQueryFilter` for every `ITenantScoped` entity. Walk the file line-by-line and tick each one.
- [ ] 🔥 `SaveChangesAsync` override stamps `RestaurantId` on **Added** entities and **forbids changing** it on **Modified** entities (a tenant transfer is a bug, not a feature).
- [ ] `SuppressResolution()` (or whatever the bypass is called) has a hard documented set of callers — list them. Each call site needs a comment justifying why bypass is correct.
- [ ] `TenantResolutionMiddleware` reads `restaurantId` from route **or** `qrToken` from query — confirm precedence rules and what happens when both are present.
- [ ] Membership check (`TenantAccess`) is invoked from the middleware for authenticated requests *and* enforced again at the controller level via `[Authorize]` policies — defense in depth.
- [ ] Anonymous customer routes (QR-token-based) cannot reach restaurant-scoped endpoints that require membership. Trace one: `GET /menu/{qrToken}` vs `POST /admin/products`.
- [ ] Query filters can be bypassed with `IgnoreQueryFilters()` — grep the codebase. Each call needs justification.

  ↳ What happens when the query filter's `RestaurantId` is `Guid.Empty` (unresolved tenant)? Does it return zero rows, or does it leak everything?

**Verification commands**
```powershell
# Find every IgnoreQueryFilters / SuppressResolution call
```
Use Grep for: `IgnoreQueryFilters`, `SuppressResolution`, `ITenantScoped`, then list call sites.

**Integration test idea** (record as a follow-up if missing):
> Seed two restaurants R1 and R2 with overlapping product names. Authenticate as R1's admin. Hit every admin endpoint with R2's `restaurantId`. None should succeed.

---

## Segment 05 — Authentication & token lifecycle

**Goal:** Sign-up, sign-in, refresh, password reset, email confirmation all behave correctly under adversarial input.

**Files in scope**
- [backend/RestaurantApi/Controllers/AuthController.cs](backend/RestaurantApi/Controllers/AuthController.cs)
- [backend/RestaurantApi/Auth/JwtTokenService.cs](backend/RestaurantApi/Auth/JwtTokenService.cs)
- [backend/RestaurantApi/Auth/JwtOptions.cs](backend/RestaurantApi/Auth/JwtOptions.cs)
- [backend/RestaurantApi/Models/RefreshToken.cs](backend/RestaurantApi/Models/RefreshToken.cs)
- [backend/RestaurantApi/Models/AppUser.cs](backend/RestaurantApi/Models/AppUser.cs)
- [backend/RestaurantApi/DTO/Requests/AuthRequests.cs](backend/RestaurantApi/DTO/Requests/AuthRequests.cs)
- [backend/RestaurantApi/DTO/Responses/AuthResponses.cs](backend/RestaurantApi/DTO/Responses/AuthResponses.cs)

**Microscopic review checklist**
- [ ] 🔥 Refresh tokens are stored **hashed** in DB (not plaintext). If plaintext, every DB leak is a session takeover.
- [ ] Refresh tokens are single-use: a refresh issues a new token *and* revokes the old one in the same transaction.
- [ ] Refresh token revocation: logout marks the token revoked; reuse of a revoked token triggers revoke-all-for-user.
- [ ] Password reset tokens: short-lived (≤1h), single-use, and tied to a specific user+timestamp.
- [ ] Email-confirmation tokens: same constraints; an unconfirmed account can't log in (or can only log in to a restricted view).
- [ ] `AppUser` password hashing is via ASP.NET Identity defaults (PBKDF2) — not custom.
- [ ] Sign-up does not leak whether an email exists (response is identical for "created" vs "already exists, check your inbox").
- [ ] Brute-force protection: lockout after N failed attempts, configurable in Identity options.
- [ ] JWT claims include `sub` = user ID, `jti` for revocation correlation, and **do not** include `password_hash`, `email_confirmed`, or other server-only state.
- [ ] `AccessTokenMinutes` is reasonable (≤60 min). `RefreshTokenDays` (30) — confirm acceptable for product.
- [ ] Validators reject weak passwords (length, complexity) at the API layer, not only client-side.

  ↳ When a user is deleted, are their refresh tokens cascaded-deleted?
  ↳ Where is the rate limit on `/auth/login`? If absent, document as a known gap.

**Verification commands**
```powershell
# Manual flow via curl/Postman after compose up:
# 1) POST /api/auth/signup -> 200 + email in mailpit
# 2) Click confirm link -> account active
# 3) POST /api/auth/login -> access + refresh
# 4) POST /api/auth/refresh -> new pair
# 5) POST /api/auth/refresh with OLD refresh -> 401
# 6) POST /api/auth/logout -> revokes refresh
# 7) POST /api/auth/refresh again -> 401
```

---

## Segment 06 — Data layer & migrations

**Goal:** Schema is migrate-forward-safe and indexes match query patterns.

**Files in scope**
- [backend/RestaurantApi/Data/AppDbContext.cs](backend/RestaurantApi/Data/AppDbContext.cs)
- [backend/RestaurantApi/Data/SeedData.cs](backend/RestaurantApi/Data/SeedData.cs)
- [backend/RestaurantApi/Migrations/](backend/RestaurantApi/Migrations/) (all `.cs` files)

**Microscopic review checklist**
- [ ] Each migration is reversible (`Down` method generated and not empty for non-trivial changes).
- [ ] Indexes exist for FKs and frequently filtered columns: `Product.CategoryId`, `Product.RestaurantId`, `Order.RestaurantId`, `Order.TableId`, `RefreshToken.UserId`, `RestaurantTable.QrToken` (unique).
- [ ] `QrToken` is `UNIQUE` globally (not per-restaurant), since lookup is unauthenticated.
- [ ] `OrderNumber` has a composite index with `RestaurantId` (per the explore report) — confirm uniqueness scope.
- [ ] Cascade rules are intentional: `OrderItem.ProductId` is `Restrict` (preserves history) — confirm every other FK's `OnDelete`.
- [ ] `decimal` precision is set explicitly for `Price`, `Total`, `SimilarityRules.Score` — Npgsql defaults are not what you want for money.
- [ ] Timestamps use `timestamptz` (UTC) consistently; `Restaurant.Timezone` is for display only.
- [ ] Seed data runs only in Development and is idempotent (can re-run without duplicates).
- [ ] No data migration code lives in `OnModelCreating` — only schema.
- [ ] Initial migration matches a single coherent schema (no "fixup" migrations in the same week without explanation in commit messages).

  ↳ How does the team handle a backward-incompatible migration against a populated prod DB?

**Verification commands**
```powershell
# Inside the backend container:
docker compose exec backend-api dotnet ef migrations list
docker compose exec backend-api dotnet ef migrations script --idempotent  # full SQL, eyeball it
docker compose exec postgres psql -U dev -d restaurant -c '\dt'
docker compose exec postgres psql -U dev -d restaurant -c '\di'   # index list
```

---

## Segment 07 — Domain models & invariants

**Goal:** Entities encode business rules; invalid states should be unrepresentable.

**Files in scope**
All of [backend/RestaurantApi/Models/](backend/RestaurantApi/Models/) (16 files).

**Microscopic review checklist**
- [ ] Each entity has a single primary key, typed `Guid` (no `int` autoincrement leaking to URLs).
- [ ] `Order.Status` and `OrderItem` flags are enums, not strings — or, if strings, validated at DB-level via check constraints.
- [ ] `FlashDealOffer.ExpiresAt` and `Status` cannot drift: code that transitions to `EXPIRED` should be idempotent and the only mutator (not multiple callers).
- [ ] `Membership.Role` is an enum with a documented set: `OrgOwner`, `RestaurantAdmin`, `Staff`. Anything else is a bug.
- [ ] `Invitation` has expiry + status + token; one-shot acceptance enforced at controller layer.
- [ ] `RefreshToken` has `RevokedAt`, `ReplacedByToken` (for chain detection), `ExpiresAt`.
- [ ] `Product.SortOrder`, `Category.SortOrder` are integers, allow gaps, and have a documented strategy for reordering (large gaps vs full-rewrite).
- [ ] `RestaurantTable.QrToken` is sufficiently entropic (cryptographically random, ≥128 bits) — find the generator and verify.
- [ ] No entity has setters that allow mutating its `RestaurantId` after creation — combined with the `SaveChanges` guard in segment 04.
- [ ] Money fields never use `double` / `float`. Always `decimal`.

  ↳ Are there places where domain logic lives in controllers that should be on the entity?

---

## Segment 08 — Controllers — admin surface

**Goal:** Authorization is correct and inputs are validated.

**Files in scope**
- [backend/RestaurantApi/Controllers/AdminController.cs](backend/RestaurantApi/Controllers/AdminController.cs)
- [backend/RestaurantApi/Controllers/MenuManagementController.cs](backend/RestaurantApi/Controllers/MenuManagementController.cs)
- [backend/RestaurantApi/Controllers/MenuScanController.cs](backend/RestaurantApi/Controllers/MenuScanController.cs)
- [backend/RestaurantApi/Controllers/OrganizationsController.cs](backend/RestaurantApi/Controllers/OrganizationsController.cs)
- [backend/RestaurantApi/Controllers/TablesController.cs](backend/RestaurantApi/Controllers/TablesController.cs)
- [backend/RestaurantApi/Validators/ProductValidator.cs](backend/RestaurantApi/Validators/ProductValidator.cs)

**Microscopic review checklist**
- [ ] 🔥 Every action either has `[Authorize]` (default policy) or is explicitly `[AllowAnonymous]` with a justifying comment.
- [ ] Role-restricted actions use a policy (e.g. `[Authorize(Policy = "RestaurantAdmin")]`), not string-comparison inside the method.
- [ ] Route `{restaurantId:guid}` is constrained to a Guid and is reconciled against the authenticated user's memberships (segment 04 handles enforcement; here verify the controller doesn't override).
- [ ] Bulk endpoints (`bulk-create-products`) enforce a max batch size and reject mixed-tenant payloads.
- [ ] File upload endpoints (`MenuScanController`) enforce: max size, allowed MIME types, no PDF zip-bombs, file count limit.
- [ ] DTOs do not bind sensitive fields (`Id`, `RestaurantId`, `CreatedAt`) from the request body — only the parts the client should control.
- [ ] Returning DTOs (not entities) so EF lazy-load doesn't leak nav properties to JSON.
- [ ] `400` for validation, `403` for cross-tenant, `404` for not-found-or-not-owned (avoid leaking existence).
- [ ] PATCH/PUT preserve `RestaurantId` (segment 07 invariant).
- [ ] No `async void` methods.

  ↳ When a bulk import partially fails, is the response shape clear about which rows failed and why?

---

## Segment 09 — Controllers — customer surface

**Goal:** A QR token alone is enough authority — make sure it can't do anything an admin should authorize.

**Files in scope**
- [backend/RestaurantApi/Controllers/CustomerController.cs](backend/RestaurantApi/Controllers/CustomerController.cs)
- [backend/RestaurantApi/Controllers/MenuController.cs](backend/RestaurantApi/Controllers/MenuController.cs)

**Microscopic review checklist**
- [ ] 🔥 A valid QR token resolves to a single `RestaurantTable` and that's the **only** restaurant the request can touch. Trace once.
- [ ] Customer cannot modify menu, prices, or directives — only read.
- [ ] Order placement: server **recomputes** total from menu prices, ignores any client-supplied price.
- [ ] Order placement validates: products exist, are available, belong to the resolved restaurant, quantities >0 and within limits.
- [ ] Flash-deal redemption checks `Status == ACCEPTED` and `ExpiresAt > now` server-side.
- [ ] Recommendation endpoint never reveals products from a different restaurant or unavailable items.
- [ ] Table session state is scoped to the QR token — switching tables creates a new session.
- [ ] Rate limit on order placement (1 per N seconds per table) — document if absent.
- [ ] Idempotency key supported for `POST /orders` to handle network retries without duplicate orders.

  ↳ When a QR token is rotated by an admin, do in-flight customer sessions fail cleanly (401) or silently break?

---

## Segment 10 — Background & domain services

**Files in scope**
- [backend/RestaurantApi/Services/FlashDealService.cs](backend/RestaurantApi/Services/FlashDealService.cs)
- [backend/RestaurantApi/Services/OrderFinalizationService.cs](backend/RestaurantApi/Services/OrderFinalizationService.cs) (hosted service)
- [backend/RestaurantApi/Services/RecommendationService.cs](backend/RestaurantApi/Services/RecommendationService.cs)
- [backend/RestaurantApi/Services/SimilarityService.cs](backend/RestaurantApi/Services/SimilarityService.cs)
- [backend/RestaurantApi/Services/MenuExtractionClient.cs](backend/RestaurantApi/Services/MenuExtractionClient.cs)

**Microscopic review checklist**
- [ ] 🔥 `OrderFinalizationService` (IHostedService): catches and logs exceptions per iteration so one bad row doesn't kill the loop; sleeps with a `PeriodicTimer` (not `Task.Delay` in `while(true)`); honors `CancellationToken` on shutdown.
- [ ] Background services create their own scope (`IServiceScopeFactory`) for DbContext — never inject `AppDbContext` directly into a singleton.
- [ ] `FlashDealService.Expire` runs in a transaction or uses optimistic concurrency to avoid double-expiry.
- [ ] `MenuExtractionClient` has: timeout configured (default `HttpClient.Timeout` of 100s is wrong for sync user flows), retry policy with jitter, circuit breaker or fail-fast on repeated AI outages.
- [ ] AI client failures map to a clear API error (e.g., 503 with "AI service unavailable") — not a 500 stack trace.
- [ ] `RecommendationService` and `SimilarityService` validate that AI-returned product IDs exist in the current restaurant before persisting (segment 16's twin).
- [ ] No service has a static mutable field used for caching (memory leak / cross-request bleed).
- [ ] Logging includes `RestaurantId` and `CorrelationId` in scopes so prod logs are traceable.

  ↳ What's the on-call response when the AI service is down for 30 minutes? (Should be: customer flows degrade gracefully; admin flows return a clear error.)

---

## Segment 11 — Email subsystem

**Files in scope**
- [backend/RestaurantApi/Services/Email/IEmailSender.cs](backend/RestaurantApi/Services/Email/IEmailSender.cs)
- [backend/RestaurantApi/Services/Email/SmtpEmailSender.cs](backend/RestaurantApi/Services/Email/SmtpEmailSender.cs)
- [backend/RestaurantApi/Services/Email/SmtpOptions.cs](backend/RestaurantApi/Services/Email/SmtpOptions.cs)
- [backend/RestaurantApi/Services/Email/AppUrlOptions.cs](backend/RestaurantApi/Services/Email/AppUrlOptions.cs)

**Microscopic review checklist**
- [ ] 🔥 Confirmation/reset links use the configured `App__AdminBaseUrl` — never reflect a `Host` or `Origin` header from the request (host-header injection → phishing).
- [ ] Tokens in URLs are URL-encoded.
- [ ] Email body templates (HTML or text) escape user-provided fields (name, restaurant name) to avoid HTML injection.
- [ ] SMTP credentials come from config, never hardcoded.
- [ ] On send failure: log + return a generic "we'll retry" response, do not surface SMTP errors to the user.
- [ ] In production, `Smtp__UseStartTls` is `true`. In dev (Mailpit), it's correctly `false` — confirm a check exists that flags `false` outside dev.
- [ ] Sending is awaited and not fire-and-forget on a Task — otherwise dropped sends are invisible.
- [ ] Subject and from-name are localized only if relevant; otherwise document the single language.

  ↳ Is there a retry/outbox pattern for transient SMTP failures, or is one missed email gone forever?

**Verification commands**
```powershell
# After triggering signup or password reset:
Start-Process http://localhost:8025   # mailpit UI; eyeball headers, links, body
```

---

## Segment 12 — SignalR hubs & realtime

**Files in scope**
- [backend/RestaurantApi/Hubs/CustomerHub.cs](backend/RestaurantApi/Hubs/CustomerHub.cs)
- [backend/RestaurantApi/Hubs/StaffHub.cs](backend/RestaurantApi/Hubs/StaffHub.cs)
- [backend/RestaurantApi/Hubs/TenantGroups.cs](backend/RestaurantApi/Hubs/TenantGroups.cs)

**Microscopic review checklist**
- [ ] 🔥 On connection, the hub assigns the caller to a group keyed by `RestaurantId` (and **only** that restaurant's group). Cross-tenant broadcast is structurally impossible.
- [ ] `CustomerHub` accepts QR-token authentication; `StaffHub` requires JWT auth. Authorization is enforced via `[Authorize]` on the class.
- [ ] No public hub methods that accept a `restaurantId` parameter without re-checking ambient tenant.
- [ ] OnDisconnected cleans up group memberships (SignalR does this automatically — confirm no manual override broke it).
- [ ] Server pushes only DTOs, never EF entities (lazy-load JSON loop).
- [ ] Connection limits / message-size limits configured — defaults are generous and can be DoS vectors.
- [ ] Events emitted match the client's expectations (segment 19): `FlashDealAvailable`, `FlashDealExpired`, `OrderStatusUpdated` — names match exactly.
- [ ] Token expiry mid-connection: documented behavior (reconnect required, vs silent drop).

  ↳ Is there a server-side mechanism to drop a connection when an admin revokes a QR token?

---

## Segment 13 — Backend tests (`RestaurantApi.Tests`)

**Files in scope**
- [backend/RestaurantApi.Tests/](backend/RestaurantApi.Tests/) — all `*.cs` (exclude `bin/`, `obj/`)

**Microscopic review checklist**
- [ ] `TestWebApplicationFactory` uses Testcontainers Postgres (real DB) — not in-memory — so EF behaviors match prod.
- [ ] One end-to-end test per controller covering: happy path, cross-tenant attempt → 403/404, invalid input → 400.
- [ ] WireMock.Net mocks of the AI service cover: success, timeout, 500, malformed JSON.
- [ ] Tests are deterministic (no `DateTime.UtcNow` in assertions without injection; no random ordering).
- [ ] Tests run in parallel safely (separate databases or transaction-rollback per test).
- [ ] `BulkCreateProductsTests` covers partial-failure and exact-error response shape.
- [ ] `ProductValidatorTests` is property-based or at least covers boundary values (zero price, very long name, unicode).

**Gaps to file as follow-ups** (likely missing — check):
- Auth lifecycle tests (refresh reuse, revoke chain).
- Multi-tenancy negative tests (the integration test from segment 04).
- SignalR hub tests (TestServer + SignalR client).

**Verification commands**
```powershell
docker compose exec backend-api dotnet test --logger "console;verbosity=normal"
# Or locally:
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj
```

---

## Segment 14 — AI service — bootstrap & routers

**Files in scope**
- [backend/ai-scripts/main.py](backend/ai-scripts/main.py)
- [backend/ai-scripts/routers/recommendations.py](backend/ai-scripts/routers/recommendations.py)
- [backend/ai-scripts/routers/similarity.py](backend/ai-scripts/routers/similarity.py)
- [backend/ai-scripts/routers/directives.py](backend/ai-scripts/routers/directives.py)
- [backend/ai-scripts/routers/menu_extraction.py](backend/ai-scripts/routers/menu_extraction.py)
- [backend/ai-scripts/models/schemas.py](backend/ai-scripts/models/schemas.py)
- [backend/ai-scripts/models/menu_extraction.py](backend/ai-scripts/models/menu_extraction.py)

**Microscopic review checklist**
- [ ] CORS: the AI service is only called by the backend API (server-to-server). Confirm CORS is restrictive or absent (no browser caller).
- [ ] `/health` is liveness only — does not call the LLM provider (otherwise health check spends money).
- [ ] All endpoints accept and return validated Pydantic models (no `dict` blobs).
- [ ] No endpoint accepts a `restaurantId` and exposes data — confirm the service is stateless w.r.t. tenants.
- [ ] Error responses use a consistent envelope (e.g. `{"detail": "..."}`) and don't leak provider stack traces.
- [ ] File upload (`/menu/extract`) limits: per-file size, total request size, file count, allowed MIME types.
- [ ] Timeouts configured on the LLM client side (segment 15) so endpoints don't hang past the caller's deadline.
- [ ] `python-multipart` version pinned ≥ 0.0.9 (a known CVE was below).
- [ ] Logging includes `request_id` propagated from the API.

  ↳ How does the AI service know the request came from the trusted backend and not from the public internet? (network-only isolation, or a shared header secret?)

---

## Segment 15 — AI service — vision extraction

**Files in scope**
- [backend/ai-scripts/services/menu_extraction_service.py](backend/ai-scripts/services/menu_extraction_service.py)
- [backend/ai-scripts/services/vision_extractor.py](backend/ai-scripts/services/vision_extractor.py)

**Microscopic review checklist**
- [ ] 🔥 Both providers (Claude, Gemini) return structured output **enforced by schema** (Claude tool-use, Gemini response schema) — never free-text parsed by regex.
- [ ] `AI_VISION_PROVIDER` env switch is the only way to pick a provider; default is the more reliable one (currently Gemini per `.env.example`).
- [ ] Per-page extraction runs in parallel with bounded concurrency (`asyncio.Semaphore`) — uncapped `gather` exhausts API quotas.
- [ ] Failures: a failed page produces a `FailedPage` entry with reason; the overall request succeeds with partial data. Total failure threshold documented.
- [ ] Media-type sniffing: file headers (magic bytes) checked, not just filename extension.
- [ ] PDFs are page-split (or rejected past N pages) — confirm the limit.
- [ ] No PII or sensitive data is logged in raw form (image content is fine to log path/hash, not bytes).
- [ ] Prompt is constant (no user-controlled prompt injection via filename, etc.).
- [ ] Costs: per-request token estimate logged so spend can be attributed.
- [ ] `FakeVisionExtractor` is wired only via dependency override in tests, never in prod.

  ↳ If both providers are configured, what's the fallback strategy? (None? Explicit? Document it.)

---

## Segment 16 — AI service — recommendation/similarity/directives

**Files in scope**
- [backend/ai-scripts/services/recommendation_engine.py](backend/ai-scripts/services/recommendation_engine.py)
- [backend/ai-scripts/services/similarity_engine.py](backend/ai-scripts/services/similarity_engine.py)
- [backend/ai-scripts/services/directive_parser.py](backend/ai-scripts/services/directive_parser.py)

**Microscopic review checklist**
- [ ] All three engines accept the **menu in the request** and return results referencing only those products — never invent product IDs.
- [ ] Recommendation returns 2–3 items max, never duplicates of items already in cart.
- [ ] Confidence scores are normalized [0,1] and consistent across providers.
- [ ] Directive parser handles RO **and** EN (per the explore report) — verify with a few real prompts in each language.
- [ ] Directive parser has both LLM and regex paths — document when each is used and ensure they don't disagree silently.
- [ ] Similarity engine outputs scores rounded to match `SimilarityRules.Score` precision (decimal(5,4)) — otherwise EF truncates and loses meaning.
- [ ] LLM JSON outputs go through Pydantic validation before being returned (catch hallucinated shapes).
- [ ] When the LLM returns an empty result, the engine returns an empty list, not `null`.
- [ ] Romanian-specific test strings exist in tests (segment 17) — flag if missing.

  ↳ How are directive conflicts resolved? (Two rules both saying "recommend X with Y" vs "never recommend X" — who wins?)

---

## Segment 17 — AI service tests

**Files in scope**
- [backend/ai-scripts/tests/](backend/ai-scripts/tests/)

**Microscopic review checklist**
- [ ] `FakeVisionExtractor` wired via dependency override — no real API calls in CI.
- [ ] Tests cover: single-page success, multi-page success, mixed success/failure, all-failure (full request fails appropriately).
- [ ] Tests for each router exist (only `test_menu_extraction_router.py` currently — file follow-ups for the others).
- [ ] Recommendation/similarity/directive engines have unit tests with deterministic LLM mocks.
- [ ] Schema validation tests: a malformed LLM response is rejected, not propagated.
- [ ] No test depends on a real `GEMINI_API_KEY`.
- [ ] Tests run via `pytest -q` in CI without network access.

**Gaps likely** (file as follow-ups): recommendations router test, similarity router test, directives router test, RO/EN directive parsing test.

**Verification commands**
```powershell
docker compose exec ai-service pytest -q
```

---

## Segment 18 — Customer SPA — shell & state

**Files in scope**
- [frontend-customer/src/main.ts](frontend-customer/src/main.ts), [App.vue](frontend-customer/src/App.vue)
- [frontend-customer/src/view/OrderingView.vue](frontend-customer/src/view/OrderingView.vue)
- All Pinia stores in [frontend-customer/src/stores/](frontend-customer/src/stores/)
- [frontend-customer/src/services/api-client.ts](frontend-customer/src/services/api-client.ts)

**Microscopic review checklist**
- [ ] App boots from a QR token in the URL (`/:qrToken?`). Missing or invalid token shows a clean "Scan your QR code" screen, not a stack trace.
- [ ] `sessionStorage` (not `localStorage`) holds the resolved `restaurantId` — wiped on tab close.
- [ ] Cart store persists across tab refresh (which storage? confirm and document expiry).
- [ ] All API calls go through `api-client.ts` — no `fetch` scattered through components.
- [ ] API errors render a toast/banner; never `alert(...)`.
- [ ] Loading states are explicit per feature (no global blocking spinner that masks problems).
- [ ] No `console.log` in production builds (lint rule or strip).
- [ ] Vite env vars used (`VITE_API_URL`, `VITE_WS_URL`) — no hardcoded localhost:5005.
- [ ] No secrets in Vite env (`VITE_*` is shipped to the browser).
- [ ] Accessibility: tap targets ≥44px (this is the diner-facing app on phones).

  ↳ When the backend returns a localized error message, does the customer SPA forward it or replace it with a generic message in their language?

**Verification commands**
```powershell
docker compose up -d frontend-customer
Start-Process http://localhost:3000/some-qr-token
# Manual smoke: change network to offline mid-order, observe behavior
```

---

## Segment 19 — Customer SPA — realtime client

**Files in scope**
- [frontend-customer/src/composables/useWebSocket.ts](frontend-customer/src/composables/useWebSocket.ts) (or wherever the SignalR client lives)
- Consumers: [frontend-customer/src/stores/useFlashDealStore.ts](frontend-customer/src/stores/useFlashDealStore.ts), [useOrderStatusStore.ts](frontend-customer/src/stores/useOrderStatusStore.ts)

**Microscopic review checklist**
- [ ] 🔥 Event names match backend hub names exactly (`FlashDealAvailable`, `FlashDealExpired`, `OrderStatusUpdated`) — case-sensitive.
- [ ] Reconnect backoff (currently 5s per explore report) has jitter and a max-attempt cap or exponential growth — otherwise reconnect storms after an outage.
- [ ] On reconnect, the client re-fetches state (orders, deals) so events missed during disconnect are reconciled.
- [ ] Connection closes cleanly on page unload (`beforeunload`) — orphaned connections eat backend resources.
- [ ] Heartbeat / keepalive handled by SignalR protocol (binary `\x1e` frame separator per report) — confirm not reimplemented manually.
- [ ] Errors from the WS layer don't crash the component tree — they bubble to a global handler.
- [ ] No PII logged from received messages.

  ↳ How does the customer's WS authenticate? (QR token in query string, per backend.) What happens if the token is revoked while connected?

---

## Segment 20 — Customer SPA — i18n (EN/RO)

**Files in scope**
- [frontend-customer/src/composables/useLocale.ts](frontend-customer/src/composables/useLocale.ts)
- All `.vue` files that use translation keys.

**Microscopic review checklist**
- [ ] Every user-visible string comes from the locale dictionary — no hardcoded English in templates.
- [ ] Both EN and RO dictionaries have **identical key sets**. Diff them.
- [ ] Romanian diacritics (`ă`, `â`, `î`, `ș`, `ț`) render correctly (UTF-8 source, no double-encoding).
- [ ] Locale persists (`localStorage` per explore report) and is read on boot before the first paint.
- [ ] Number / currency / date formatting uses `Intl.*` with the active locale, not hardcoded strings.
- [ ] When a key is missing, fallback is English, not a literal `key.path` shown to the user.
- [ ] Pluralization rules handled where needed (Romanian has different rules from English).
- [ ] No HTML tags inside translation strings unless rendered with `v-html` and known safe.

**Verification commands**
```powershell
# Compare key sets
# (Use a small node script or jq once the dictionary is split into JSON; or eyeball if it's one file)
```

---

## Segment 21 — Admin SPA — auth store & guards 🔥

**Files in scope**
- [frontend-admin/src/stores/useAuthStore.ts](frontend-admin/src/stores/useAuthStore.ts)
- [frontend-admin/src/router/index.ts](frontend-admin/src/router/index.ts) (or wherever guards live)
- [frontend-admin/src/services/api-client.ts](frontend-admin/src/services/api-client.ts)
- [frontend-admin/src/views/LoginView.vue](frontend-admin/src/views/LoginView.vue)
- [frontend-admin/src/views/ConfirmEmail.vue](frontend-admin/src/views/ConfirmEmail.vue)
- [frontend-admin/src/views/AcceptInvite.vue](frontend-admin/src/views/AcceptInvite.vue)

**Microscopic review checklist**
- [ ] 🔥 Refresh token storage: if in `localStorage`, document the XSS risk acceptance. Prefer `httpOnly` cookie if available; if not, ensure CSP is strict.
- [ ] Access-token refresh logic in `api-client.ts` handles **concurrent 401s** — multiple in-flight requests should trigger a single refresh, not N.
- [ ] On refresh failure, the user is redirected to `/login` and the store cleared.
- [ ] Route guards check both authentication **and** role/membership for restricted routes — list every guarded route.
- [ ] Restored-from-localStorage user state is **re-verified** with the backend on app boot (a token might have been revoked).
- [ ] Logout: clears tokens, clears stores, redirects to login, calls backend `/logout` (so refresh token is revoked).
- [ ] `LoginView` does not log credentials, does not autofill from URL params.
- [ ] `AcceptInvite` token comes from URL, is sent in the body (not query string when POSTing), is single-use server-side (segment 05).
- [ ] No `eval`, no dynamic imports of untrusted URLs.

  ↳ If a user belongs to multiple restaurants, how is the "current restaurant" selected and how is it changed? Where is it persisted?

---

## Segment 22 — Admin SPA — menu management & import

**Files in scope**
- [frontend-admin/src/views/MenuManagement.vue](frontend-admin/src/views/MenuManagement.vue)
- [frontend-admin/src/views/MenuImport.vue](frontend-admin/src/views/MenuImport.vue)
- [frontend-admin/src/components/menu-import/](frontend-admin/src/components/menu-import/) (4 step components)
- [frontend-admin/src/stores/useMenuManagementStore.ts](frontend-admin/src/stores/useMenuManagementStore.ts), [useMenuImportStore.ts](frontend-admin/src/stores/useMenuImportStore.ts)
- [frontend-admin/src/services/menuImportApi.ts](frontend-admin/src/services/menuImportApi.ts)

**Microscopic review checklist**
- [ ] Wizard state survives accidental tab close or browser refresh — or, if not, the user is warned with `beforeunload`.
- [ ] Step-back is non-destructive (don't lose review edits when going back to step 2).
- [ ] File upload shows progress; failed upload can be retried without restarting the wizard.
- [ ] Validation errors are shown inline next to the row, not in a global toast.
- [ ] Bulk save: the API result's per-row status is rendered (segment 08).
- [ ] Optimistic UI updates roll back on failure.
- [ ] Image previews are revoked (`URL.revokeObjectURL`) on unmount to avoid memory leaks.
- [ ] Long lists virtualize or paginate (a 500-item menu shouldn't render 500 DOM nodes at once).
- [ ] Sort order edits commit in a way that doesn't race with concurrent edits from another admin.

  ↳ When two admins edit the same product simultaneously, what's the conflict resolution? Last-write-wins, or optimistic concurrency error from the API?

---

## Segment 23 — Admin SPA — orders, metrics, AI config

**Files in scope**
- [frontend-admin/src/views/OrdersLog.vue](frontend-admin/src/views/OrdersLog.vue), [ActiveOrdersBoard.vue](frontend-admin/src/views/ActiveOrdersBoard.vue)
- [frontend-admin/src/views/AIConfig.vue](frontend-admin/src/views/AIConfig.vue), [RulesManagement.vue](frontend-admin/src/views/RulesManagement.vue)
- [frontend-admin/src/components/DailyPromptInput.vue](frontend-admin/src/components/DailyPromptInput.vue), [RecommendationRuleInput.vue](frontend-admin/src/components/RecommendationRuleInput.vue), [SimilarityRuleTable.vue](frontend-admin/src/components/SimilarityRuleTable.vue), [PerformanceMetrics.vue](frontend-admin/src/components/PerformanceMetrics.vue)
- Related stores in [frontend-admin/src/stores/](frontend-admin/src/stores/)

**Microscopic review checklist**
- [ ] `ActiveOrdersBoard` polls or subscribes via SignalR — confirm which, and document acceptable staleness. (If the explore report says no admin WS, this is polling — flag it.)
- [ ] Status transitions on the board (e.g. "preparing" → "ready") are confirmed (button text reflects target state, not "are you sure" twice).
- [ ] AI directive submission: user can preview the parsed rules before they're saved.
- [ ] Directive deletion has a confirmation modal.
- [ ] Date-range filters on metrics use the restaurant's timezone, not the browser's.
- [ ] Empty states (no orders, no rules) are designed, not blank pages.
- [ ] Error states have actionable next steps ("retry", "check internet", "contact support") not raw error codes.
- [ ] QR code generation on tables view uses the `VITE_CUSTOMER_URL` env, not hardcoded `localhost:3000`.

  ↳ When the AI service is down, what does the AI config page do? Does it show stale rules and a banner, or refuse to load?

---

## Segment 24 — Shared contracts (`/shared`)

**Files in scope**
- All of [shared/types/](shared/types/)
- [shared/contracts/api-contract-ai.json](shared/contracts/api-contract-ai.json)

**Microscopic review checklist**
- [ ] 🔥 Every DTO returned by the .NET API has a matching TypeScript type in `shared/types/`. Diff every endpoint.
- [ ] `shared/` is volume-mounted into `frontend-admin` (per compose) but **not** `frontend-customer` — confirm intentional or extend.
- [ ] `api-contract-ai.json` describes the AI service contract — confirm it matches the Pydantic schemas in `models/schemas.py` and `models/menu_extraction.py`.
- [ ] Types are exported via a single `index.ts` per folder, no `import` from deep paths in consumers.
- [ ] Enums (`OrderStatus`) match exactly: backend C# enum string values ↔ TS union literals ↔ Python schemas.
- [ ] Date fields are typed as `string` (ISO8601), not `Date` — JSON has no Date type.
- [ ] Money fields are typed as `string` or `number` consistently across the three layers (decision documented).

  ↳ Is there a generator (e.g., NSwag, openapi-generator) that produces these types from the C# source, or are they hand-maintained? If hand-maintained, what catches drift?

**Verification commands**
```powershell
# Type-check both frontends — surfaces shared type mismatches
docker compose exec frontend-admin npx vue-tsc --noEmit
docker compose exec frontend-customer npx vue-tsc --noEmit
```

---

## Segment 25 — End-to-end security review

**Goal:** Cross-cutting security pass after the per-segment reviews. Run this last; many findings will already be covered.

**Files in scope:** all touched in segments 03, 04, 05, 09, 11, 12, 14, 21.

**Microscopic review checklist (OWASP-ish)**
- [ ] **Injection:** EF Core parameterizes — confirm no raw SQL (`FromSqlRaw` / `ExecuteSqlRaw`) without parameters. Grep.
- [ ] **Broken access control:** segment 04 + 21 must both pass.
- [ ] **Cryptographic failures:** segment 05 (refresh-token hashing, JWT key length) + segment 06 (TLS for SMTP in prod) must pass.
- [ ] **Insecure design:** rate limiting on auth endpoints (segment 05), idempotency on order placement (segment 09).
- [ ] **Security misconfiguration:** Swagger off in prod (segment 03); secrets not in compose for prod (segment 01).
- [ ] **Vulnerable and outdated components:** run `dotnet list package --vulnerable`, `npm audit`, `pip-audit`.
- [ ] **Identification/auth failures:** segment 05.
- [ ] **SSRF:** `MenuExtractionClient` only talks to the configured AI service URL — confirm no user-controlled URL is ever passed to it.
- [ ] **Logging & monitoring:** correlation IDs in logs across services; sensitive fields (passwords, tokens) never logged.
- [ ] **CSRF:** API uses JWT in `Authorization` header (no cookie auth), so CSRF is not the primary risk — but the SignalR query-string token is. Confirm tokens aren't logged at proxies.
- [ ] **XSS:** Vue's default escaping is on; any `v-html` usage justified. Grep both frontends.
- [ ] **Content Security Policy:** is one set? If not, document as a follow-up.

**Verification commands**
```powershell
docker compose exec backend-api dotnet list package --vulnerable --include-transitive
docker compose exec frontend-admin npm audit --production
docker compose exec frontend-customer npm audit --production
docker compose exec ai-service pip-audit
```

---

## Sign-off checklist (run after all 25 segments)

- [ ] Every 🔥 checklist item across all segments has an explicit pass/fail outcome.
- [ ] Failing items tracked as issues with `[REV-NN]` tags.
- [ ] The verification commands at the end of each segment have been run on a clean stack and outputs archived.
- [ ] A two-paragraph summary written: top three risks, top three strengths, and recommended fix order.


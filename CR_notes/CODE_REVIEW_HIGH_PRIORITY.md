# Smart Restaurant — Pre-Production Blocker List

**Compiled:** 2026-05-21
**Source:** All 25 segment reports in `CODE_REVIEW_SEGMENT_*.md`.
**Purpose:** the **minimum** that must change before this codebase serves paying clients. Anything not on this list is a known issue but does not, by itself, block shipping. Issues are deduplicated across segments and ordered by *do-this-first* priority within each tier.

> Each item below cites the originating segment report(s) so the full context, recommended fix sketch, and code references are one click away.

---

## Tier 0 — CRITICAL: do not deploy until these are fixed

Each of these is either an active session-takeover primitive, a cross-tenant data leak, or makes the whole stack undeployable to prod safely.

### 0.1 — Account takeover via `/api/auth/confirm-email`

**Source:** [Seg 05 F2](CODE_REVIEW_SEGMENT_05_AUTH.md#f2--high--confirm-email-mints-a-fresh-token-pair-for-any-already-confirmed-user-without-validating-the-supplied-token).

`POST /api/auth/confirm-email` mints a fresh `(access, refresh)` pair for any UserId whose user is already confirmed, **without inspecting the `token` field at all** ([AuthController.cs:87-116](backend/RestaurantApi/Controllers/AuthController.cs#L87-L116)). UserId is a `Guid` but it is *not* secret — it appears in confirmation-email URLs, mailbox forwards, IT mail-scan logs, browser history, and `Referer` headers. Once observed, the holder can request session tokens for that user indefinitely with `{ userId, token: "anything" }`. Combined with the lack of rate limit (0.6) and lack of logout (0.5), the takeover is permanent.

**Fix shape:** in the already-confirmed branch, return `{ status: "already_confirmed" }` without minting tokens. Frontend routes that response to `/login`.

### 0.2 — Refresh tokens stored plaintext (DB leak = mass session takeover)

**Source:** [Seg 05 F1](CODE_REVIEW_SEGMENT_05_AUTH.md#f1--high--refresh-tokens-are-stored-in-plaintext-) (🔥), [Seg 07 F3](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f3--medium--refreshtoken-is-missing-replacedbytoken-and-is-stored-in-plaintext), [Seg 25 Finding 2](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-2--cryptographic-failures-jwtrefresh-token-hygiene-is-mostly-correct-carried-over--medium).

Refresh tokens are 384-bit CSPRNG output (good) stored as plaintext `text` in `refresh_tokens` with a unique index ([RefreshToken.cs:11](backend/RestaurantApi/Models/RefreshToken.cs#L11), [AppDbContext.cs:260-271](backend/RestaurantApi/Data/AppDbContext.cs#L260-L271)). Any DB leak — backup, replica, SQL injection in unrelated code, over-permissioned analytics user, dev dump on a laptop — hands the attacker bearer credentials for every active session for the next 30 days.

**Fix shape:** rename `Token` → `TokenHash`, store `SHA-256(token)`, look up by hashing the inbound value. One-way migration; all current sessions invalidate at deploy. Pair with 0.4 (single-use rotation alarm).

### 0.3 — Cross-tenant transfer via `SaveChanges` (🔥 invariant unmet)

**Source:** [Seg 04 F1](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md#f1--high--savechanges-does-not-forbid-restaurantid-changes-on-modified-entities-) (🔥), [Seg 07 F1](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries).

`AppDbContext.StampTenant()` only handles `EntityState.Added`. Every `ITenantScoped` entity exposes a public mutable `RestaurantId` setter. There is **no guard** against mutating `entity.RestaurantId = otherTenant` on a tracked entity. The query filter blocks *reads* across tenants but not writes — a single buggy controller, a future refactor, or a `SuppressResolution`-wrapped service can silently transfer a row from tenant A to tenant B. The roadmap names this as a 🔥 invariant: *"a tenant transfer is a bug, not a feature."*

**Fix shape:** in `StampTenant`, walk Modified entries and throw `InvalidOperationException` when `entry.Property(nameof(ITenantScoped.RestaurantId)).IsModified`. Two lines.

### 0.4 — Anonymous customer can submit/wipe other tables' orders within the same restaurant (🔥 invariant unmet)

**Source:** [Seg 09 F1](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f1--high--qr-token-binds-only-the-session-call-subsequent-customer-endpoints-trust-the-url-restaurantid-) (🔥).

The QR token is validated **only once**, at `GET /tables/{qrToken}/session`. Every other customer endpoint (`POST /orders`, `PATCH /orders/{id}/items`, `POST /orders/{id}/submit`, `POST /orders/{id}/claim-deal/{dealId}`, `GET /orders/{id}/recommendations`) is anonymous and authenticated by URL `restaurantId` alone. The QR never re-enters the equation. Within an active restaurant, anyone with a `(TableId, OrderId)` pair — both `Guid`, neither treated as secret by the system, and the `tableId` is broadcast on the customer SignalR group while `orderId` lands in the staff-hub broadcast on every submit — can:

- `PATCH {"items": []}` to wipe another table's cart
- `POST /submit` another table's draft prematurely
- `POST /orders {TableId: any_table_at_R, ...}` to create orders at any table
- `GET /orders/{victim_orderId}/recommendations` to read another table's active flash deal (leaks `dealId`)

**Fix shape:** issue a short-lived signed cookie/HMAC from `GET /tables/{qrToken}/session` capturing `(restaurantId, tableId)`, validate it on every subsequent customer endpoint as a proxy `[Authorize]`.

### 0.5 — Flash-deal theft via `ClaimDealAsync` missing ownership check

**Source:** [Seg 09 F2](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f2--high--claimdealasync-does-not-verify-that-the-deal-belongs-to-the-order), [Seg 10 F2](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f2--high--claimdealasync-has-a-check-then-write-race-no-transaction-no-concurrency-token).

`FlashDealService.ClaimDealAsync(orderId, dealId)` loads the deal and the order independently, checks `Status`/`ExpiresAt`, and adds the deal's product to the order — but **never asserts `deal.OrderId == order.Id`** ([FlashDealService.cs:42-88](backend/RestaurantApi/Services/FlashDealService.cs#L42-L88)). The model carries `OrderId` on `FlashDealOffer` but the field is unread. Combined with 0.4, an attacker reads any table's `dealId` from the recommendations endpoint then claims it to their own order. Status flips to `"claimed"`, denying the rightful target. Separately: the claim is also a check-then-write race (no transaction, no concurrency token).

**Fix shape:** add `if (deal.OrderId != order.Id) return false;` after the null check; wrap the claim mutation in a transaction or add an EF concurrency token on `FlashDealOffer.Status`.

### 0.6 — CustomerHub is anonymous + cross-tenant subscribe (🔥 invariant unmet)

**Source:** [Seg 12 F1](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-) (🔥), [Seg 12 F2](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) (🔥), [Seg 19 F1](CODE_REVIEW_SEGMENT_19_CUSTOMER_REALTIME.md#f1--high--the-qr-token-query-parameter-is-decorative-the-server-never-reads-it-customerhub-is-anonymous-).

Two compounding failures:
1. `CustomerHub` has no `[Authorize]`, no `OnConnectedAsync`. Group membership comes from client-invoked RPCs `JoinTableGroup(restaurantId, tableId)` / `JoinRoleGroup(restaurantId, role)` that trust whatever the client sent ([CustomerHub.cs:7-14](backend/RestaurantApi/Hubs/CustomerHub.cs#L7-L14), [StaffHub.cs:9-17](backend/RestaurantApi/Hubs/StaffHub.cs#L9-L17)).
2. The customer SPA sends `?token=qrToken` but the server reads `?access_token` ([Program.cs:71](backend/RestaurantApi/Program.cs#L71)), and even if the names matched, a QR token is not a JWT and wouldn't validate.

A client can `JoinTableGroup("<any restaurant guid>", "<any table guid>")` and receive every `FlashDealAvailable`, `FlashDealExpired`, `OrderStatusUpdated` for that table.

**Fix shape:** read `restaurantId` from `Context.User.Claims` (StaffHub, after a membership check) or from a server-side QR-token lookup (CustomerHub). Assign the group in `OnConnectedAsync`. Remove the public `Join*Group` methods. Fix the client-side parameter name.

### 0.7 — Dev secrets baked in compose with no prod override path

**Source:** [Seg 01 F1](CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md#finding-1----dev-secrets-in-compose-with-no-documented-prod-override-path-high), [Seg 25 Finding 5](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-5--security-misconfiguration-most-boxes-checked-two-leftovers-low), [Seg 05 F7](CODE_REVIEW_SEGMENT_05_AUTH.md#f7--low--default-jwt-signing-key-is-committed-in-appsettingsjson).

`docker-compose.yml` is the only deployment artefact in the repo, and it inlines `POSTGRES_PASSWORD: devpass`, `Jwt__Key: "dev-only-super-secret-signing-key-change-me-32+chars"`, mailpit ports `1025+8025` published to host, `Smtp__UseStartTls: "false"`. The `Jwt__Key` is also committed to [`appsettings.json:9`](backend/RestaurantApi/appsettings.json#L9) — the startup validator at [Program.cs:42-47](backend/RestaurantApi/Program.cs#L42-L47) accepts it because it's ≥32 bytes. **A prod deployment that forgets one env var boots with a publicly known JWT key.** Anyone reading the repo can forge tokens for any user.

**Fix shape:** (a) add `docker-compose.prod.yml` overriding every secret with `${VAR}` and gating Mailpit behind `profiles: ["dev"]`; (b) extend the startup validator to refuse known-dev key strings outside `Development`; (c) add a root `.env.example` enumerating every required variable.

### 0.8 — CI runs `dotnet test` from the wrong directory; backend suite is silently green

**Source:** [Seg 13 F1](CODE_REVIEW_SEGMENT_13_TESTS.md#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-) (CRITICAL), [Seg 13 F2](CODE_REVIEW_SEGMENT_13_TESTS.md#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-) (HIGH), [Seg 02 F1](CODE_REVIEW_SEGMENT_02_CI_WORKFLOWS.md#finding-1--frontend-ci-has-no-lint-step-and-frontend-customer-tests-are-a-fake-echo-high).

[backend.yml:18](.github/workflows/backend.yml#L18) pins `working-directory: backend/RestaurantApi` (the web project, not the test project) — `dotnet test` from there finds zero test projects and exits 0. **Every PR has merged into `main` since the workflow was authored without a single backend assertion being evaluated.** Worse, all 8 integration tests still target pre-multi-tenancy routes from `830f123` — they would all 404 if they ran. Customer SPA `npm run test` is `echo "no tests yet" && exit 0` ([Seg 02 F1](CODE_REVIEW_SEGMENT_02_CI_WORKFLOWS.md#finding-1--frontend-ci-has-no-lint-step-and-frontend-customer-tests-are-a-fake-echo-high)) — also vacuously green.

This isn't just a quality problem — it means every other Tier 0/1 fix below ships with no automated verification.

**Fix shape:** drop `working-directory`, run `dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj` from repo root. Fix the routes in the eight integration tests. Replace customer `test` script with `vitest run`.

### 0.9 — SMTP plaintext default with no startup guard (🔥)

**Source:** [Seg 11 F1](CODE_REVIEW_SEGMENT_11_EMAIL.md#f1--high--smtp-transport-defaults-to-plaintext-no-startup-guard-flags-unsafe-tls-outside-development-).

`SmtpOptions` defaults `UseStartTls = false` and `UseSsl = false`. No `IValidateOptions<SmtpOptions>`, no `.ValidateOnStart()`, no `env.IsDevelopment()` check anywhere. A prod deploy that mirrors compose env-var names — which explicitly set `Smtp__UseStartTls: "false"` — sends SMTP `AUTH PLAIN` credentials and confirmation-token URLs in cleartext. The roadmap calls for an explicit guard; none exists.

**Fix shape:** add `Configure<SmtpOptions>(...).Validate(o => env.IsDevelopment() || o.UseStartTls || o.UseSsl, "Smtp must use TLS outside Development").ValidateOnStart()`.

---

## Tier 1 — HIGH: ship-blocking before paying clients

Each here is either a live attack path, a data-loss bug, or undermines operational sanity.

### 1.1 — No rate limit on `/api/auth/*` and `/admin/menu/scan`

**Source:** [Seg 05 F3](CODE_REVIEW_SEGMENT_05_AUTH.md#f3--medium--no-brute-force-protection-on-login), [Seg 25 Finding 4](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-4--insecure-design-no-rate-limiting-no-idempotency-keys-high), [Seg 09 F5](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f5--low--no-rate-limit-on-order-placement-checklist-asks-document-if-absent).

`grep "AddRateLimiter|UseRateLimiter"` across the backend returns zero matches. `UserManager.CheckPasswordAsync` is used at login ([AuthController.cs:133](backend/RestaurantApi/Controllers/AuthController.cs#L133)) — that method does **not** track `AccessFailedCount`, does **not** consult `LockoutEnd`, does **not** apply Identity's lockout policy (because `SignInManager` was deliberately omitted). Net: unlimited password guesses per email; unlimited register/refresh; unlimited 50 MB × 6-min menu-scan calls that drive LLM spend.

**Fix shape:** `builder.Services.AddRateLimiter(...)` with fixed-window or token-bucket on `/api/auth/login` (e.g. 5/min/IP), `/register` (1/min/IP), `/refresh` (10/min/IP), and a concurrency limit on `/admin/menu/scan` (2/tenant). .NET 8 has the limiter built in.

### 1.2 — Refresh token in `localStorage` + no CSP + no httpOnly cookie (🔥 admin auth)

**Source:** [Seg 21 F1](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--) (🔥), [Seg 25 Finding 11](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-11--xss--csp-vue-escapes-by-default-no-v-html-no-csp-header-carried-over--medium).

Both `accessToken` and `refreshToken` are persisted to `localStorage` ([useAuthStore.ts:66-75](frontend-admin/src/stores/useAuthStore.ts#L66-L75)). There is no `httpOnly` cookie path on the backend, no CSP in [frontend-admin/index.html](frontend-admin/index.html), no documented XSS-risk acceptance. A single XSS payload — from a supply-chain dependency compromise, or any future stored-XSS — exfiltrates the refresh token, and combined with 1.3 (no logout endpoint) the attacker has 30 days of access.

**Fix shape:** at minimum, add a meta-CSP to `index.html` in `Content-Security-Policy-Report-Only` mode for a week, then enforce. Long term: move refresh token to `HttpOnly; Secure; SameSite=Strict` cookie.

### 1.3 — No `/api/auth/logout`; users cannot revoke their sessions

**Source:** [Seg 05 F5](CODE_REVIEW_SEGMENT_05_AUTH.md#f5--medium--no-logout-endpoint--refresh-tokens-cannot-be-invalidated-on-demand), [Seg 21 F3](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout).

`grep "logout"` in [Controllers/AuthController.cs](backend/RestaurantApi/Controllers/AuthController.cs) returns zero. The admin SPA's "Logout" button clears `localStorage` and that's it ([useAuthStore.ts:152-159](frontend-admin/src/stores/useAuthStore.ts#L152-L159)). The refresh token stays valid for 30 days — a lost laptop, a shared kiosk, an employee offboarding, or 1.2's leaked token cannot be revoked operationally.

**Fix shape:** `POST /api/auth/logout` (authenticated, body `{ refreshToken }`) sets `RevokedAt`. SPA's logout button calls it before clearing local state.

### 1.4 — Refresh-token reuse doesn't trigger revoke-all-for-user (no theft-detection)

**Source:** [Seg 05 F4](CODE_REVIEW_SEGMENT_05_AUTH.md#f4--medium--reuse-of-a-revoked-refresh-token-does-not-trigger-revoke-all-for-user), [Seg 07 F3](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f3--medium--refreshtoken-is-missing-replacedbytoken-and-is-stored-in-plaintext).

[AuthController.cs:152-167](backend/RestaurantApi/Controllers/AuthController.cs#L152-L167) handles reuse by returning 401 — no defensive action. The `RefreshToken` entity has no `ReplacedByTokenHash` chain field. Without it, an attacker who steals a token and rotates it once is silently the only holder of the active session — the legitimate user's stale token returns 401 the next time they try, looks like an expired session, they log in again, and the attacker keeps going.

**Fix shape:** add `ReplacedByTokenHash` (paired with 0.2's hashing migration). On `/refresh`, if a *revoked* token is presented, set `RevokedAt` on every active token for that user (the "family") and return 401 with a structured "session compromised, log in again" code.

### 1.5 — Concurrent 401s log the admin out mid-session

**Source:** [Seg 21 F2](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md#f2--high--concurrent-401s-trigger-n-parallel-refresh-calls-singleuse-refresh-token-guarantees-n1-of-them-fail--logout).

`authedFetcher` calls `auth.tryRefresh()` on every 401 independently ([api-client.ts:135-138](frontend-admin/src/services/api-client.ts#L135-L138)). When N parallel admin requests all see 401 after access-token expiry, each launches its own refresh. Backend rotates refresh tokens single-use — only the first refresh wins, the other N-1 return 401, `tryRefresh` catches and calls `logout()`. **The user is kicked out of an active session for no fault of their own** every time multiple requests race past the 60-min access-token expiry.

**Fix shape:** coalesce refresh — first 401 starts the refresh, every other concurrent 401 awaits the same in-flight `Promise`.

### 1.6 — Email-confirmation send failures silently succeed at API level

**Source:** [Seg 11 F2](CODE_REVIEW_SEGMENT_11_EMAIL.md#f2--high--register--resendconfirmation-always-respond-confirmation_sent-even-when-smtp-throws).

`SendConfirmationEmailAsync` catches and logs SMTP errors ([AuthController.cs:256-264](backend/RestaurantApi/Controllers/AuthController.cs#L256-L264)). `Register` / `ResendConfirmation` unconditionally return `200 { status: "confirmation_sent" }`. **The account is created, marked unconfirmed, the user can never log in, and the API claims success.** No outbox, no retry.

**Fix shape:** at minimum, return a "we'll try again" envelope to the frontend so the UI can show "retry in a moment". Better: a real outbox (`Outbox` table + a hosted service that retries).

### 1.7 — Order placement: no `Quantity > 0`, no `IsAvailable` check, no idempotency key

**Source:** [Seg 09 F3](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f3--medium--createorder-and-additemsorder-do-not-validate-quantity--0-or-product-availability), [Seg 09 F4](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f4--medium--post-orders-has-no-idempotency-key--retries-create-duplicate-draft-orders), [Seg 25 Finding 4](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-4--insecure-design-no-rate-limiting-no-idempotency-keys-high).

- `CreateOrder` accepts any signed `Quantity` — zero or negative values produce negative `TotalAmount` ([CustomerController.cs:120](backend/RestaurantApi/Controllers/CustomerController.cs#L120)).
- Neither `CreateOrder` nor `AddItemsOrder` filters out unavailable products. `Products.FindAsync(item.ProductId)` returns hidden products; the customer SPA's display-time filter is the only block, and it's bypassable by sending raw `ProductId`s.
- No `Idempotency-Key` header inspection on `POST /orders`; a phone with a flaky connection retrying creates duplicate drafts.

**Fix shape:** FluentValidation rule `Quantity >= 1 && Quantity <= 99`. `Products.FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsAvailable == true)`. Accept `Idempotency-Key` and store a unique `(restaurant_id, key) → order_id` lookup.

### 1.8 — Flash-deal expiry in fire-and-forget `Task.Run` (lost on restart)

**Source:** [Seg 10 F1](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f1--high--flash-deal-expiry-runs-in-a-fire-and-forget-taskrun-and-is-lost-on-process-restart-) (🔥).

[FlashDealService.cs:262-283](backend/RestaurantApi/Services/FlashDealService.cs#L262-L283) fires the deal's 2-minute expiry via `_ = Task.Run(async () => { await Task.Delay(...); ... })`. On any `docker compose restart` or deploy mid-window, the timer is gone and the deal stays `pending` indefinitely. Exceptions inside the lambda are unobservable (discarded task).

**Fix shape:** move expiry into the existing `OrderFinalizationService` (or a sibling hosted service) — poll for `Status == "pending" AND ExpiresAt <= UtcNow`, transition + broadcast in a transaction.

### 1.9 — `/admin/menu/scan` and Python `/menu/extract`: header-only MIME check, no PDF page cap, Python directly exposed

**Source:** [Seg 08 F3](CODE_REVIEW_SEGMENT_08_ADMIN_CONTROLLERS.md#f3--medium--menuscan-trusts-client-supplied-content-type-no-magic-byte-sniffing), [Seg 08 F4](CODE_REVIEW_SEGMENT_08_ADMIN_CONTROLLERS.md#f4--medium--menuscan-accepts-pdfs-with-no-page-count-limit-checklist-names-no-pdf-zip-bombs), [Seg 14 F2](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service), [Seg 14 F3](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f3--high--no-trust-boundary-the-service-binds-00008000-is-published-to-the-host-and-has-no-auth-on-llm-spending-endpoints).

The C# layer checks the client-supplied `Content-Type` header (trivially forged), has no PDF page-count limit (5 MB compressed PDF → 20-30k pages), and forwards the bytes to the Python service. The Python service binds `0.0.0.0:8000`, is **published to the host**, has zero auth, zero MIME check, zero size limit. An attacker who can reach the Docker host bypasses all C# guards by hitting Python directly — and burns paid LLM spend without limit.

**Fix shape:** magic-byte sniff in C# (FF D8 FF / 89 50 4E 47 / RIFF...WEBP / %PDF-); peek the PDF `/Count` for page limit. Add a shared-secret header validated by Python middleware, OR bind Python to an internal Docker network only (drop the `8000:8000` publish in compose).

### 1.10 — No bulk batch-size cap on `POST /admin/menu/products/bulk`

**Source:** [Seg 08 F1](CODE_REVIEW_SEGMENT_08_ADMIN_CONTROLLERS.md#f1--high--bulkcreate-has-no-max-batch-size-explicit-checklist-fail).

`MenuManagementController.BulkCreate` enforces only `Items.Count == 0`. Kestrel's default 30 MB body accepts ~400 000 product entries — all loaded into the EF change tracker, then a single fat INSERT batch locks `products`/`categories` for the duration. One buggy or malicious admin client wedges their own tenant's writes.

**Fix shape:** reject `Items.Count > 500` (or pick a sensible per-tenant cap). Also handle the `request.Items is null` deserialization case to avoid NRE → 500.

### 1.11 — `directives.py` swallows AI failures into 200 OK with empty result

**Source:** [Seg 14 F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) (🔥), [Seg 10 F5](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites).

Both directive-parsing endpoints catch `except Exception: return {"parsed_rules": {}}` ([routers/directives.py:35-36, 81-82](backend/ai-scripts/routers/directives.py#L35-L82)). The C# admin controller persists a directive with `parsedRules = null`; the admin's SPA shows a success toast; **the rule is silently dead**. Compounded by no `response_model`, no logging, no metric.

**Fix shape:** narrow the catch to specific exceptions, return `502 Bad Gateway` with structured envelope, log the exception. Declare Pydantic `response_model` on both endpoints. C# side maps 502 to 503 + user-visible "AI temporarily unavailable" message.

### 1.12 — Customer SPA cart loss + silent cart-sync errors

**Source:** [Seg 18 F1](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution), [Seg 18 F2](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f2--high--cart-sync-errors-are-silent-only-consoleerror-no-toastbanner-no-retry).

`useCartStore` keeps items in a fresh `ref<CartItem[]>([])` — tab refresh empties the cart and loses every `wasAiRecommended` attribution flag. Worse, `syncCartWithBackend` catches `apiClient.updateCartItems` failures with `console.error` and emits no UI signal. The customer sees their local cart fine; the backend has stale (or empty) order; **`submitOrder` then sends what the backend has — not what the customer sees**.

**Fix shape:** persist `items`/`recommendedItemIds` to `sessionStorage`; rehydrate on mount from either storage or the backend's active order. Surface sync errors via toast/banner with retry.

### 1.13 — Customer SPA realtime: anonymous WS + no reconnect backoff + no state reconciliation

**Source:** [Seg 19 F1](CODE_REVIEW_SEGMENT_19_CUSTOMER_REALTIME.md#f1--high--the-qr-token-query-parameter-is-decorative-the-server-never-reads-it-customerhub-is-anonymous-) (covered by 0.6), [Seg 19 F2](CODE_REVIEW_SEGMENT_19_CUSTOMER_REALTIME.md#f2--high--reconnect-is-a-fixed-5s-no-jitter-no-exponential-no-maxattempt-cap), [Seg 19 F3](CODE_REVIEW_SEGMENT_19_CUSTOMER_REALTIME.md#f3--high--no-state-reconciliation-on-reconnect-events-missed-during-the-disconnect-window-are-lost).

Reconnect is fixed 5s with no jitter, no exponential, no cap — every connected client thunders the backend in lockstep after an outage. On reconnect, no state re-fetch — events missed during the disconnect window (e.g. "your food is ready") are gone forever.

**Fix shape:** exponential backoff with jitter (200ms → 30s max), per-tab attempt cap, on connect re-fetch cart + active order status + active flash deal.

### 1.14 — Admin SPA has NO SignalR client; 4 staff broadcasts hit empty rooms

**Source:** [Seg 12 F3](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f3--high--admin-spa-has-no-signalr-client-four-staff-broadcasts-have-zero-subscribers).

`grep "/hubs/"` / `HubConnection` across [frontend-admin/](frontend-admin/) returns zero matches. `OrderSubmitted`, `OrderStatusUpdated`, `OrderFinalized`, `FlashDealClaimed` all broadcast into empty rooms. The kitchen sees orders only via 5-second polling (Seg 23) — real-time order entry from a busy restaurant is a 5-second-stale view.

**Fix shape:** add a SignalR client to the admin SPA's auth-store-aware area, subscribe to the staff group via `JoinRoleGroup`, fan events into Pinia stores. Pair with 0.6 (auth + tenant-bound group join from the server).

---

## Tier 2 — MEDIUM: fix before scaling beyond first ~5 clients

### 2.1 — Authorization policies missing; in-method role checks scattered

**Source:** [Seg 03 F1](CODE_REVIEW_SEGMENT_03_STARTUP.md#f1--low--no-fallback-authorization-policy--an-unannotated-controller-defaults-to-anonymous), [Seg 08 F6](CODE_REVIEW_SEGMENT_08_ADMIN_CONTROLLERS.md#f6--low--no-named-authorization-policies-defense-in-depth-missing), [Seg 21 F4](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md#f4--medium--route-guards-check-authentication-only-not-rolemembership-orgowneronly-views-render-for-restaurantadmins), [Seg 25 Finding 1](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-1--broken-access-control-cross-tenant-gaps-are-well-defended-but-admin-authorization-relies-on-string-roles-not-policies-carried-over--medium).

`AddAuthorization()` is called with no overload. Default policy is "any authenticated user" — a future controller that forgets `[Authorize]` ships anonymous by omission. `OrganizationsController` repeats `if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();` five times. Admin SPA route guards don't check role/membership — a `RestaurantAdmin` reaching `/organization` (OrgOwner-only) renders an empty page while the API 403s.

**Fix shape:** define `RestaurantAdmin` / `OrgOwner` policies in `Program.cs`, set `FallbackPolicy = RequireAuthenticatedUser()`. Apply per-controller. Replicate the same checks in Vue Router guards.

### 2.2 — Status strings free-form with no DB check constraint

**Source:** [Seg 07 F2](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f2--medium--orderstatus-paymentstatus-flashdealofferstatus-invitationstatus-are-free-form-strings-with-no-db-check-constraint).

`Order.Status`, `Order.PaymentStatus`, `FlashDealOffer.Status`, `Invitation.Status` are all `text` columns with no `CHECK`. Eleven literal writers scattered across controllers/services. `AdminController.ActiveStatuses` is the only allow-list and it's private to one class.

**Fix shape:** C# enum + EF `HasConversion<string>()` on each, OR a `CHECK status IN (...)` constraint per migration. Either closes the gap.

### 2.3 — `(restaurant_id, order_number)` not unique; SELECT-MAX/INSERT race

**Source:** [Seg 06 F3](CODE_REVIEW_SEGMENT_06_DATA_MIGRATIONS.md#f3--medium--orders-restaurant_id-order_number-is-not-unique-and-the-assigner-is-read-modify-write).

Two simultaneous order submits at the same restaurant both see `MAX = N`, both write `N+1`. Customer screens and kitchen kanban then show duplicate `#N+1` rows. The code comment at [CustomerController.cs:50-53](backend/RestaurantApi/Controllers/CustomerController.cs#L50-L53) acknowledges the race deliberately to avoid 500s — but a retry loop solves it cleanly.

**Fix shape:** promote the index to unique. Wrap the SELECT-MAX/INSERT in a retry-on-`DbUpdateException` loop (limit ~3 retries).

### 2.4 — Missing indexes on `order_items.restaurant_id` and `flash_deal_offers.restaurant_id`

**Source:** [Seg 06 F2](CODE_REVIEW_SEGMENT_06_DATA_MIGRATIONS.md#f2--medium--order_items-and-flash_deal_offers-have-no-index-on-restaurant_id-despite-the-query-filter).

Both tables implement `ITenantScoped`, both have `HasQueryFilter` on `RestaurantId`, neither has an index on `restaurant_id`. Every read translates to `WHERE restaurant_id = $tenant` against a non-indexed column. Admin metrics endpoints `CountAsync` the entire table on every dashboard load.

**Fix shape:** `e.HasIndex(x => x.RestaurantId)` on both. New migration.

### 2.5 — No CI vulnerability audit

**Source:** [Seg 02 F4](CODE_REVIEW_SEGMENT_02_CI_WORKFLOWS.md#finding-4--no-vulnerability-audit-step-in-any-workflow-medium--same-as-segment-25-f6), [Seg 25 Finding 6](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-6--vulnerableoutdated-components-no-audit-step-in-ci-medium).

No workflow runs `dotnet list package --vulnerable`, `npm audit`, or `pip-audit`. A CVE'd transitive bump merges silently.

**Fix shape:** three one-line CI steps; ~10 s each.

### 2.6 — No CSP header / no security headers / no correlation IDs

**Source:** [Seg 21 F1](CODE_REVIEW_SEGMENT_21_ADMIN_AUTH.md#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--), [Seg 25 Finding 9](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-9--logging--monitoring-no-correlation-ids-across-services-sensitive-fields-hygiene-is-ok-but-unmechanised-medium), [Seg 25 Finding 11](CODE_REVIEW_SEGMENT_25_SECURITY.md#finding-11--xss--csp-vue-escapes-by-default-no-v-html-no-csp-header-carried-over--medium), [Seg 10 F8](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f8--low--logging-does-not-include-restaurantid-or-correlationid-via-beginscope).

Already named in 1.2. Pair with a `CorrelationIdMiddleware` so request ID flows through .NET API → AI service → SPA, otherwise debugging cross-service incidents is forensics.

### 2.7 — No healthcheck on backend-api and no `/healthz` endpoint

**Source:** [Seg 03 F2](CODE_REVIEW_SEGMENT_03_STARTUP.md#f2--low--no-health-check-endpoint), [Seg 01 F4](CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md#finding-4--no-healthcheck-on-backend-api-no-health-endpoint-either-medium).

No `AddHealthChecks`, no `MapHealthChecks`, no compose `healthcheck:` block. Orchestrators have no liveness signal beyond "process is running"; a deadlocked API is invisible.

**Fix shape:** `AddHealthChecks().AddDbContextCheck<AppDbContext>()`; map `/healthz/live` and `/healthz/ready` anonymously; compose healthcheck calls `/healthz/live`.

### 2.8 — Frontend Dockerfiles run `npm run dev`; no prod build path

**Source:** [Seg 01 F5](CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md#finding-5--frontend-containers-run-npm-run-dev-no-prod-build-path-medium-cross-cutting-with-finding-1).

Both `frontend-admin/Dockerfile` and `frontend-customer/Dockerfile` end with `CMD ["npm", "run", "dev"]`. There is no `npm run build` + static-serve stage. The whole container story is dev-only.

**Fix shape:** multi-stage Dockerfile: build stage runs `npm ci && npm run build`, runtime stage is `nginx:alpine` (or `caddy`) serving `/usr/share/nginx/html`. Separate `Dockerfile.dev` for the live-reload path.

### 2.9 — Customer SPA: `shared/` not mounted in Docker

**Source:** [Seg 24 F3](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md#finding-3--customer-spa-is-missing-the-shared-volume-mount-live-dev-should-be-failing-in-docker-high), [Seg 01 F7](CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md#finding-7--shared-is-not-mounted-into-frontend-customer-despite-the-spa-importing-from-shared-high--same-as-segment-24-f3).

`docker-compose.yml` mounts `./shared` into `frontend-admin` but not `frontend-customer`. Customer SPA imports from `@shared/types/...` in 9+ files. Either Docker dev for the customer app is silently broken, or developers only run it on the host.

**Fix shape:** add `./shared:/shared:ro` to `frontend-customer.volumes`. One line.

### 2.10 — Object URL leak in menu-import upload

**Source:** [Seg 22 F5](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-5--object-urls-for-image-previews-leak-never-revoked-medium).

`URL.createObjectURL(file)` in `StepUploadPhotos.thumbUrl` runs on every render; `URL.revokeObjectURL` is never called anywhere in `frontend-admin/src`. Each call pins the underlying `File` in memory.

**Fix shape:** compute the URLs once in a `Map`, revoke in `removeFile` and `onBeforeUnmount`.

### 2.11 — Menu-import wizard: state lost on refresh; step-back wipes Step-3 edits

**Source:** [Seg 22 F1](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-1--wizard-state-is-wiped-on-refresh--navigation-without-warning-high), [Seg 22 F2](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-2--going-back-to-step-2-and-forward-again-wipes-step-3-edits-high), [Seg 22 F4](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-4--bulk-create-error-response-missingcategoryids-parsed-but-never-rendered-medium).

`onBeforeUnmount` calls `$reset()`; navigating away or refreshing loses every parsed-from-AI line and edit. Going back to Step 2 and forward again rebuilds `reviewItems` from scratch — silently discards manual fixes. `missingCategoryIds` is parsed by the API client but never rendered.

**Fix shape:** (a) only rebuild `reviewItems` when empty in `advanceToReview`; (b) `beforeunload` warning + `onBeforeRouteLeave` guard while wizard is in progress; (c) render `missingCategoryIds` inline in Step 3 with remap UX.

### 2.12 — Active orders polls every 5 s, even on hidden tabs

**Source:** [Seg 23 F1](CODE_REVIEW_SEGMENT_23_ADMIN_ORDERS_METRICS_AI.md#finding-1--active-orders-board-polls-every-5-s-no-signalr-no-visibility-aware-throttling-medium).

`ActiveOrdersBoardView` polls every 5 s with no `visibilitychange` gate. Metrics polls every 30 s, same. Concurrent edits fight for 5 s before reconciling.

**Fix shape:** gate polling on `document.visibilityState === 'visible'`. Long-term: replace with SignalR (1.14).

### 2.13 — AI directive submission has no preview-before-save

**Source:** [Seg 23 F2](CODE_REVIEW_SEGMENT_23_ADMIN_ORDERS_METRICS_AI.md#finding-2--ai-directive-submission-has-no-preview-before-save-medium).

`handleNewPrompt` posts the prompt straight to the API; parsed rules render only after persistence. Every bad parse costs another paid LLM call.

**Fix shape:** add a "Preview" step that runs the parse against the prompt without saving; persist on a second click.

### 2.14 — Backend timestamps use browser timezone, not restaurant timezone

**Source:** [Seg 23 F4](CODE_REVIEW_SEGMENT_23_ADMIN_ORDERS_METRICS_AI.md#finding-4--times-are-rendered-with-browser-localetimezone-not-restaurant-timezone-medium).

`Restaurant.Timezone` exists on the model but is never threaded through `Intl.DateTimeFormat`. An admin in London reviewing today's orders sees timestamps two hours behind the restaurant's reality.

**Fix shape:** thread `restaurant.timezone` through `useAuthStore`, build per-restaurant `Intl.DateTimeFormat({ timeZone: ... })`.

### 2.15 — Shared `DailyMetrics` missing `flashDealRevenue` + admin order endpoints typed `any[]`

**Source:** [Seg 24 F1](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md#finding-1--dailymetrics-is-missing-flashdealrevenue-which-the-backend-returns-and-the-ui-consumes-high), [Seg 24 F2](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md#finding-2--admin-api-client-uses-any-for-orders-shared-order-shape-doesnt-match-what-the-api-returns-high).

Backend returns `flashDealRevenue`, frontend consumes it, shared type does not declare it. Three admin order endpoints typed `any[]`. The shared `Order`/`OrderItem` shape doesn't match what the admin actually receives (nested `product:Pick<...>` vs flat `productName`/`unitPrice`/`subtotal`).

**Fix shape:** add `flashDealRevenue: number` to `DailyMetrics`. Promote local admin `Order`/`OrderItem` interfaces to `shared/` and replace `any[]` with concrete types in `api-client.ts`.

### 2.16 — `api-contract-ai.json` describes a dead API

**Source:** [Seg 24 F4](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md#finding-4--api-contract-aijson-describes-an-api-that-no-longer-exists-high), [Seg 02 F2](CODE_REVIEW_SEGMENT_02_CI_WORKFLOWS.md#finding-2--contracts-workflow-only-checks-json-syntax-the-actual-contract-drift-is-unguarded-high).

Every endpoint path documented is out of date. Field casing is snake_case in the contract, camelCase on the wire. The contracts CI workflow only JSON-parses the file — it doesn't verify accuracy.

**Fix shape:** delete the file and replace with a README pointing at `/swagger/v1/swagger.json`. Or regenerate from Swagger on each backend release.

### 2.17 — No idempotency on order POST (separate from rate limit)

Covered under 1.7 above — listed here for completeness.

---

## Tier 3 — recommended but not strictly blocking

These are recorded so they aren't forgotten. They show up in the per-segment reports and should be backlogged.

- **Customer SPA: no global error handler, no toast system** ([Seg 18 F5](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern)).
- **`fetcher` has no timeout/AbortController** ([Seg 18 F4](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f4--medium--fetcher-has-no-timeout-or-abortcontroller-a-hung-request-hangs-the-ui-forever)).
- **`console.log` not stripped in Vite build** ([Seg 18 F3](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f3--medium--consolelog-not-stripped-in-vite-build-no-no-console-lint-rule)).
- **AI client failures inconsistently mapped** ([Seg 10 F4-F5](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f4--medium--aiservice-httpclient-has-no-retry-policy-and-no-circuit-breaker)).
- **Inline `:style="background-image: url(${imageUrl})"` CSS-injection vector** ([Seg 22 F9](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-9--inline-stylebackground-image-urlimageurl-allows-css-injection-medium)).
- **`menuImportApi` bypasses 401-refresh** ([Seg 22 F10](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-10--menuimportapi-bypasses-the-authedfetcher-401-refresh-path-medium)).
- **Native `window.confirm` for rule deletion** ([Seg 23 F3](CODE_REVIEW_SEGMENT_23_ADMIN_ORDERS_METRICS_AI.md#finding-3--directive-deletion-uses-native-windowconfirm-not-a-styled-modal-low)).
- **`useDirectivesStore` swallows errors silently** ([Seg 23 F7](CODE_REVIEW_SEGMENT_23_ADMIN_ORDERS_METRICS_AI.md#finding-7--usedirectivesstorefetchdirectives-and-toggleactive-swallow-errors-silently-medium)).
- **Wizard upload has no progress, no per-file retry** ([Seg 22 F3](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-3--upload-has-no-progress-per-file-retry-impossible-medium)).
- **Long menu lists not virtualized** ([Seg 22 F6](CODE_REVIEW_SEGMENT_22_MENU_MANAGEMENT.md#finding-6--no-virtualisation--pagination-on-category--product-lists-medium)).
- **`OrderFinalizationService` uses `Task.Delay` not `PeriodicTimer`** ([Seg 10 F3](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f3--medium--orderfinalizationservice-sleeps-with-taskdelay-in-a-whiletrue-instead-of-periodictimer-)).
- **`SimilarityService` is dead code; `/similarity/auto-generate` skips validation** ([Seg 10 F6](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation), [Seg 16 F2](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation)).
- **Backend Docker `:/src` bind mount is dead code** ([Seg 01 F2](CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md#finding-2----backendrestaurantapisrc-bind-mount-is-dead-code-medium)).
- **No non-root user in any Dockerfile** ([Seg 01 F6](CODE_REVIEW_SEGMENT_01_COMPOSE_TOPOLOGY.md#finding-6--no-non-root-user-in-any-dockerfile-low)).
- **i18n: no `Intl.NumberFormat` / `Intl.DateTimeFormat` in customer SPA** ([Seg 20 F2](CODE_REVIEW_SEGMENT_20_CUSTOMER_I18N.md#f2--high--no-intlnumberformat-prices-render-as-raw-js-numbers-249-ron-not-2490-ron)).
- **Dead components with hardcoded RO text in `components/`** ([Seg 20 F1](CODE_REVIEW_SEGMENT_20_CUSTOMER_I18N.md#f1--high--four-orphan-components-still-contain-hardcoded-romanian-text-cartvue--menulistvue--flashdealbannervue--recommendationwidgetvue)).
- **PDFs not page-split in vision extractor; Claude path passes `application/pdf` as image** ([Seg 15 F1](CODE_REVIEW_SEGMENT_15_VISION.md#f1--high--pdfs-are-not-page-split-and-the-claude-path-passes-applicationpdf-as-an-image-source-)).
- **Unbounded `asyncio.gather` over per-page extractions** ([Seg 15 F2](CODE_REVIEW_SEGMENT_15_VISION.md#f2--high--unbounded-asynciogather-over-per-page-extractionsno-asynciosemaphore-)).
- **Tests missing for 4 of 5 AI routers; 2 of 3 engines** ([Seg 17 F1](CODE_REVIEW_SEGMENT_17_AI_TESTS.md#f1--high--four-of-five-real-routers-have-zero-tests-only-menuextract-is-covered-), [Seg 17 F2](CODE_REVIEW_SEGMENT_17_AI_TESTS.md#f2--high--two-of-three-live-engines-recommendation-similarity-have-zero-unit-tests)).
- **`directive_parser.py` is unreachable dead code** ([Seg 16 F1](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-)).

---

## Quick scoreboard

| Tier | Items | What it costs to skip |
|---|---|---|
| 0 — CRITICAL | 9 | Active vulns (session takeover, cross-tenant leak, unauth SignalR), undeployable to prod (secrets), zero test signal. |
| 1 — HIGH | 14 | Open security paths (rate limit, logout, SMTP), data-loss bugs (cart, idempotency, flash deal restart), broken admin realtime + cart sync. |
| 2 — MEDIUM | 17 | Operational debt that bites at 5-10 clients: missing policies, indexes, CSP, healthchecks, prod-build path, contract drift. |
| 3 — TRACKED | 18 | Backlog. No blocker individually but a steady stream of small papercuts. |

## Suggested batching

Six PRs is the minimum to be safe; each unlocks the next:

1. **Identity hardening.** Items 0.1, 0.2, 1.3, 1.4 — refresh-token hashing, confirm-email fix, logout endpoint, theft detection. One auth-shaped migration, one controller change set, one frontend change set. Without this, every other change is built on a broken auth substrate.
2. **Tenancy + customer-surface auth.** Items 0.3, 0.4, 0.5, 0.6 — `SaveChanges` modified-guard, QR-token re-binding, flash-deal ownership, SignalR auth + tenant-bound group join. Closes every named 🔥 invariant in one batch.
3. **Infra + ship pipeline.** Items 0.7, 0.8, 0.9, 1.1, 2.7, 2.8 — prod compose, fix CI, SMTP guard, rate limits, healthcheck, prod frontend Dockerfiles. Required to even *deploy* safely; required for every subsequent fix to have CI signal.
4. **Order + input safety.** Items 1.7, 1.10, 1.11, 1.9 — quantity/availability/idempotency on POST /orders, bulk-cap, AI failure mapping, MIME + page-count guards. Closes the rest of the customer-and-admin write surface.
5. **Realtime + cart correctness.** Items 1.8, 1.12, 1.13, 1.14, 2.12 — flash-deal expiry into hosted service, cart persistence + sync error UI, WS reconnect backoff + reconciliation, admin SignalR client, visibility-gated polling. Closes the live-experience bugs.
6. **Contracts + ops polish.** Items 1.2/1.5 (CSP/coalesced refresh), 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.9-2.16 — defense in depth, schema integrity, dependency audits, drift cleanup.

After (1)-(3), the stack is safe to demo to design-partner clients under real names. After (4)-(5), it's safe to take payment for a single restaurant. (6) is the runway for scaling.

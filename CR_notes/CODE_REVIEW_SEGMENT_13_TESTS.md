# Segment 13 — Backend tests (`RestaurantApi.Tests`): review findings

Scope review against the [Segment 13 checklist](CODE_REVIEW_ROADMAP.md#segment-13--backend-tests-restaurantapitests) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). The project is six `*.cs` files under [backend/RestaurantApi.Tests/](backend/RestaurantApi.Tests/): [BulkCreateProductsTests.cs](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs), [MenuExtractionClientTests.cs](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs), [MenuScanControllerTests.cs](backend/RestaurantApi.Tests/MenuScanControllerTests.cs), [ProductValidatorTests.cs](backend/RestaurantApi.Tests/ProductValidatorTests.cs), [SanityTest.cs](backend/RestaurantApi.Tests/SanityTest.cs), and the shared [Helpers/TestWebApplicationFactory.cs](backend/RestaurantApi.Tests/Helpers/TestWebApplicationFactory.cs). The CI surface that's supposed to run them is [.github/workflows/backend.yml](.github/workflows/backend.yml).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**7 findings, three of them HIGH and one of them CRITICAL.** The headline is that the test suite is not running anywhere — CI is silently green ([F1](#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-)), and if you ran it locally on `main` today, every integration test would fail because the multi-tenant refactor in `830f123` rewrote every endpoint route from `api/admin/...` to `api/restaurants/{restaurantId:guid}/admin/...` and added `[Authorize]`, but the tests still POST to the pre-refactor URLs with no auth header ([F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-)). The two HTTP-driven test fixtures haven't been refreshed since 2026-05-19 — the same day the refactor landed — and the CI bug means nobody noticed.

The other six findings are coverage- and isolation-shaped, listed in roadmap order:

1. **No coverage for the 🔥 areas.** [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) — there is no test exercising multi-tenancy negative cases (the roadmap's named gap from segment 04), no test for any AuthController endpoint (segment 05), no SignalR hub test (segment 12), and no test for `FlashDealService` (segment 10). The eight tests we have cover exactly two endpoints (`POST /api/admin/menu/products/bulk` and `POST /api/admin/menu/scan`) — both of which are unreachable post-refactor.
2. **TestWebApplicationFactory leaks state across fixtures.** [F4](#f4--medium--testwebapplicationfactory-does-not-isolate-the-database-per-test-or-clean-it-between-them) — the four `BulkCreateProductsTests` share one Postgres container; `db.Database.MigrateAsync()` is called from `InitializeAsync` but nothing resets data between tests. The two tests that assert counts (`(await db.Products.CountAsync()).Should().Be(0)`, line 99) survive only because xUnit creates a new fixture per test class, but inside the class they're order-dependent and would fail under xUnit's default per-method test instance plus shared container.
3. **AI mocks cover success and 503 only.** [F5](#f5--medium--wiremock-coverage-misses-the-roadmap-listed-failure-modes-timeout-malformed-json) — the roadmap names "success, timeout, 500, malformed JSON"; `MenuExtractionClientTests` covers success and 503, `MenuScanControllerTests` covers success and an unreachable backend. No timeout test (`Task.Delay` in WireMock), no malformed-JSON test, no slow-trickle test.
4. **`ProductValidatorTests` boundary coverage is uneven.** [F6](#f6--low--productvalidatortests-boundary-coverage-is-uneven) — name has empty/whitespace/too-long, price has `0 / -1 / 99999999.999 / 100000000`. Description has only `null` and `1001 chars`. Unicode names, NaN-decimal, leading/trailing whitespace on price strings, and emoji-laden inputs are absent. The "property-based or at least covers boundary values (zero price, very long name, unicode)" item passes for unicode-in-name but not the others.
5. **No assertion that the bulk endpoint enforces auth or tenancy.** [F7](#f7--low--bulk-tests-also-skip-the-authorize-401-and-cross-tenant-403-cases-even-if-the-routes-were-fixed) — even when the routes get fixed, the BulkCreateProductsTests has no test that an unauthenticated request returns 401, no test that an auth'd user without membership in the target restaurant returns 403, and no test that a mixed-tenant payload is rejected (the segment 08 checklist item).

What does check out is the foundation: the project picks the right libraries (Testcontainers Postgres ✓, WireMock.Net ✓, FluentAssertions ✓, xUnit ✓; [RestaurantApi.Tests.csproj:10-16](backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj#L10-L16)), uses `IAsyncLifetime` correctly for per-class lifecycle ([BulkCreateProductsTests.cs:19-53](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L19-L53)), boots the API via the `WebApplicationFactory<Program>` integration pattern ([TestWebApplicationFactory.cs:12-34](backend/RestaurantApi.Tests/Helpers/TestWebApplicationFactory.cs#L12-L34)), and the WireMock fixture in `MenuExtractionClientTests` is the cleanest test in the project — it's the one that doesn't go through the broken HTTP layer. `SanityTest.cs` exists, runs, and proves the test runner itself is healthy ([SanityTest.cs:6-12](backend/RestaurantApi.Tests/SanityTest.cs#L6-L12)) — the irony is that it's the only test that *can* actually pass on the current branch.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| `TestWebApplicationFactory` uses Testcontainers Postgres (real DB) — not in-memory — so EF behaviors match prod | ✓ Pass | `Testcontainers.PostgreSql` 3.10.0 ([RestaurantApi.Tests.csproj:14](backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj#L14)); `BulkCreateProductsTests.InitializeAsync` spins up `postgres:16-alpine` and wires the connection string into the factory ([line 21-44](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L21-L44)). The factory replaces the `DbContextOptions<AppDbContext>` correctly ([TestWebApplicationFactory.cs:30-33](backend/RestaurantApi.Tests/Helpers/TestWebApplicationFactory.cs#L30-L33)). |
| One end-to-end test per controller covering: happy path, cross-tenant attempt → 403/404, invalid input → 400 | ❌ Fail — see [F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-), [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) | Eight controllers ([Controllers/](backend/RestaurantApi/Controllers/)). Tests touch *two* endpoints, both via stale URLs. Auth, organisations, tables, menu, customer, admin — no coverage. Cross-tenant 403 — no coverage anywhere. |
| WireMock.Net mocks of the AI service cover: success, timeout, 500, malformed JSON | ⚠ Partial — see [F5](#f5--medium--wiremock-coverage-misses-the-roadmap-listed-failure-modes-timeout-malformed-json) | Success ✓ ([MenuExtractionClientTests.cs:37-69](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs#L37-L69)), 503 ✓ ([line 71-83](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs#L71-L83)). Timeout ❌. 500 ❌. Malformed JSON ❌. |
| Tests are deterministic (no `DateTime.UtcNow` in assertions without injection; no random ordering) | ✓ Pass | No assertions reference clock-derived values. The new-category-name normalisation test ([BulkCreateProductsTests.cs:117-132](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L117-L132)) is the closest thing to a fragile assertion and it's data-shaped, not time-shaped. |
| Tests run in parallel safely (separate databases or transaction-rollback per test) | ❌ Fail — see [F4](#f4--medium--testwebapplicationfactory-does-not-isolate-the-database-per-test-or-clean-it-between-them) | Inside `BulkCreateProductsTests`, the four `[Fact]`s share state — `InitializeAsync` seeds one Category but never resets between tests. The assertions in `Validation_failure_on_one_item_rolls_back_everything` (line 98 expects `Be(1)`) and `New_category_with_existing_name_is_merged_into_existing` (line 129 expects `Be(1)`) both assume one-shot fresh state, and would interfere if xUnit decided to share an instance — `IClassFixture` would expose this immediately. |
| `BulkCreateProductsTests` covers partial-failure and exact-error response shape | ✓ Pass (in theory; tests don't run) | Three substantive tests: validation-failure rollback ([line 81-100](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L81-L100)), unknown category 409 with `missingCategoryIds` shape ([line 102-114](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L102-L114)), and merge-by-name ([line 116-132](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L116-L132)). The assertions are well-shaped; only the routes are wrong (see [F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-)). |
| `ProductValidatorTests` is property-based or at least covers boundary values (zero price, very long name, unicode) | ⚠ Partial — see [F6](#f6--low--productvalidatortests-boundary-coverage-is-uneven) | Empty/whitespace/121-char name ✓. Zero/negative/over-cap price ✓ ([line 36-43](backend/RestaurantApi.Tests/ProductValidatorTests.cs#L36-L43)). Unicode in name ✓ ("Ciorbă de burtă", [line 30](backend/RestaurantApi.Tests/ProductValidatorTests.cs#L30)). 1001-char description ✓. But no property-based generator, no decimal-precision (currency-rounding) test, no leading-trailing-whitespace-on-price test, no maximum-decimals test. |
| Auth lifecycle tests (refresh reuse, revoke chain) (roadmap "Gaps likely") | ❌ Fail — see [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) | Grep `Authcontroller`, `refresh`, `login`, `confirm-email` across [backend/RestaurantApi.Tests/](backend/RestaurantApi.Tests/) returns zero matches. |
| Multi-tenancy negative tests (roadmap "Gaps likely") | ❌ Fail — see [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) | Grep `Tenant`, `Membership`, `Restaurant.*Id`, cross-tenant, etc. returns no matches in the test project. |
| SignalR hub tests (`TestServer` + SignalR client) (roadmap "Gaps likely") | ❌ Fail — see [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) | Grep `Hub`, `SignalR`, `HubConnection` returns no matches. The findings of [Segment 12](CODE_REVIEW_SEGMENT_12_SIGNALR.md) (anonymous CustomerHub, client-supplied `restaurantId`, dead admin broadcasts) are exactly the kind of regressions a per-hub test would have surfaced. |

---

## F1 — CRITICAL — CI runs `dotnet test` from the web project's directory and discovers no tests; the suite is silently green 🔥

[.github/workflows/backend.yml:13-26](.github/workflows/backend.yml#L13-L26):

```yaml
jobs:
  dotnet:
    name: C# ASP.NET Core
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: backend/RestaurantApi
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release --verbosity normal
```

The job sets `working-directory: backend/RestaurantApi` and runs `dotnet test` from there. [backend/RestaurantApi/RestaurantApi.csproj](backend/RestaurantApi/RestaurantApi.csproj) is `<Project Sdk="Microsoft.NET.Sdk.Web">` — a web SDK, not a test SDK. It has no `xunit` reference and no `<IsTestProject>true</IsTestProject>`. `dotnet test` invoked there finds zero test projects, prints something like *"No test is available in /home/runner/work/.../RestaurantApi.dll"*, and **exits 0**.

The test project is one directory up the tree at [backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj](backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj) (`<IsTestProject>true</IsTestProject>`, [line 6](backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj#L6)). The job never reaches it. Confirmation:

- `backend/RestaurantApi/RestaurantApi.csproj` lists `Microsoft.AspNetCore.SignalR`, `Npgsql.EntityFrameworkCore.PostgreSQL`, EF Core Design, ASP.NET Identity, JwtBearer, Swashbuckle, MailKit — none of `xunit`, `Microsoft.NET.Test.Sdk`, or `Testcontainers.*` ([RestaurantApi.csproj:9-21](backend/RestaurantApi/RestaurantApi.csproj#L9-L21)).
- The Python job for comparison runs `working-directory: backend/ai-scripts` and calls `pytest` correctly ([backend.yml:30-43](.github/workflows/backend.yml#L30-L43)) — that one works.
- The job's `paths: ['backend/**']` filter triggers the workflow even when the tests change, so the build/restore/test steps "run," but `dotnet test` finds nothing to do.

This invalidates several conclusions Segment 02 ("CI workflows & build hygiene") would otherwise reach: the dotnet check is non-existent in practice. Every PR has merged into `main` since the workflow was authored on 2026-03-22 without one assertion being evaluated. Coupled with [F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-), the masking is total: the suite is broken *and* CI is misconfigured, so the only signal anyone would see is "all green."

The CI runner also lacks Docker-in-Docker by default, so even if the working directory were correct, `BulkCreateProductsTests` (which boots a Postgres container, [BulkCreateProductsTests.cs:21-23](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L21-L23)) would need an explicit `services:` block or DinD setup on `ubuntu-latest`. The roadmap's Segment 02 item — *"Backend workflow runs: `dotnet restore` → `build` → `test` with a real Postgres (Testcontainers needs Docker; confirm CI runner supports it)"* — was already a "confirm" call-out; the answer is no, not configured.

Two-line fix shape (out of scope): remove `working-directory: backend/RestaurantApi`, run `dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj` from the repo root. Then expect, and fix, the cascade of failures in [F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-).

---

## F2 — HIGH — All eight integration tests call pre-refactor routes; `BulkCreateProductsTests` + `MenuScanControllerTests` are stale since `830f123` 🔥

`830f123` ("All the app to be Multi Tenant", 2026-05-19) rewrote the controller routes:

```diff
- [Route("api/admin/menu")]
+ [Authorize]
+ [Route("api/restaurants/{restaurantId:guid}/admin/menu")]
```

— see [MenuManagementController.cs:13-14](backend/RestaurantApi/Controllers/MenuManagementController.cs#L13-L14) and [MenuScanController.cs:10-11](backend/RestaurantApi/Controllers/MenuScanController.cs#L10-L11). Every test still calls the old, unprefixed path with no auth:

[BulkCreateProductsTests.cs:70](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L70):

```csharp
var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
```

[MenuScanControllerTests.cs:65](backend/RestaurantApi.Tests/MenuScanControllerTests.cs#L65), [line 76, 90, 97](backend/RestaurantApi.Tests/MenuScanControllerTests.cs#L65-L97):

```csharp
var resp = await _client.PostAsync("/api/admin/menu/scan", OnePhoto());
// ...
var resp = await _client.PostAsync("/api/admin/menu/scan", OnePhoto());  // [Returns_502_when_python_unreachable]
// ...
var resp = await _client.PostAsync("/api/admin/menu/scan", form);        // [Returns_413_when_payload_exceeds_cap]
// ...
var resp = await _client.PostAsync("/api/admin/menu/scan", new MultipartFormDataContent());  // [Returns_400_when_no_photos]
```

Two compounding mismatches:

- **No route matches.** `/api/admin/menu/products/bulk` and `/api/admin/menu/scan` are now unmapped. ASP.NET returns 404. The assertions expect 200/400/409/413/502 — every assertion is wrong relative to the actual response. The first one fired (the happy-path `Be(HttpStatusCode.OK)`) hits 404 and `Should().Be(OK)` throws.
- **No auth.** `TestWebApplicationFactory` ([line 12-34](backend/RestaurantApi.Tests/Helpers/TestWebApplicationFactory.cs#L12-L34)) does not replace the JWT auth handler with a test scheme, and the test `HttpClient` ([line 37](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L37)) has no `Authorization` header set. Even if you fixed the routes by prepending `/api/restaurants/{guid}/`, every request would 401 from the `[Authorize]` on both controllers.

The two integration test files were last touched on 2026-05-20 (`3139199`, *"feat: pdf import support"*) — *after* the refactor on 2026-05-19. The author updated the test bodies for PDF support but not the URLs, so the staleness is not pre-existing-and-forgotten; it's pre-existing-and-papered-over.

`MenuExtractionClientTests` is the only HTTP-driven test that's not stale, because it constructs `MenuExtractionClient` directly against a WireMock URL ([MenuExtractionClientTests.cs:25-27](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs#L25-L27)) — it never goes through the controller routing layer, so the refactor doesn't reach it.

The cumulative effect with [F1](#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-): the suite has been broken at the routing layer for over two months in calendar time, and nobody noticed because CI silently skips it. If a developer ran `dotnet test backend/RestaurantApi.Tests/` locally today, every integration test would fail.

Fix shape (out of scope): every test client either needs a per-test `restaurantId` and the `/api/restaurants/{restaurantId}/admin/...` prefix, plus a test-scheme auth handler that injects a `RestaurantAdmin` membership claim. The `TestWebApplicationFactory` is the right home for the auth handler. Without that work, none of these tests express what they claim to express.

---

## F3 — HIGH — No tests for the roadmap's 🔥 areas: multi-tenancy, auth lifecycle, SignalR hubs, FlashDealService

The roadmap names these explicitly as "Gaps to file as follow-ups (likely missing — check)." They are missing. Concretely — grep across [backend/RestaurantApi.Tests/](backend/RestaurantApi.Tests/):

| Concern | Roadmap segment | Touched in tests? |
|---|---|---|
| `AuthController` (register/login/refresh/refresh-reuse/confirm-email/accept-invite) | Segment 05 🔥 | No — zero references to `auth`, `refresh`, `login`, `confirm-email`. |
| Multi-tenancy enforcement (cross-tenant 403 from every admin endpoint) | Segment 04 🔥 | No — zero references to `Membership`, `Tenant`, cross-tenant payloads. |
| `CustomerHub` / `StaffHub` event delivery, group isolation | Segment 12 🔥 | No — zero references to `Hub`, `HubConnection`, `SignalR`. |
| `FlashDealService.ClaimDealAsync` (race + concurrency, segment 10 F2) | Segment 10 🔥 | No — zero references to `FlashDeal`. |
| `OrderFinalizationService` poll/finalize cycle | Segment 10 | No — zero references to `Finalization`, `Hosted`. |
| `OrganizationsController` invite/revoke flow | Segment 08 | No coverage. |
| `MenuController` (customer-side) | Segment 09 | No coverage. |
| `CustomerController.SubmitOrder` (order placement, idempotency) | Segment 09 🔥 | No coverage. |

The two endpoints that *are* covered ([F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-)) are the two that touch the menu-import AI flow — i.e., the area where the test author was actively working when they wrote them. Every higher-stakes surface in the codebase has zero tests.

This is the worst gap for the project's stability story:

- The Segment 12 findings (anonymous CustomerHub, client-supplied tenant IDs) are precisely the kind of regression a `JoinTableGroup("<wrong-restaurant-guid>", "...")` test would have caught.
- The Segment 10 finding F2 (check-then-write race in `ClaimDealAsync`) is a `Parallel.ForEachAsync` test away from being demonstrated.
- The Segment 05 work — refresh-token rotation, single-use semantics — has no acceptance test, so any future change is going to land blind.

The minimal table-stakes coverage to address the 🔥 segments (and not in scope for this review): one cross-tenant negative integration test per admin controller, one full auth-lifecycle scenario test (register → confirm → login → refresh → refresh-reuse → logout → refresh-after-logout), and one hub-level integration test that exercises `JoinTableGroup` and asserts the broadcast surface.

---

## F4 — MEDIUM — `TestWebApplicationFactory` does not isolate the database per test or clean it between them

[TestWebApplicationFactory.cs:12-34](backend/RestaurantApi.Tests/Helpers/TestWebApplicationFactory.cs#L12-L34):

```csharp
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public string PostgresConnectionString { get; set; } = string.Empty;
    public string PythonAiBaseUrl { get; set; } = "http://localhost:0";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = PostgresConnectionString,
                ["AiService:BaseUrl"] = PythonAiBaseUrl,
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));
        });
    }
}
```

[BulkCreateProductsTests.cs:29-46](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L29-L46):

```csharp
public async Task InitializeAsync()
{
    await _pg.StartAsync();
    _factory = new TestWebApplicationFactory { ... };
    _client = _factory.CreateClient();

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    var cat = new Category { Id = Guid.NewGuid(), Name = "Aperitive", SortOrder = 0 };
    db.Categories.Add(cat);
    await db.SaveChangesAsync();
    _existingCategoryId = cat.Id;
}
```

Two distinct problems:

- **No per-test reset.** `InitializeAsync` runs once per test class instance. xUnit's default is one instance *per test method* — so each `[Fact]` does get a fresh container and a fresh seed. That works today, but it's wasteful (a 500 ms - 1 s container spin-up per test) and it doesn't run in parallel because xUnit serialises tests inside a class. The "tests run in parallel safely" roadmap item is technically satisfied by accident: the database isn't shared, so there's no interference. The faster, more correct shape is one container per *class* (via `IClassFixture`) with a per-test transaction rollback or `TRUNCATE`.
- **No verifiable invariant on connection-string uniqueness.** `MenuScanControllerTests` ([line 31](backend/RestaurantApi.Tests/MenuScanControllerTests.cs#L31)) passes a literal `"Host=localhost;Database=ignored;Username=x;Password=x"` because it doesn't need the DB. The factory accepts it and tries to `UseNpgsql` against it. The tests work only because the controller bug ([F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-)) means the request never reaches an EF query — once the routes are fixed, the scan tests would crash on first DB access instead of returning the expected `OK`/`502`/`413`/`400`.

The `TestWebApplicationFactory` also doesn't substitute an auth handler (the [F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-) blocker) and doesn't substitute the SignalR transport (the [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) blocker). Both are the kind of test-infrastructure work that belongs here, not in individual test files.

---

## F5 — MEDIUM — WireMock coverage misses the roadmap-listed failure modes: timeout, malformed JSON

The roadmap names "success, timeout, 500, malformed JSON" as the four shapes a `MenuExtractionClient` test surface should cover.

[MenuExtractionClientTests.cs](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs) covers two:

- Success ([line 37-69](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs#L37-L69)) — well-shaped happy path with a representative JSON payload.
- 503 ([line 71-83](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs#L71-L83)) — single non-success status, asserts `HttpRequestException` is thrown.

[MenuScanControllerTests.cs:73-78](backend/RestaurantApi.Tests/MenuScanControllerTests.cs#L73-L78) covers a fourth via a different mechanism (Python stopped → connection refused). 500 specifically and malformed-JSON are both missing.

The missing tests matter for two reasons:

- **Timeout.** Segment 10 F4 names the `MenuExtractionClient` timeout configuration (6 min for the typed client, [Program.cs:127](backend/RestaurantApi/Program.cs#L127)) as an unvalidated assumption. A WireMock test using `Response.Create().WithDelay(TimeSpan.FromMinutes(...))` and a `CancellationTokenSource(timeout)` is the right place to assert the deadline is honoured.
- **Malformed JSON.** Segment 16 of the roadmap covers the AI service's structured-output enforcement, but the C# side has its own JSON-deserialisation path (`System.Text.Json` defaults, `JsonNamingPolicy.CamelCase` from [Program.cs:138-139](backend/RestaurantApi/Program.cs#L138-L139)). A response with the wrong shape — say, `items` as a string instead of an array, or a missing required field — should fail loudly. There's no test that proves it does.

`MenuScanController` is the controller meant to translate AI failures into a user-visible 502 with a localised message ([MenuScanController.cs:64-69](backend/RestaurantApi/Controllers/MenuScanController.cs#L64-L69)). Today only the connection-refused path is exercised; a malformed-JSON path probably blows up with a `JsonException` 500 — the test that would prove this works was never written.

---

## F6 — LOW — `ProductValidatorTests` boundary coverage is uneven

[ProductValidatorTests.cs](backend/RestaurantApi.Tests/ProductValidatorTests.cs) handles the obvious axes well:

- Name: `null` / `""` / `"   "` / 121-char string ([line 9-25](backend/RestaurantApi.Tests/ProductValidatorTests.cs#L9-L25)).
- Name happy path trims whitespace and accepts unicode ("Ciorbă de burtă", [line 28-33](backend/RestaurantApi.Tests/ProductValidatorTests.cs#L28-L33)).
- Price: `0`, `-1`, `99999999.999`, `100000000` ([line 36-43](backend/RestaurantApi.Tests/ProductValidatorTests.cs#L36-L43)).
- Description: `null`, 1001 chars ([line 52-61](backend/RestaurantApi.Tests/ProductValidatorTests.cs#L52-L61)).

Missing axes the roadmap implies ("property-based or at least covers boundary values"):

- Maximum decimal precision for `Price` — the segment 06 roadmap item is "`decimal` precision is set explicitly for `Price`, `Total`, `SimilarityRules.Score`". A price of `12.999` or `12.9999999999999999` should round / reject; no test asserts which.
- Surrogate-pair / RTL / emoji name input ("🍕 Margherita", "‏السلام", "\u{1F37A}") — none of which are tested. Segment 20 (i18n) implies these will reach `Product.Name` in practice.
- Property-based generation with `FsCheck` / `CsCheck` — the roadmap explicitly says "or at least covers boundary values," so the boundary fallback is acceptable, but the property-based form would catch the unicode and precision gaps for free.
- `ValidatePrice` with `decimal.MaxValue` / `decimal.MinValue` — to confirm overflow-safe behaviour.

The validators tested are *correct* for the cases that are covered; the concern is whether the test set is representative. For now it isn't, but the gap is small.

---

## F7 — LOW — Bulk tests also skip the `[Authorize]` 401 and cross-tenant 403 cases, even if the routes were fixed

Even after [F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-) is repaired, the bulk-create tests still won't reach the segment 08 checklist items. The roadmap calls for:

- "Every action either has `[Authorize]` ... or is explicitly `[AllowAnonymous]`." A test that POSTs without a token and asserts 401 is the matching guard.
- "Bulk endpoints (`bulk-create-products`) enforce a max batch size and reject mixed-tenant payloads." Today's tests don't probe the batch-size limit (or even verify whether one exists — segment 08's `↳` was a follow-up that's never been answered with a test), and don't exercise the mixed-tenant case at all.
- "DTOs do not bind sensitive fields (`Id`, `RestaurantId`, `CreatedAt`) from the request body." A test that posts a `BulkCreateProductItem` with a hand-set `Id` or `RestaurantId` and asserts the persisted entity ignored those values would close this.

These are extensions to the existing test class, not a new test surface — once the routing and auth blockers are gone, three more `[Fact]` methods in `BulkCreateProductsTests` would cover them.

---

## What's solid

| Item | Why it's good |
|---|---|
| Library choices ([RestaurantApi.Tests.csproj:10-16](backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj#L10-L16)) | Testcontainers ✓, WireMock.Net ✓, Mvc.Testing ✓, FluentAssertions ✓, xUnit ✓. The selection matches the roadmap's expectations precisely. |
| `IAsyncLifetime` discipline ([BulkCreateProductsTests.cs:19-53](backend/RestaurantApi.Tests/BulkCreateProductsTests.cs#L19-L53), [MenuExtractionClientTests.cs:17-34](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs#L17-L34), [MenuScanControllerTests.cs:19-44](backend/RestaurantApi.Tests/MenuScanControllerTests.cs#L19-L44)) | Async setup/teardown is implemented correctly, container is awaited on dispose, no `.Result`/`.Wait` calls. |
| Assertion style (FluentAssertions) is consistent across files | One `.Should().Be(...)` shape; readable failure messages. |
| `MenuExtractionClientTests` is the cleanest test in the project | Direct unit-style test of the client against WireMock — no controller-layer coupling, so it's the one test surface that survived the refactor. The pattern is the right shape for the rest of the AI surface. |
| `SanityTest.cs` exists | Proves the runner is alive — useful as the trivial smoke-test once CI is fixed in [F1](#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-). |
| `BulkCreateProductsTests` assertion shapes are well-chosen (in theory) | The four scenarios in the file (happy, validation-rollback, unknown-category, merge-by-name) are the right cases to exercise for the bulk endpoint. Once the routes and auth blocker are repaired, they become valuable tests. |

---

## Cross-segment follow-ups raised here

- **[Segment 02](CODE_REVIEW_ROADMAP.md#segment-02--ci-workflows--build-hygiene):** [F1](#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-) is the canonical example of why the segment-02 `↳` follow-up ("Is there a status check rule on `main` requiring all three workflows to pass before merge?") matters less than it sounds — the workflows pass, but they don't run anything. The fix lives in [.github/workflows/backend.yml](.github/workflows/backend.yml).
- **[Segment 04](CODE_REVIEW_ROADMAP.md#segment-04--multi-tenancy-enforcement):** the multi-tenancy negative-test integration described in segment 04's body ("Seed two restaurants R1 and R2 with overlapping product names. Authenticate as R1's admin. Hit every admin endpoint with R2's `restaurantId`. None should succeed.") is the missing test [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) names. It belongs here.
- **[Segment 05](CODE_REVIEW_ROADMAP.md#segment-05--authentication--token-lifecycle):** the manual curl flow in segment 05's verification commands (signup → confirm → login → refresh → refresh-old → logout → refresh-after-logout) becomes a single integration test once [F2](#f2--high--all-eight-integration-tests-call-pre-refactor-routes-bulkcreateproductstests--menuscancontrollertests-are-stale-since-830f123-) and [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice) are unblocked.
- **[Segment 08](CODE_REVIEW_ROADMAP.md#segment-08--controllers--admin-surface):** [F7](#f7--low--bulk-tests-also-skip-the-authorize-401-and-cross-tenant-403-cases-even-if-the-routes-were-fixed) is the test-layer instance of segment 08's checklist items on auth and bulk-batch limits.
- **[Segment 10](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services):** the `FlashDealService.ClaimDealAsync` race ([CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md F2](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f2--high--claimdealasync-has-a-check-then-write-race-no-transaction-no-concurrency-token)) is reproducible in a 30-line `Parallel.ForEachAsync` test against a Testcontainers Postgres — the missing test from [F3](#f3--high--no-tests-for-the-roadmaps--areas-multi-tenancy-auth-lifecycle-signalr-hubs-flashdealservice).
- **[Segment 12](CODE_REVIEW_ROADMAP.md#segment-12--signalr-hubs--realtime):** every finding in [CODE_REVIEW_SEGMENT_12_SIGNALR.md](CODE_REVIEW_SEGMENT_12_SIGNALR.md) is a candidate hub-test scenario — anonymous-connection join, client-supplied `restaurantId` cross-tenant probe, default-options DoS surface.
- **[Segment 11](CODE_REVIEW_ROADMAP.md#segment-11--email-subsystem):** [CODE_REVIEW_SEGMENT_11_EMAIL.md F2](CODE_REVIEW_SEGMENT_11_EMAIL.md#f2--high--register--resendconfirmation-always-respond-confirmation_sent-even-when-smtp-throws) (`Register` returns `confirmation_sent` even when SMTP throws) is also a missing test — a `MailKit` mock that throws, then asserting the response status, would prove it.

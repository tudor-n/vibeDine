# Segment 10 — Background & domain services: review findings

Scope review against the [Segment 10 checklist](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). This segment covers the five service classes that sit between the controllers and either the AI microservice or long-running domain work: [FlashDealService.cs](backend/RestaurantApi/Services/FlashDealService.cs), [OrderFinalizationService.cs](backend/RestaurantApi/Services/OrderFinalizationService.cs), [RecommendationService.cs](backend/RestaurantApi/Services/RecommendationService.cs), [SimilarityService.cs](backend/RestaurantApi/Services/SimilarityService.cs), [MenuExtractionClient.cs](backend/RestaurantApi/Services/MenuExtractionClient.cs). Their wiring in [Program.cs](backend/RestaurantApi/Program.cs#L87-L133) is in scope where it affects lifetime or HTTP-client configuration.

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**9 findings.** DI lifetimes are right — `RecommendationService` and `FlashDealService` are scoped alongside `AppDbContext` ([Program.cs:130-132](backend/RestaurantApi/Program.cs#L130-L132)); `OrderFinalizationService` is the only hosted-service registration and it creates a fresh scope per poll ([OrderFinalizationService.cs:59](backend/RestaurantApi/Services/OrderFinalizationService.cs#L59)). The HttpClient story is solid in shape: both AI clients go through `IHttpClientFactory` with explicit base addresses and timeouts ([Program.cs:88-128](backend/RestaurantApi/Program.cs#L88-L128)), the production path fails fast at startup if `AiService:BaseUrl` is missing, and no service holds a `new HttpClient()`. `RecommendationService` correctly intersects AI-returned product IDs with the current tenant's available menu before returning them ([RecommendationService.cs:80-81](backend/RestaurantApi/Services/RecommendationService.cs#L80-L81), [line 179](backend/RestaurantApi/Services/RecommendationService.cs#L179)). The tenant filter does the right thing in the one hosted service: `OrderFinalizationService` calls `SuppressResolution()` explicitly because it works cross-tenant ([line 67](backend/RestaurantApi/Services/OrderFinalizationService.cs#L67)), with the obvious justifying comment. No static mutable caches anywhere.

The findings cluster around four themes:

1. **Flash-deal lifecycle is built on two anti-patterns.** [F1](#f1--high--flash-deal-expiry-runs-in-a-fire-and-forget-taskrun-and-is-lost-on-process-restart-) — `FlashDealService.EvaluateEligibilityAsync` fires the deal's 2-minute expiry via `_ = Task.Run(async () => { await Task.Delay(...); ... })` at [FlashDealService.cs:262-283](backend/RestaurantApi/Services/FlashDealService.cs#L262-L283). On process restart, the timer is gone and the deal stays `Status = "pending"` forever; any exception inside the lambda is unobservable because the task is discarded. The OrderFinalizationService poll pattern is the right shape for this and lives in the same folder. [F2](#f2--high--claimdealasync-has-a-check-then-write-race-no-transaction-no-concurrency-token) — `ClaimDealAsync` reads `deal.Status == "pending"`, mutates `deal.Status = "claimed"`, and `SaveChangesAsync` without optimistic concurrency or a row lock; two concurrent claims of the same dealId both pass the check and both insert an OrderItem.
2. **The 🔥 OrderFinalizationService item is two-thirds met.** [F3](#f3--medium--orderfinalizationservice-sleeps-with-taskdelay-in-a-whiletrue-instead-of-periodictimer-) — the roadmap's 🔥 item is a triple: "catches and logs exceptions per iteration (✓); sleeps with a `PeriodicTimer` (not `Task.Delay` in `while(true)`) (❌); honors `CancellationToken` on shutdown (✓)." The loop at [OrderFinalizationService.cs:31-54](backend/RestaurantApi/Services/OrderFinalizationService.cs#L31-L54) uses `Task.Delay` and so accumulates drift (one iteration's work shifts the next iteration's wake time). Functional, but the roadmap is explicit and `PeriodicTimer.WaitForNextTickAsync(ct)` would also collapse the two try/catch blocks into one cancellation path.
3. **AI-client outages are handled inconsistently.** [F4](#f4--medium--aiservice-httpclient-has-no-retry-policy-and-no-circuit-breaker), [F5](#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites) — the roadmap asks for "timeout configured (✓), retry policy with jitter, circuit breaker or fail-fast on repeated AI outages." `AddPolicyHandler` / Polly is not referenced anywhere; every call sees one shot. Failure mapping is uneven: `MenuScanController` is the model (502 + localized message), `AdminController.AutoGenerateRules` returns 503 on exception **and** bare 500 on a non-success status (a flow bug — see [F9](#f9--low--admincontrollerautogeneraterules-has-a-fallthrough-500-and-loads-unbounded-order-history)), `AdminController.CreateDirective` / `CreateRecommendationDirective` swallow the failure and persist a directive with `parsedRules = null` with no signal to the admin, and `RecommendationService` falls back to random products silently. The customer surface's worst offender (`ClaimFlashDeal` leaking `ex.Message`) is the same call site already flagged in segment 09 [F6](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f6--low--claimflashdeal-leaks-exmessage-to-the-client-on-500), so it isn't re-counted here.
4. **Dead and near-dead code on the AI surface.** [F6](#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation) — `SimilarityService` is registered in DI ([Program.cs:131](backend/RestaurantApi/Program.cs#L131)) and has exactly zero callers; the live `/similarity/auto-generate` endpoint at [AdminController.cs:223-246](backend/RestaurantApi/Controllers/AdminController.cs#L223-L246) duplicates the AI call via `IHttpClientFactory` and skips validation that AI-returned product IDs belong to the current restaurant (the roadmap explicitly asks for that). [F7](#f7--low--flashdealclaimed-broadcasts-to-a-waitstaff-group-with-no-subscribers) — `ClaimDealAsync` notifies the `"WaitStaff"` group ([FlashDealService.cs:84](backend/RestaurantApi/Services/FlashDealService.cs#L84)) but every other call site (and `StaffHub.JoinRoleGroup` callers in the admin SPA) uses `"admin"`, so the `FlashDealClaimed` event has zero subscribers.

The remaining low-severity items are operational hygiene: [F8](#f8--low--logging-does-not-include-restaurantid-or-correlationid-via-beginscope) — no `_logger.BeginScope` anywhere in the codebase, so `RestaurantId` / `CorrelationId` don't propagate to surrounding log lines; [F9](#f9--low--admincontrollerautogeneraterules-has-a-fallthrough-500-and-loads-unbounded-order-history) — the auto-generate-rules endpoint loads the tenant's entire order history with no `Where(o => o.DeletedAt == null)` filter and no recency cap.

What does check out, beyond the verdict opener: `OrderFinalizationService` does catch exceptions per iteration and logs them ([OrderFinalizationService.cs:41-44](backend/RestaurantApi/Services/OrderFinalizationService.cs#L41-L44)); it honors `CancellationToken` both via `OperationCanceledException` on the work path and `TaskCanceledException` on the delay ([line 37-39, 50-52](backend/RestaurantApi/Services/OrderFinalizationService.cs#L37-L52)); it does not inject `AppDbContext` into the singleton — it resolves it from a freshly created scope ([line 59-67](backend/RestaurantApi/Services/OrderFinalizationService.cs#L59-L67)); the cross-tenant `SuppressResolution()` is the bounded, documented escape hatch for this exact case. `RecommendationService` rule-driven path also intersects with `availableIdSet` ([RecommendationService.cs:179](backend/RestaurantApi/Services/RecommendationService.cs#L179)) so AI hallucinations and stale rules can't surface a foreign product. `MenuExtractionClient` is the cleanest of the set: typed `HttpClient`, 6-minute timeout calibrated to the long-tail extract path ([Program.cs:127](backend/RestaurantApi/Program.cs#L127)), and `MenuScanController` translates `HttpRequestException` to a 502 with a user-visible localized message ([MenuScanController.cs:64-69](backend/RestaurantApi/Controllers/MenuScanController.cs#L64-L69)). The roadmap's "no static mutable cache" item passes outright — every static is either `readonly` or a `Random.Shared` / encoding-policy helper.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 OrderFinalizationService catches & logs exceptions per iteration; sleeps with `PeriodicTimer` (not `Task.Delay` in `while(true)`); honors `CancellationToken` | ⚠ Partial — see [F3](#f3--medium--orderfinalizationservice-sleeps-with-taskdelay-in-a-whiletrue-instead-of-periodictimer-) | Catches & logs: ✓ ([OrderFinalizationService.cs:41-44](backend/RestaurantApi/Services/OrderFinalizationService.cs#L41-L44)). PeriodicTimer: ❌ — uses `Task.Delay(PollInterval, stoppingToken)` ([line 48](backend/RestaurantApi/Services/OrderFinalizationService.cs#L48)) inside `while(!stoppingToken.IsCancellationRequested)`. CancellationToken: ✓ ([line 37-39](backend/RestaurantApi/Services/OrderFinalizationService.cs#L37-L39), [line 50-52](backend/RestaurantApi/Services/OrderFinalizationService.cs#L50-L52)). |
| Background services create their own scope (`IServiceScopeFactory`) for DbContext — never inject `AppDbContext` directly into a singleton | ✓ Pass | `OrderFinalizationService` injects `IServiceProvider` ([line 23](backend/RestaurantApi/Services/OrderFinalizationService.cs#L23)) and calls `_services.CreateScope()` each iteration ([line 59](backend/RestaurantApi/Services/OrderFinalizationService.cs#L59)). `FlashDealService` is **scoped**, not hosted, so the request scope it inherits is correct — and the background expiry it fires via `Task.Run` correctly creates its own scope at [FlashDealService.cs:266-267](backend/RestaurantApi/Services/FlashDealService.cs#L266-L267). |
| 🔥 `FlashDealService.Expire` runs in a transaction or uses optimistic concurrency to avoid double-expiry | ❌ Fail — see [F1](#f1--high--flash-deal-expiry-runs-in-a-fire-and-forget-taskrun-and-is-lost-on-process-restart-), [F2](#f2--high--claimdealasync-has-a-check-then-write-race-no-transaction-no-concurrency-token) | "Expire" runs in a fire-and-forget `_ = Task.Run` ([FlashDealService.cs:262-283](backend/RestaurantApi/Services/FlashDealService.cs#L262-L283)) — restart-fragile and not in a transaction. Sibling `ClaimDealAsync` ([line 42-88](backend/RestaurantApi/Services/FlashDealService.cs#L42-L88)) also has no transaction and no concurrency token on `FlashDealOffer.Status` ([FlashDealOffer.cs:21](backend/RestaurantApi/Models/FlashDealOffer.cs#L21)), so double-claim races aren't blocked at the DB. |
| `MenuExtractionClient` has timeout, retry policy with jitter, circuit breaker or fail-fast on repeated AI outages | ⚠ Partial — see [F4](#f4--medium--aiservice-httpclient-has-no-retry-policy-and-no-circuit-breaker) | Timeout: ✓ — 6 min for menu extract ([Program.cs:127](backend/RestaurantApi/Program.cs#L127)), 10 s for the shared `AiService` client ([line 106](backend/RestaurantApi/Program.cs#L106)). Retry with jitter: ❌ — no `AddPolicyHandler`, no Polly reference (`grep` confirms). Circuit breaker: ❌. Fail-fast: implicit (one shot per request). |
| AI client failures map to a clear API error (e.g., 503 with "AI service unavailable") — not a 500 stack trace | ⚠ Partial — see [F5](#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites), [F9](#f9--low--admincontrollerautogeneraterules-has-a-fallthrough-500-and-loads-unbounded-order-history) | `MenuScanController` ✓ (502 + localized message, [MenuScanController.cs:64-69](backend/RestaurantApi/Controllers/MenuScanController.cs#L64-L69)). `AdminController.AutoGenerateRules` partial (503 on `Exception`, bare 500 on a non-success status, [AdminController.cs:240-245](backend/RestaurantApi/Controllers/AdminController.cs#L240-L245)). `AdminController.CreateDirective` and `CreateRecommendationDirective` swallow the failure and persist a directive with `parsedRules = null` ([AdminController.cs:78-81](backend/RestaurantApi/Controllers/AdminController.cs#L78-L81), [line 157-160](backend/RestaurantApi/Controllers/AdminController.cs#L157-L160)). `RecommendationService` silent fallback to random ([RecommendationService.cs:93-96](backend/RestaurantApi/Services/RecommendationService.cs#L93-L96)). |
| RecommendationService and SimilarityService validate AI-returned product IDs exist in the current restaurant before persisting | ⚠ Partial — see [F6](#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation) | Recommendation: ✓ — AI IDs intersected with `availableIdSet` ([RecommendationService.cs:80-81](backend/RestaurantApi/Services/RecommendationService.cs#L80-L81)); rule-driven IDs intersected at [line 179](backend/RestaurantApi/Services/RecommendationService.cs#L179). Similarity: ❌ — `SimilarityService.AutoGenerateRulesAsync` ([SimilarityService.cs:16-37](backend/RestaurantApi/Services/SimilarityService.cs#L16-L37)) returns the raw AI JsonElement without validation, **and** the service is uncalled — the live path at [AdminController.cs:236-237](backend/RestaurantApi/Controllers/AdminController.cs#L236-L237) is also unvalidated. |
| No service has a static mutable field used for caching (memory leak / cross-request bleed) | ✓ Pass | Every `static` is `readonly` (`GracePeriod`, `PollInterval`, `MaxFiles`, `AllowedContentTypes`, `SnakeCaseJson`, `RemoveDiacritics`). `Random.Shared` is thread-safe by design. No singleton/static caches. |
| Logging includes `RestaurantId` and `CorrelationId` in scopes so prod logs are traceable | ❌ Fail — see [F8](#f8--low--logging-does-not-include-restaurantid-or-correlationid-via-beginscope) | `grep "BeginScope"` returns zero matches in [backend/RestaurantApi/](backend/RestaurantApi/). No correlation-ID middleware in [Program.cs](backend/RestaurantApi/Program.cs). Log lines do interpolate `{Total}`, `{Threshold}` etc. inline ([FlashDealService.cs:143-145](backend/RestaurantApi/Services/FlashDealService.cs#L143-L145)) but `RestaurantId` is never on them. |

---

## F1 — HIGH — Flash-deal expiry runs in a fire-and-forget `Task.Run` and is lost on process restart 🔥

[FlashDealService.cs:262-283](backend/RestaurantApi/Services/FlashDealService.cs#L262-L283):

```csharp
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromMinutes(2));

    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hub = scope.ServiceProvider.GetRequiredService<IHubContext<CustomerHub>>();

    // The background scope has no ambient request, so set the tenant explicitly
    // for the query filter to find the deal.
    scope.ServiceProvider.GetRequiredService<RestaurantApi.Tenancy.ICurrentTenant>()
        .SetRestaurant(restaurantId);

    var dealToExpire = await db.FlashDealOffers.FindAsync(dealId);
    if (dealToExpire == null || dealToExpire.Status != "pending") return;

    dealToExpire.Status = "expired";
    await db.SaveChangesAsync();

    await hub.Clients.Group(TenantGroups.Table(restaurantId, tableId))
        .SendAsync("FlashDealExpired", new { DealId = dealId });
});
```

Three independent problems on the same line:

- **Restart-fragile.** The 2-minute timer lives entirely in process memory. A `docker compose restart backend-api`, a deploy, an OOM kill — any of those, mid-window — leaves the row at `Status = "pending"` indefinitely. The customer SPA continues to display the deal because the only path that marks `expired` is this in-process task; once it's lost, only a manual claim (which the customer may or may not do) ever moves the row off `pending`. The customer's `GetRecommendation` endpoint at [CustomerController.cs:348-351](backend/RestaurantApi/Controllers/CustomerController.cs#L348-L351) does additionally check `ExpiresAt > DateTime.UtcNow`, so the customer eventually stops seeing it — but `GET /admin/flash-deals` ([AdminController.cs:417-435](backend/RestaurantApi/Controllers/AdminController.cs#L417-L435)) only filters by `Status` and would show a permanently-pending row to the kitchen.
- **Unobserved exception.** `_ = Task.Run(...)` discards the returned `Task`. There's no `try/catch` inside the lambda. Any exception (`DbUpdateConcurrencyException`, a transient SignalR failure, a malformed group key) is captured on the discarded task, never observed. With .NET 4+/Core there's no `TaskScheduler.UnobservedTaskException` crash path by default, so the exception is simply lost.
- **Pattern duplication.** `OrderFinalizationService` already exists in the same folder ([OrderFinalizationService.cs](backend/RestaurantApi/Services/OrderFinalizationService.cs)) and is structured for exactly this kind of work: poll the DB on a cadence, look for rows whose deadline has passed, transition them, emit SignalR. Reusing that shape for `FlashDealOffer.Status = "pending" && ExpiresAt < UtcNow` is straightforward and removes the in-memory timer entirely.

The roadmap's checklist puts this under "FlashDealService.Expire runs in a transaction or uses optimistic concurrency to avoid double-expiry" — which assumes "Expire" is a method called once, not a timer-fired-and-forgotten path that doesn't exist as a method at all. The structural pre-condition is missing.

**Recommended fix sketch (out of scope for this review).** Drop the `Task.Run`; let `OrderFinalizationService` (or a sibling background service) scan `FlashDealOffers` where `Status == "pending" AND ExpiresAt <= UtcNow` every few seconds and expire+broadcast in a transaction. SignalR latency is irrelevant for a 2-minute window.

---

## F2 — HIGH — `ClaimDealAsync` has a check-then-write race; no transaction, no concurrency token

[FlashDealService.cs:42-88](backend/RestaurantApi/Services/FlashDealService.cs#L42-L88):

```csharp
public async Task<bool> ClaimDealAsync(Guid orderId, Guid dealId)
{
    var deal = await _context.FlashDealOffers
        .Include(d => d.Product)
        .FirstOrDefaultAsync(d => d.Id == dealId);

    var order = await _context.Orders
        .Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == orderId);

    if (deal == null || order == null) return false;
    if (deal.Status != "pending") return false;
    if (DateTime.UtcNow > deal.ExpiresAt) return false;
    if (deal.ProductId == null) return false;

    order.Items ??= new List<OrderItem>();

    bool alreadyClaimed = order.Items.Any(i => i.IsFlashDealItem == true && i.ProductId == deal.ProductId);
    if (alreadyClaimed) return false;

    var orderItem = new OrderItem { ... };
    _context.OrderItems.Add(orderItem);
    order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
    deal.Status = "claimed";
    await _context.SaveChangesAsync();
    ...
}
```

`FlashDealOffer.Status` is a `string?` with no `[ConcurrencyCheck]`, `[Timestamp]`, or `RowVersion` ([FlashDealOffer.cs:21](backend/RestaurantApi/Models/FlashDealOffer.cs#L21)); the entity registration adds no `IsConcurrencyToken()` either ([AppDbContext.cs:128-148](backend/RestaurantApi/Data/AppDbContext.cs#L128-L148)). EF Core emits:

```sql
UPDATE flash_deal_offers SET status = 'claimed' WHERE id = @id
```

No `AND status = 'pending'` predicate. Two HTTP requests can race:

1. Request A reads `deal.Status == "pending"`; check passes.
2. Request B (different scope, different `_context`) reads `deal.Status == "pending"`; check passes.
3. A inserts an `OrderItem` into its order, writes `status = 'claimed'`, commits.
4. B inserts an `OrderItem` into its order, writes `status = 'claimed'` (same value, no exception), commits.

Both end up with the discounted product. The `alreadyClaimed` check at [line 62](backend/RestaurantApi/Services/FlashDealService.cs#L62) only deduplicates within the same `order.Items` graph — and segment 09's [F2](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f2--high--claimdealasync-does-not-verify-that-the-deal-belongs-to-the-order) has already established that nothing requires `deal.OrderId == order.Id`, so the two requests don't even need to share an order.

The roadmap is explicit: "FlashDealService.Expire runs in a **transaction or uses optimistic concurrency** to avoid double-expiry." Same machinery applies here. Today neither half is in place.

**Recommended fix sketch.** Add `e.Property(f => f.Status).IsConcurrencyToken();` on `FlashDealOffer` in `OnModelCreating` (or `[ConcurrencyCheck]` on the property). EF will then issue:

```sql
UPDATE flash_deal_offers SET status = 'claimed' WHERE id = @id AND status = 'pending'
```

and throw `DbUpdateConcurrencyException` for the loser. Wrap the `SaveChangesAsync` in a try/catch that returns `false` on concurrency conflict.

---

## F3 — MEDIUM — `OrderFinalizationService` sleeps with `Task.Delay` in a `while(true)` instead of `PeriodicTimer` 🔥

The roadmap's 🔥 item is a single bullet with three sub-items:

> `OrderFinalizationService` (IHostedService): catches and logs exceptions per iteration so one bad row doesn't kill the loop; **sleeps with a `PeriodicTimer` (not `Task.Delay` in `while(true)`)**; honors `CancellationToken` on shutdown.

[OrderFinalizationService.cs:29-54](backend/RestaurantApi/Services/OrderFinalizationService.cs#L29-L54):

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await FinalizeDueOrdersAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderFinalizationService poll iteration failed");
        }

        try
        {
            await Task.Delay(PollInterval, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }
    }
}
```

What `Task.Delay` does here, that `PeriodicTimer` doesn't: drift. Each iteration's `FinalizeDueOrdersAsync` takes some non-zero time; the next iteration's wake is `Task.Delay(5s)` *after that work completes*, not on a fixed 5-second clock. With a 5-second cadence and (say) 500 ms of work, iterations occur every 5.5 s rather than every 5 s. For 45-second order finalization, the drift is invisible. For anything tighter or more bursty, it shows up.

`PeriodicTimer.WaitForNextTickAsync(ct)` ticks on the wall clock (catching up if a tick was missed, by returning immediately) and collapses the two try/catch blocks into one cancellation path. The pattern Microsoft documents for hosted services since .NET 6.

The other two sub-items pass:

- **Catches per-iteration exceptions:** ✓ at [line 41-44](backend/RestaurantApi/Services/OrderFinalizationService.cs#L41-L44), with a tight log statement and no rethrow — so one bad batch doesn't kill the loop.
- **Honors `CancellationToken` on shutdown:** ✓ — `stoppingToken` is forwarded to `FinalizeDueOrdersAsync` (and on into `ToListAsync(ct)` and `SaveChangesAsync(ct)` at [line 76, 86](backend/RestaurantApi/Services/OrderFinalizationService.cs#L76-L86)); both delay and work paths return cleanly when cancellation fires.

The 🔥 status is two-thirds met. Calling it out as MEDIUM rather than LOW because it's named on the roadmap and is the kind of pattern question a senior reviewer will flag every time.

**Recommended fix sketch.**

```csharp
private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(PollInterval);
    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await FinalizeDueOrdersAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "OrderFinalizationService poll iteration failed");
            }
        }
    }
    catch (OperationCanceledException) { /* shutdown */ }
}
```

---

## F4 — MEDIUM — `AiService` HttpClient has no retry policy and no circuit breaker

[Program.cs:88-128](backend/RestaurantApi/Program.cs#L88-L128) wires both AI HTTP clients with:

- Base address (env-resolved, with a hard fail in non-dev if missing — ✓).
- Per-client timeout: 10 s for the shared `AiService` client, 6 min for `MenuExtractionClient`.
- No `AddPolicyHandler`, no Polly reference (`grep "Polly" backend/RestaurantApi` returns nothing in the project), no `Microsoft.Extensions.Http.Polly` import.

The roadmap names this directly:

> `MenuExtractionClient` has: timeout configured (default `HttpClient.Timeout` of 100s is wrong for sync user flows), retry policy with jitter, circuit breaker or fail-fast on repeated AI outages.

What's missing in practice:

- **Retry with jitter.** A transient TLS reset / DNS hiccup mid-request fails the customer's recommendation request immediately. With Polly's `WaitAndRetryAsync` + jitter, 2–3 retries inside the 10-second timeout would mask normal flakiness.
- **Circuit breaker.** If the AI service is hard-down for 30 minutes, *every* customer order incurs a full 10-second timeout on the recommendation path before falling through to the random-product fallback ([RecommendationService.cs:107-111](backend/RestaurantApi/Services/RecommendationService.cs#L107-L111)). That's 10 seconds of held HTTP threads per request, no early signal to drop the call, no signal to the admin that the AI is hard-down. A breaker that opens after N consecutive failures and fast-fails for K seconds would convert that into immediate fallback.

The trailing question on the roadmap asks: "What's the on-call response when the AI service is down for 30 minutes?" Today the answer is: every customer recommendation request hangs for 10 s and then falls back; every admin directive-create silently persists a directive with `parsedRules = null`; every `/menu/scan` returns 502 after up to 6 min of waiting; every admin `/similarity/auto-generate` returns either 503 or a bare 500 depending on how the AI fails. None of those signal "AI is down" to operators automatically — there's no `AddTransientHttpErrorPolicy` to emit those metrics.

**Recommended fix sketch.** Add the `Microsoft.Extensions.Http.Polly` NuGet, then:

```csharp
builder.Services.AddHttpClient("AiService", ...)
    .AddTransientHttpErrorPolicy(b => b.WaitAndRetryAsync(
        3, n => TimeSpan.FromMilliseconds(200 * Math.Pow(2, n)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100))))
    .AddTransientHttpErrorPolicy(b => b.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
```

`MenuExtractionClient` should probably **not** retry (a 6-minute request is too expensive to retry), but should be guarded by the breaker so a brown-out doesn't keep eating compute.

---

## F5 — MEDIUM — AI client failure mapping is inconsistent across call sites

The roadmap asks:

> AI client failures map to a clear API error (e.g., 503 with "AI service unavailable") — not a 500 stack trace.

Five live call sites, five different conventions:

| Call site | Behavior on AI exception | Behavior on AI non-success status | Surface |
|---|---|---|---|
| [`MenuScanController.Scan`](backend/RestaurantApi/Controllers/MenuScanController.cs#L60-L69) | 502 + localized message | n/a — `EnsureSuccessStatusCode` throws | Admin |
| [`AdminController.AutoGenerateRules`](backend/RestaurantApi/Controllers/AdminController.cs#L223-L246) | 503 + `"AI Service unavailable"` | Falls through to bare `return StatusCode(500)` — see [F9](#f9--low--admincontrollerautogeneraterules-has-a-fallthrough-500-and-loads-unbounded-order-history) | Admin |
| [`AdminController.CreateDirective`](backend/RestaurantApi/Controllers/AdminController.cs#L78-L81) | Silent: persists directive with `parsedRules = null`, returns 200 | Same silent path | Admin |
| [`AdminController.CreateRecommendationDirective`](backend/RestaurantApi/Controllers/AdminController.cs#L157-L160) | Silent: same as above | Logs warning, persists with null rules | Admin |
| [`RecommendationService.GetRecommendationsAsync`](backend/RestaurantApi/Services/RecommendationService.cs#L93-L96) | Falls back to random products silently | Logs warning, same silent fallback | Customer |
| [`CustomerController.ClaimFlashDeal`](backend/RestaurantApi/Controllers/CustomerController.cs#L290-L293) | 500 with `ex.Message` (segment 09 [F6](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f6--low--claimflashdeal-leaks-exmessage-to-the-client-on-500), not re-counted) | n/a | Customer |

The two customer-surface fallbacks are actually OK — a random pairing or no flash deal is a fine degradation, the SPA never needs to know "AI is down." The admin-surface silences are the trouble. An admin creates a directive ("offer free wine with the salad"), the AI is hard-down, the directive is persisted with `parsedRules = null`, and `FlashDealService.EvaluateEligibilityAsync` then falls through to its raw-prompt regex path at [FlashDealService.cs:205-218](backend/RestaurantApi/Services/FlashDealService.cs#L205-L218) — *which works for some prompts*. The admin has no way to know whether the directive's behavior is "AI-parsed structured rules" or "raw regex over the prompt." That's a feature-degradation that should at minimum show up in the response.

**Recommended fix sketch.** Centralise the AI-error → HTTP-status mapping in a small helper (or middleware that recognises `HttpRequestException` from a typed client). For admin directive creation, return the persisted directive with an extra field `ai_status: "parsed" | "fallback"` so the SPA can show a "we couldn't parse this with the AI; raw matching only" banner.

---

## F6 — MEDIUM — `SimilarityService` is dead code; the live `/similarity/auto-generate` path duplicates logic in `AdminController` and skips validation

[SimilarityService.cs](backend/RestaurantApi/Services/SimilarityService.cs) is registered in DI at [Program.cs:131](backend/RestaurantApi/Program.cs#L131). Grep for its consumers turns up only `Program.cs` itself and the dev guides:

```
backend\tudor-backend-guide.md
backend\BACKEND_DEV_GUIDE.md
backend\beny-backend-guide.md
backend\RestaurantApi\Program.cs              ← registration
backend\RestaurantApi\Services\SimilarityService.cs  ← the file itself
```

No controller injects it. No service injects it. The live `/similarity/auto-generate` endpoint sits in `AdminController` at [AdminController.cs:223-246](backend/RestaurantApi/Controllers/AdminController.cs#L223-L246) and re-implements the same logic by hand:

```csharp
[HttpPost("similarity-rules/auto-generate")]
public async Task<IActionResult> AutoGenerateRules()
{
    var client = _httpClientFactory.CreateClient("AiService");
    var orders = await _dbContext.Orders.Include(o => o.Items).ToListAsync();
    var history = orders.Select(o => o.Items.Select(i => i.ProductId.ToString()).ToList()).ToList();

    try
    {
        var aiResponse = await client.PostAsJsonAsync("/similarity/auto-generate", new { order_history = history });
        if (aiResponse.IsSuccessStatusCode)
        {
            var result = await aiResponse.Content.ReadFromJsonAsync<JsonElement>();
            return Ok(result);
        }
    }
    catch
    {
        return StatusCode(503, new { message = "AI Service unavailable" });
    }
    return StatusCode(500);
}
```

Two consequences:

- **Duplication trap.** A future change (a new `HttpClient` named client, a request-shape change, a Pydantic schema bump in the Python side) needs to be applied in two places; whoever does it will probably touch the live one and forget the dead one, since the dead one isn't exercised in tests either. Or vice-versa — the symmetric risk is worse, because the next person to wire up the dead `SimilarityService` would probably trust its shape and miss the working implementation.
- **Missed validation.** The roadmap explicitly couples this to segment 16: "RecommendationService and SimilarityService validate that AI-returned product IDs exist in the current restaurant before persisting (segment 16's twin)." Neither path validates — the controller returns the raw AI `JsonElement` straight to the admin SPA. If the AI hallucinates a product ID, or returns IDs that belong to seed data from another restaurant, the admin's "preview rules" UI displays them and offers to save. Whether the save path (which I have not audited here — it appears to be missing entirely, given there's no `POST /similarity-rules` endpoint visible) sanitises is the next question.

**Recommended fix sketch.** Either: (a) delete `SimilarityService` and `AddScoped<SimilarityService>()`, and add the missing tenant-product-ID validation directly in `AutoGenerateRules`; or (b) move the logic from `AutoGenerateRules` into `SimilarityService.AutoGenerateRulesAsync`, inject it into `AdminController`, and add the validation there. The duplicate must not survive.

---

## F7 — LOW — `FlashDealClaimed` broadcasts to a `"WaitStaff"` group with no subscribers

[FlashDealService.cs:84](backend/RestaurantApi/Services/FlashDealService.cs#L84):

```csharp
await _staffhub.Clients.Group(TenantGroups.Role(order.RestaurantId, "WaitStaff"))
    .SendAsync("FlashDealClaimed", new { OrderId = order.Id, DealName = productName });
```

The `"WaitStaff"` role string appears nowhere else in the codebase. The four other staff-hub broadcasts all use `"admin"`:

- [`AdminController.UpdateOrderStatus`:399](backend/RestaurantApi/Controllers/AdminController.cs#L399) — `Role(order.RestaurantId, "admin")`
- [`CustomerController.SubmitOrder`:66](backend/RestaurantApi/Controllers/CustomerController.cs#L66) — `Role(order.RestaurantId, "admin")`
- [`OrderFinalizationService`:99](backend/RestaurantApi/Services/OrderFinalizationService.cs#L99) — `Role(order.RestaurantId, "admin")`

`StaffHub.JoinRoleGroup` ([StaffHub.cs:9-11](backend/RestaurantApi/Hubs/StaffHub.cs#L9-L11)) is client-driven — it joins whatever string the SPA sends. A grep across `frontend-admin/` for `"WaitStaff"` and for `"FlashDealClaimed"` returns zero matches; the admin SPA never asks to join the `"WaitStaff"` group and never registers a `FlashDealClaimed` handler. The broadcast is sent to a SignalR group with zero connections — SignalR silently no-ops, the kitchen never sees a flash-deal-claim event in realtime.

Functional impact is small (the order's submit a few minutes later will broadcast `OrderSubmitted` and surface the order with the flash-deal item — the kitchen sees it then). But it's a feature that looks live and isn't, and a typo-tier inconsistency: either every site should use the canonical role string (`"admin"`, since that's what everyone else uses and what the admin SPA joins), or there should be a shared constant.

**Recommended fix sketch.** Replace `"WaitStaff"` with `"admin"` at [FlashDealService.cs:84](backend/RestaurantApi/Services/FlashDealService.cs#L84) and add a `TenantGroups.AdminRole(restaurantId)` constant-style helper so this can't happen again. Future segment 12 review will need to confirm the canonical role string set anyway.

---

## F8 — LOW — Logging does not include `RestaurantId` or `CorrelationId` via `BeginScope`

The roadmap names this:

> Logging includes `RestaurantId` and `CorrelationId` in scopes so prod logs are traceable.

`grep -r "BeginScope" backend/RestaurantApi` returns zero matches across the entire backend project. No correlation-ID middleware is registered in [Program.cs](backend/RestaurantApi/Program.cs) — no `AddHttpLogging`, no `Microsoft.AspNetCore.HeaderPropagation`, no W3C trace-context setup beyond the default `Activity` plumbing.

The log lines that do exist are well-formed structured logs — e.g. [FlashDealService.cs:143-145](backend/RestaurantApi/Services/FlashDealService.cs#L143-L145):

```csharp
_logger.LogInformation(
    "Flash deal skipped: order total {Total} below threshold {Threshold} (directive: {Prompt})",
    orderTotal, threshold, activeDirective.RawPrompt);
```

— but `RestaurantId` and `OrderId` aren't on them. For single-tenant dev with a handful of orders this is fine. For a multi-tenant prod where two tenants might both be live at the same moment, an operator reading a log line about "order total 0.00 below threshold 190.00" can't tell which restaurant's prompt is misfiring without grepping for the surrounding lines.

Today's logs are debuggable; once the system has more than a single active tenant they need scopes. This is a "before-launch" fix, not a "right now" fix.

**Recommended fix sketch.** Add a small middleware after `TenantResolutionMiddleware` that opens `_logger.BeginScope(new { RestaurantId = tenant.RestaurantId, CorrelationId = Activity.Current?.TraceId.ToString() })` for the request. With Serilog (which I have not verified is configured — `dotnet list package` would clarify), all log lines from the request inherit the properties automatically.

---

## F9 — LOW — `AdminController.AutoGenerateRules` has a fallthrough 500 and loads unbounded order history

[AdminController.cs:223-246](backend/RestaurantApi/Controllers/AdminController.cs#L223-L246):

```csharp
[HttpPost("similarity-rules/auto-generate")]
public async Task<IActionResult> AutoGenerateRules()
{
    var client = _httpClientFactory.CreateClient("AiService");

    var orders = await _dbContext.Orders.Include(o => o.Items).ToListAsync();
    var history = orders.Select(o => o.Items.Select(i => i.ProductId.ToString()).ToList()).ToList();

    try
    {
        var aiResponse = await client.PostAsJsonAsync("/similarity/auto-generate", new { order_history = history });
        if (aiResponse.IsSuccessStatusCode)
        {
            var result = await aiResponse.Content.ReadFromJsonAsync<JsonElement>();
            return Ok(result);
        }
    }
    catch
    {
        return StatusCode(503, new { message = "AI Service unavailable" });
    }

    return StatusCode(500);
}
```

Two latent issues, neither catastrophic:

- **Unbounded query.** `_dbContext.Orders.Include(o => o.Items).ToListAsync()` returns *every order ever* for the tenant — soft-deleted (`DeletedAt != null`), cancelled, and drafts included. For a tenant with 50k orders it's a multi-megabyte SELECT + N+1 includes on `order_items`. The AI side also has to ingest the full history, and the request body is likely to outgrow Pydantic's default limits long before that. Suggested: `.Where(o => o.DeletedAt == null && o.Status == "completed").OrderByDescending(o => o.CreatedAt).Take(500)` (or a date window).
- **Fall-through bare 500.** If `aiResponse.IsSuccessStatusCode` is `false` (e.g. the AI returns 422 because the request body was malformed, or 500 because Gemini quota is exhausted), no exception is thrown — `EnsureSuccessStatusCode` isn't called — and control falls out of the `if` and the `try`. The method then returns `StatusCode(500)` with no body. The admin SPA can't distinguish "AI is hard-down" (catch block) from "AI rejected our request" (this path). Either log+return a 502 with the AI's status code, or normalise to `503` like the catch block does.

Both are minor on their own. They're listed together because they live in the same 23-line method and a small refactor (extract a `CallAiAsync` helper that returns `Result<JsonElement, AiError>`, used by all three admin AI endpoints) would address F5, F6, and this finding at once.

---

## Appendix

### Service inventory

| Service | DI lifetime | Touches DB? | Calls AI? | SignalR? | Test coverage |
|---|---|---|---|---|---|
| [`FlashDealService`](backend/RestaurantApi/Services/FlashDealService.cs) | Scoped ([Program.cs:132](backend/RestaurantApi/Program.cs#L132)) | ✓ via `_context` | indirect (via `AIDirective.ParsedRules`) | both customer + staff hubs | ❌ no tests |
| [`OrderFinalizationService`](backend/RestaurantApi/Services/OrderFinalizationService.cs) | HostedService ([Program.cs:133](backend/RestaurantApi/Program.cs#L133)) | ✓ via per-iteration scope | ❌ | both hubs | ❌ no tests |
| [`RecommendationService`](backend/RestaurantApi/Services/RecommendationService.cs) | Scoped ([Program.cs:130](backend/RestaurantApi/Program.cs#L130)) | ✓ | ✓ `POST /recommend` | ❌ | ❌ no tests |
| [`SimilarityService`](backend/RestaurantApi/Services/SimilarityService.cs) | Scoped ([Program.cs:131](backend/RestaurantApi/Program.cs#L131)) | ❌ | ✓ `POST /similarity/auto-generate` | ❌ | ❌ no tests; **dead code** (see [F6](#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation)) |
| [`MenuExtractionClient`](backend/RestaurantApi/Services/MenuExtractionClient.cs) | Typed client ([Program.cs:109](backend/RestaurantApi/Program.cs#L109)), transient consumer | ❌ | ✓ `POST /menu/extract` | ❌ | ✓ [`MenuExtractionClientTests`](backend/RestaurantApi.Tests/MenuExtractionClientTests.cs) (happy + 503) plus [`MenuScanControllerTests`](backend/RestaurantApi.Tests/MenuScanControllerTests.cs) (happy + 502 + 413 + 400) |

Gaps to file as follow-ups (likely missing — the test suite has 5 files total, all on the menu-scan path):

- `FlashDealServiceTests` covering: `ClaimDealAsync` happy path, expired/claimed/foreign-order rejections (segment 09 [F2](CODE_REVIEW_SEGMENT_09_CUSTOMER_CONTROLLERS.md#f2--high--claimdealasync-does-not-verify-that-the-deal-belongs-to-the-order)), concurrency race ([F2](#f2--high--claimdealasync-has-a-check-then-write-race-no-transaction-no-concurrency-token)), and `EvaluateEligibilityAsync` directive-vs-regex-vs-random paths.
- `OrderFinalizationServiceTests` covering: orders past cutoff get archived, orders before cutoff don't, multi-tenant fairness, exception in one iteration doesn't kill the loop, `CancellationToken` shutdown is prompt.
- `RecommendationServiceTests` covering: AI success path with ID validation, AI failure → rule-driven fallback, rule-driven failure → random fallback, AI returns hallucinated foreign-tenant IDs (filtered out), empty cart edge.

### Trailing roadmap question

> ↳ What's the on-call response when the AI service is down for 30 minutes? (Should be: customer flows degrade gracefully; admin flows return a clear error.)

Today, broken down by surface:

- **Customer recommendation** — degrades gracefully but slowly. Each call hangs for the 10-second client timeout ([Program.cs:106](backend/RestaurantApi/Program.cs#L106)) before falling back to random products ([RecommendationService.cs:107-111](backend/RestaurantApi/Services/RecommendationService.cs#L107-L111)). No circuit breaker, so the 10 s is paid per request.
- **Customer order submit + flash deal eval** — partial. `EvaluateEligibilityAsync` ([FlashDealService.cs:90](backend/RestaurantApi/Services/FlashDealService.cs#L90)) does its directive parsing in-process and doesn't talk to the AI for the eval itself; AI is only on the *creation* of an `AIDirective` ([AdminController.cs:68](backend/RestaurantApi/Controllers/AdminController.cs#L68)). So submitting an order with an existing directive degrades from "AI-parsed structured rules" → "raw-prompt regex" silently — not bad, but the admin never knows.
- **Admin menu scan** — clear 502 + localized message ([MenuScanController.cs:64-69](backend/RestaurantApi/Controllers/MenuScanController.cs#L64-L69)). ✓
- **Admin directive create** — silent: directive persists with `parsedRules = null`, 200 OK ([AdminController.cs:78-95](backend/RestaurantApi/Controllers/AdminController.cs#L78-L95)). The admin thinks their directive is fully wired. ❌ — see [F5](#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites).
- **Admin similarity auto-generate** — 503 if the AI throws, bare 500 if the AI returns a non-success status ([AdminController.cs:240-245](backend/RestaurantApi/Controllers/AdminController.cs#L240-L245)). ❌ — see [F9](#f9--low--admincontrollerautogeneraterules-has-a-fallthrough-500-and-loads-unbounded-order-history).

Operationally there's also no metric or alert that says "AI was unreachable for the last 5 min" — that's the broader observability gap [F8](#f8--low--logging-does-not-include-restaurantid-or-correlationid-via-beginscope) hints at.

### Cross-segment links

- The `SuppressResolution()` call at [OrderFinalizationService.cs:67](backend/RestaurantApi/Services/OrderFinalizationService.cs#L67) is the second documented bypass site, paired with the one segment 04 already audited. The comment justifying the cross-tenant work is present.
- The `IgnoreQueryFilters` site at [CustomerController.cs:54-57](backend/RestaurantApi/Controllers/CustomerController.cs#L54-L57) is upstream of `FlashDealService.EvaluateEligibilityAsync` (called at [CustomerController.cs:62](backend/RestaurantApi/Controllers/CustomerController.cs#L62)) but only affects the OrderNumber sequence, not the flash-deal flow.
- The `FlashDealOffer.Status` free-form strings (`"pending"`, `"claimed"`, `"expired"`) are part of the segment 07 [F2](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f2--medium--orderstatus-paymentstatus-flashdealofferstatus-invitationstatus-are-free-form-strings-with-no-db-check-constraint) finding — making them an enum would also surface a place to add the missing concurrency token from [F2](#f2--high--claimdealasync-has-a-check-then-write-race-no-transaction-no-concurrency-token).
- The fire-and-forget pattern in [F1](#f1--high--flash-deal-expiry-runs-in-a-fire-and-forget-taskrun-and-is-lost-on-process-restart-) is the only one in the segment; the rest of the services are either cleanly synchronous-in-request (`FlashDealService.ClaimDealAsync`, `RecommendationService`, `SimilarityService`) or are the right hosted-service shape (`OrderFinalizationService`).
- The dead `FlashDealClaimed` broadcast in [F7](#f7--low--flashdealclaimed-broadcasts-to-a-waitstaff-group-with-no-subscribers) should be picked up again in segment 12 (SignalR hubs) when the canonical role-string set is being audited.

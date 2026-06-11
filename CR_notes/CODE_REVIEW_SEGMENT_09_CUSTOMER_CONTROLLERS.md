# Segment 09 — Controllers, customer surface: review findings

Scope review against the [Segment 09 checklist](CODE_REVIEW_ROADMAP.md#segment-09--controllers--customer-surface) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). This segment covers the two anonymous customer-facing controllers — [CustomerController.cs](backend/RestaurantApi/Controllers/CustomerController.cs) and [MenuController.cs](backend/RestaurantApi/Controllers/MenuController.cs) — plus the request DTOs they bind ([AddItemRequest.cs](backend/RestaurantApi/DTO/Requests/AddItemRequest.cs)), the response DTOs they emit ([TableSessionResponse.cs](backend/RestaurantApi/DTO/Responses/TableSessionResponse.cs)), and the customer-reachable surface of [FlashDealService.cs](backend/RestaurantApi/Services/FlashDealService.cs) / [RecommendationService.cs](backend/RestaurantApi/Services/RecommendationService.cs).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**7 findings.** Tenant isolation holds — every entity the customer surface touches (`Tables`, `Products`, `Categories`, `Orders`, `OrderItems`, `FlashDealOffers`) carries a global query filter ([AppDbContext.cs:299-307](backend/RestaurantApi/Data/AppDbContext.cs#L299-L307)), and the middleware sets the ambient tenant from the URL `restaurantId` or `qrToken` before the controllers run ([TenantResolutionMiddleware.cs:32-80](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L32-L80)). The order total is recomputed server-side from `Products.Price` ignoring any client-supplied price ([CustomerController.cs:120](backend/RestaurantApi/Controllers/CustomerController.cs#L120)). Cross-restaurant attacks via the body (a `TableId` or `ProductId` from another tenant) are blocked by the filter, which `FindAsync`/`Where` both respect.

Where the segment falls short clusters around four themes:

1. **The 🔥 checklist item is only half-met.** [F1](#f1--high--qr-token-binds-only-the-session-call-subsequent-customer-endpoints-trust-the-url-restaurantid-) — the QR token is validated exactly once, at `GET /tables/{qrToken}/session` ([CustomerController.cs:217-274](backend/RestaurantApi/Controllers/CustomerController.cs#L217-L274)). Every subsequent customer endpoint (`POST /orders`, `PATCH /orders/{id}/items`, `POST /orders/{id}/submit`, `POST /orders/{id}/claim-deal/{dealId}`, `GET /orders/{id}/recommendations`) is anonymous and authenticates via the URL `restaurantId` alone — the QR token never enters the equation again. Tenant scope holds, but **table** scope does not: within a restaurant, the server has no link between the request and any specific table. Anyone with a `restaurantId` and an `orderId` / `TableId` (both `Guid`, but neither cryptographically secret to the holder of one table's QR) can place, mutate, submit, or claim deals on any other table's order at the same restaurant. The roadmap is explicit on this one: "A valid QR token resolves to a single `RestaurantTable` and that's the **only** restaurant the request can touch. Trace once."
2. **The flash-deal claim path doesn't validate ownership.** [F2](#f2--high--claimdealasync-does-not-verify-that-the-deal-belongs-to-the-order) — `FlashDealService.ClaimDealAsync(orderId, dealId)` ([FlashDealService.cs:42-88](backend/RestaurantApi/Services/FlashDealService.cs#L42-L88)) loads the deal and the order separately, checks the deal's `Status` and `ExpiresAt`, then adds the deal's product to the order — but never asserts `deal.OrderId == order.Id`. A customer can pass any other table's `dealId` to their own `orderId` and steal it. Marking the foreign deal `"claimed"` simultaneously denies it to its rightful target.
3. **Input validation is below what the checklist asks for.** [F3](#f3--medium--createorder-and-additemsorder-do-not-validate-quantity--0-or-product-availability) — neither `CreateOrder` ([CustomerController.cs:85-141](backend/RestaurantApi/Controllers/CustomerController.cs#L85-L141)) nor `AddItemsOrder` ([CustomerController.cs:143-215](backend/RestaurantApi/Controllers/CustomerController.cs#L143-L215)) filters out unavailable products (the `IsAvailable` flag on [Product.cs:24](backend/RestaurantApi/Models/Product.cs#L24) gates the menu display but is ignored at order time), and `CreateOrder` accepts any signed `Quantity` — zero or negative values are persisted and contribute negatively to `TotalAmount` ([line 120](backend/RestaurantApi/Controllers/CustomerController.cs#L120)). The segment's stated goal names "products exist, **are available**, belong to the resolved restaurant, **quantities >0 and within limits**".
4. **Operational hardening absent on the highest-volume customer endpoint.** [F4](#f4--medium--post-orders-has-no-idempotency-key--retries-create-duplicate-draft-orders), [F5](#f5--low--no-rate-limit-on-order-placement-checklist-asks-document-if-absent), [F6](#f6--low--claimflashdeal-leaks-exmessage-to-the-client-on-500) — `POST /orders` has no idempotency key (a single-tap-retry on a flaky cellular connection creates duplicate drafts), no rate limit, and a generic `try { ... } catch (Exception ex) { 500 + ex.Message }` ([CustomerController.cs:290-293](backend/RestaurantApi/Controllers/CustomerController.cs#L290-L293)) on the flash-deal claim leaks internal error text to the public surface. The checklist names idempotency and rate limit explicitly.

The seventh finding — [F7](#f7--low--gettablesession-has-a-race-on-the-implicit-draft-order-creation) — is the GetTableSession lazy-create race: two parallel session calls produce two draft `Order` rows because there's no unique index on `(TableId, Status='draft')`. Practical impact is low (one of them is leaked, not security-critical), but it's a footgun worth recording.

What does check out: tenant filter coverage is complete on every entity the customer surface touches ([AppDbContext.cs:299-307](backend/RestaurantApi/Data/AppDbContext.cs#L299-L307)); the middleware resolves and sets `_tenant` before controllers run ([Program.cs:184](backend/RestaurantApi/Program.cs#L184)); `CreateOrder` reads `product.Price` from the DB and ignores any client-supplied price ([CustomerController.cs:115](backend/RestaurantApi/Controllers/CustomerController.cs#L115)); `SubmitOrder` recomputes `TotalAmount` from items ([CustomerController.cs:47](backend/RestaurantApi/Controllers/CustomerController.cs#L47)) and the `IgnoreQueryFilters()` bypass for the per-restaurant `OrderNumber` sequence is bounded by an explicit `Where(o => o.RestaurantId == order.RestaurantId)` ([CustomerController.cs:54-57](backend/RestaurantApi/Controllers/CustomerController.cs#L54-L57)); `ClaimDealAsync` checks `Status == "pending"` and `ExpiresAt > now` server-side ([FlashDealService.cs:54-56](backend/RestaurantApi/Services/FlashDealService.cs#L54-L56)) and rejects double-claiming the same product on the same order ([line 62-63](backend/RestaurantApi/Services/FlashDealService.cs#L62-L63)); `RecommendationService` constrains AI-returned product IDs to the candidate set computed from the current tenant's available menu ([RecommendationService.cs:34-39](backend/RestaurantApi/Services/RecommendationService.cs#L34-L39), [line 80-81](backend/RestaurantApi/Services/RecommendationService.cs#L80-L81)); `MenuController` filters by `IsAvailable == true` everywhere it lists products ([MenuController.cs:33](backend/RestaurantApi/Controllers/MenuController.cs#L33), [line 59](backend/RestaurantApi/Controllers/MenuController.cs#L59)); response DTOs (`OrderResponse`, `TableSessionResponse`, `MenuCategoryResponse`) are used instead of returning EF entities; no `IgnoreQueryFilters` or `SuppressResolution` in the customer surface beyond the one audited site; no `async void`.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 A valid QR token resolves to a single `RestaurantTable` and that's the only restaurant the request can touch | ⚠ Partial — see [F1](#f1--high--qr-token-binds-only-the-session-call-subsequent-customer-endpoints-trust-the-url-restaurantid-) | QR is validated once at `GET /tables/{qrToken}/session` ([CustomerController.cs:217-224](backend/RestaurantApi/Controllers/CustomerController.cs#L217-L224)). All other customer endpoints route on `restaurantId` and never re-check the QR; the table is identified by `TableId` in the body or `orderId` in the URL, with no QR-binding. Tenant filter holds the restaurant boundary; the table boundary is unauthenticated. |
| Customer cannot modify menu, prices, or directives | ✓ Pass | Customer routes are scoped to `/restaurants/{rid}/orders/*`, `/restaurants/{rid}/menu`, `/tables/{qrToken}/session` — no PUT/PATCH/DELETE on `/products`, `/categories`, `/directives`. Admin mutations live behind `[Authorize]` on the admin controllers (segment 08). |
| Order placement: server recomputes total, ignores any client-supplied price | ✓ Pass | `CreateOrder` sets `UnitPriceAtPurchase = product.Price` from DB ([CustomerController.cs:115](backend/RestaurantApi/Controllers/CustomerController.cs#L115)); `TotalAmount = Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase)` ([line 120](backend/RestaurantApi/Controllers/CustomerController.cs#L120)). `SubmitOrder` recomputes the same way ([line 47](backend/RestaurantApi/Controllers/CustomerController.cs#L47)). `OrderItemRequest` has no `UnitPrice` field ([AddItemRequest.cs:20-25](backend/RestaurantApi/DTO/Requests/AddItemRequest.cs#L20-L25)) — client cannot supply one. |
| Order placement validates: products exist, are available, belong to restaurant, quantities > 0 and within limits | ⚠ Partial — see [F3](#f3--medium--createorder-and-additemsorder-do-not-validate-quantity--0-or-product-availability) | Exist: ✓ ([CustomerController.cs:105-106](backend/RestaurantApi/Controllers/CustomerController.cs#L105-L106)). Belongs-to-restaurant: ✓ via global query filter. Available: ❌ (no `IsAvailable` check at order time). Quantity > 0 / upper bound: ❌ in `CreateOrder`, partial in `AddItemsOrder`. |
| Flash-deal redemption checks `Status == "pending"` and `ExpiresAt > now` server-side | ⚠ Partial — see [F2](#f2--high--claimdealasync-does-not-verify-that-the-deal-belongs-to-the-order) | Status and expiry: ✓ ([FlashDealService.cs:54-56](backend/RestaurantApi/Services/FlashDealService.cs#L54-L56)). Missing: ownership check (`deal.OrderId == order.Id`). |
| Recommendation endpoint never reveals products from a different restaurant or unavailable items | ✓ Pass | Candidate set is built with `p.IsAvailable == true` ([RecommendationService.cs:34-37](backend/RestaurantApi/Services/RecommendationService.cs#L34-L37)) and constrained to the current tenant via the global filter. AI-returned IDs are intersected with `availableIdSet` ([RecommendationService.cs:80-81](backend/RestaurantApi/Services/RecommendationService.cs#L80-L81)); rule-driven IDs are intersected at [RecommendationService.cs:179](backend/RestaurantApi/Services/RecommendationService.cs#L179). The controller-side dictionary lookup is also tenant-filtered ([CustomerController.cs:319-321](backend/RestaurantApi/Controllers/CustomerController.cs#L319-L321)). |
| Table session state is scoped to the QR token | ⚠ Partial — see [F1](#f1--high--qr-token-binds-only-the-session-call-subsequent-customer-endpoints-trust-the-url-restaurantid-) | The *session* endpoint is QR-scoped. Downstream session state (the active draft order) is keyed by `TableId`, not by QR token — and the `TableId` is reachable from anywhere in the restaurant. |
| Rate limit on order placement (document if absent) | ❌ Fail — see [F5](#f5--low--no-rate-limit-on-order-placement-checklist-asks-document-if-absent) | No rate-limit middleware in [Program.cs](backend/RestaurantApi/Program.cs); no `[EnableRateLimiting]` on any controller. |
| Idempotency key supported for `POST /orders` to handle network retries | ❌ Fail — see [F4](#f4--medium--post-orders-has-no-idempotency-key--retries-create-duplicate-draft-orders) | Neither `CreateOrder` nor `GetTableSession` supports an `Idempotency-Key` header or an equivalent body field. Each call creates a new row. |

---

## F1 — HIGH — QR token binds only the session call; subsequent customer endpoints trust the URL `restaurantId` 🔥

The roadmap's 🔥 item reads:
> A valid QR token resolves to a single `RestaurantTable` and that's the **only** restaurant the request can touch. Trace once.

Tracing once: the QR token appears in exactly one route — `GET /api/tables/{qrToken:guid}/session` ([CustomerController.cs:217](backend/RestaurantApi/Controllers/CustomerController.cs#L217)). The middleware's `qrToken` branch ([TenantResolutionMiddleware.cs:69-80](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L69-L80)) resolves the table → restaurant and sets the ambient tenant. The response carries `RestaurantId`, `TableId`, and the draft order's `Id` ([TableSessionResponse.cs:4-13](backend/RestaurantApi/DTO/Responses/TableSessionResponse.cs#L4-L13)).

Every other customer call uses one of:

```text
POST   /api/restaurants/{restaurantId:guid}/orders
PATCH  /api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/items
POST   /api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/submit
POST   /api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/claim-deal/{dealId:guid}
GET    /api/restaurants/{restaurantId:guid}/orders/{id:guid}/recommendations
GET    /api/restaurants/{restaurantId:guid}/menu
GET    /api/restaurants/{restaurantId:guid}/menu/{categoryId:guid}
```

None of those routes carry the QR token. The middleware's `restaurantId` branch is unauthenticated for these calls (`ctx.User?.Identity?.IsAuthenticated == true` is false for anonymous customers — the membership check at [TenantResolutionMiddleware.cs:56-65](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L56-L65) is skipped), so the only check is `restaurant.IsActive`.

**What this means in practice.** Inside one active restaurant `R`, an anonymous caller who knows or guesses any `(TableId, OrderId)` pair can:

- Submit a foreign draft order: `POST /api/restaurants/{R}/orders/{victim_orderId}/submit` — the lookup at [CustomerController.cs:37-39](backend/RestaurantApi/Controllers/CustomerController.cs#L37-L39) is `o.Id == orderId && o.Status == "draft"`, scoped only by the global tenant filter on `Order` ([AppDbContext.cs:302](backend/RestaurantApi/Data/AppDbContext.cs#L302)). The victim's draft, including whatever items the victim's UI had staged, ships to the kitchen with the attacker's timing.
- Mutate items on a foreign draft: `PATCH /api/restaurants/{R}/orders/{victim_orderId}/items` — same lookup at [CustomerController.cs:146-148](backend/RestaurantApi/Controllers/CustomerController.cs#L146-L148). Non-flash-deal items not in the new payload are removed ([line 161-163](backend/RestaurantApi/Controllers/CustomerController.cs#L161-L163)), so an attacker who knows a foreign `orderId` can wipe its cart by sending `{"items": []}`.
- Create an order at any `TableId`: `POST /api/restaurants/{R}/orders` with `{TableId: any_table_at_R, Items: [...]}`. The `Tables.FindAsync` at [CustomerController.cs:88](backend/RestaurantApi/Controllers/CustomerController.cs#L88) respects the tenant filter and so accepts any table in `R` regardless of which QR the attacker actually holds.
- Read another order's recommendations: `GET /api/restaurants/{R}/orders/{victim_orderId}/recommendations` — the `Orders.FindAsync` at [CustomerController.cs:299](backend/RestaurantApi/Controllers/CustomerController.cs#L299) only checks tenant. The response includes the active `FlashDeal`'s `dealId`, `productId`, and `discountPrice` ([CustomerController.cs:356-368](backend/RestaurantApi/Controllers/CustomerController.cs#L356-L368)) — which feeds directly into [F2](#f2--high--claimdealasync-does-not-verify-that-the-deal-belongs-to-the-order).

The practical exploit difficulty depends on how an attacker obtains a foreign `orderId` or `TableId`. They are `Guid`s, so brute-forcing across the whole namespace is infeasible — but the `tableId` is broadcast on the customer SignalR group `r:{restaurantId}:t:{tableId}` ([TenantGroups.cs:9](backend/RestaurantApi/Hubs/TenantGroups.cs#L9)) (segment 12 reviews this surface), and `orderId` is broadcast to the staff hub on every submit at [CustomerController.cs:67-73](backend/RestaurantApi/Controllers/CustomerController.cs#L67-L73). What matters here is that *the model has no defense*: the only thing standing between a malicious customer at table 1 and table 2's cart is the secrecy of two `Guid`s, neither of which the system treats as a secret.

**Recommended fix sketch (out of scope for this review).** Re-bind every customer write to the QR token: either keep the QR in the path on every customer endpoint (`/api/tables/{qrToken}/orders/...`), or issue a short-lived signed cookie from `GET /tables/{qrToken}/session` that captures `(restaurantId, tableId)` and validate it as an `[Authorize]`-equivalent in subsequent endpoints. Today's design effectively trades a per-table secret for a per-restaurant one, which is what the checklist 🔥 line warns against.

---

## F2 — HIGH — `ClaimDealAsync` does not verify that the deal belongs to the order

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

    var orderItem = new OrderItem
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,             // <-- the attacker's order
        ProductId = deal.ProductId.Value,
        Quantity = 1,
        UnitPriceAtPurchase = deal.DiscountPrice ?? 0m,
        IsFlashDealItem = true,
        WasAiRecommended = false
    };

    _context.OrderItems.Add(orderItem);
    order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
    deal.Status = "claimed";            // <-- the victim's deal
    await _context.SaveChangesAsync();
    ...
}
```

The deal and order are loaded independently. No assertion that `deal.OrderId == order.Id`. The `FlashDealOffer` model carries the originating `OrderId` ([FlashDealOffer.cs:10](backend/RestaurantApi/Models/FlashDealOffer.cs#L10)) and `EvaluateEligibilityAsync` sets it correctly when minting the deal ([FlashDealService.cs:231-238](backend/RestaurantApi/Services/FlashDealService.cs#L231-L238)) — the field is present, just never read at claim time.

The route that reaches this method is `POST /api/restaurants/{restaurantId:guid}/orders/{orderId:guid}/claim-deal/{dealId:guid}` ([CustomerController.cs:276](backend/RestaurantApi/Controllers/CustomerController.cs#L276)) — anonymous, with both ids bound from the URL. Combined with [F1](#f1--high--qr-token-binds-only-the-session-call-subsequent-customer-endpoints-trust-the-url-restaurantid-), an attacker at table 1:

1. Reads the active flash deal for table 2 via `GET /restaurants/{R}/orders/{table2_orderId}/recommendations` ([CustomerController.cs:296-377](backend/RestaurantApi/Controllers/CustomerController.cs#L296-L377)). The response shape ([line 356-368](backend/RestaurantApi/Controllers/CustomerController.cs#L356-L368)) includes `id` (the `dealId`) and `discountPrice`.
2. Calls `POST /restaurants/{R}/orders/{table1_orderId}/claim-deal/{table2_dealId}`.
3. Table 1 receives the discounted product at the discount price the kitchen prepared for table 2.
4. `deal.Status = "claimed"` is persisted, which the next eligibility check at [FlashDealService.cs:54](backend/RestaurantApi/Services/FlashDealService.cs#L54) treats as terminal — table 2's legitimate customer is told "Deal is invalid, expired, or already claimed" at [CustomerController.cs:285](backend/RestaurantApi/Controllers/CustomerController.cs#L285).

The tenant query filter blocks cross-restaurant abuse (both `Order` and `FlashDealOffer` are `ITenantScoped`), so the bug is bounded to a single restaurant. Within a restaurant it's free, because the only check standing between order A and deal B is the secrecy of `dealId` — and the `GET /recommendations` response above leaks it on demand.

**Recommended fix.** After the null-check, add `if (deal.OrderId != order.Id) return false;` ([FlashDealService.cs:52](backend/RestaurantApi/Services/FlashDealService.cs#L52)). The model already carries the link; it just isn't consulted.

---

## F3 — MEDIUM — `CreateOrder` and `AddItemsOrder` do not validate `Quantity > 0` or product availability

[CustomerController.cs:85-141](backend/RestaurantApi/Controllers/CustomerController.cs#L85-L141), `CreateOrder`:

```csharp
foreach (var item in request.Items)
{
    var product = await _context.Products.FindAsync(item.ProductId);
    if (product is null)
        return BadRequest(new { message = $"Product {item.ProductId} not found." });

    fetchedProducts.Add(product);

    order.Items.Add(new OrderItem
    {
        OrderId = order.Id,
        ProductId = item.ProductId,
        Quantity = item.Quantity,                         // <-- any int, including <= 0
        UnitPriceAtPurchase = product.Price,
        IsFlashDealItem = false
    });
}

order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPriceAtPurchase);
```

Two checklist items fail:

- **`IsAvailable`.** `Products.FindAsync` does not filter on `IsAvailable`. A product the admin has just hidden is still orderable — the customer's cached menu is the only thing nominally preventing it, and a malicious caller submits raw `ProductId`s. The customer SPA's [MenuController](backend/RestaurantApi/Controllers/MenuController.cs#L23-L46) filters `IsAvailable == true` correctly at *display* time ([line 33](backend/RestaurantApi/Controllers/MenuController.cs#L33)) — order-time has no such guard.
- **`Quantity > 0` and within limits.** No floor. No ceiling. `request.Items = [{ ProductId: P, Quantity: -10 }]` is accepted; `TotalAmount` goes negative ([line 120](backend/RestaurantApi/Controllers/CustomerController.cs#L120)). `Quantity = 0` produces an `OrderItem` row with no charge contribution — junk data. `Quantity = int.MaxValue` overflows the `decimal` `TotalAmount` calculation (it actually doesn't overflow `decimal` but a 10-digit price × 2.1B quantity exceeds `decimal(10,2)`'s 8-digit-integer headroom and throws at `SaveChanges`, surfacing as a 500). The DTO is bare ([AddItemRequest.cs:20-25](backend/RestaurantApi/DTO/Requests/AddItemRequest.cs#L20-L25)) and has no `[Range]` annotations and no FluentValidation registration.

`AddItemsOrder` ([CustomerController.cs:143-215](backend/RestaurantApi/Controllers/CustomerController.cs#L143-L215)) partially papers over the quantity issue:

```csharp
var incomingProductIds = request.Items.Where(i => i.Quantity > 0).Select(i => i.ProductId).ToList();
...
foreach (var reqItem in request.Items.Where(i => i.Quantity > 0)) { ... }
```

Items with `Quantity <= 0` are silently dropped (treated as a soft delete), not rejected — there's no `400` if every item is zero, and the upper bound is still absent. The `IsAvailable` gap is the same.

**Recommended fix.** Centralise the validation in a FluentValidation rule (the pattern already used by [ProductValidator.cs](backend/RestaurantApi/Validators/ProductValidator.cs) for admin writes): `Quantity >= 1 && Quantity <= N` (pick a per-item cap like 99), and at the controller, replace `Products.FindAsync(item.ProductId)` with `Products.FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsAvailable == true)`. Mirror it in `AddItemsOrder`.

---

## F4 — MEDIUM — `POST /orders` has no idempotency key — retries create duplicate draft orders

Checklist (segment 09):
> Idempotency key supported for `POST /orders` to handle network retries without duplicate orders.

[CustomerController.cs:85-141](backend/RestaurantApi/Controllers/CustomerController.cs#L85-L141) accepts a `CreateOrderRequest` ([AddItemRequest.cs:6-10](backend/RestaurantApi/DTO/Requests/AddItemRequest.cs#L6-L10)) with `TableId` + `Items` and unconditionally inserts a new `Order`. There is no `Idempotency-Key` header inspection, no client-token field in the DTO, no `(TableId, Status='draft')` uniqueness check.

The customer-flow consequence: a phone with a flaky connection sends `POST /orders`, the request goes through, the response is lost on the way back, the SPA's retry creates a second draft. The kitchen sees the *first* draft only after the customer hits `/submit` — but the second draft sits stranded in the DB. Worse: the `GetTableSession` endpoint's lookup `FirstOrDefaultAsync(o => o.TableId == table.Id && o.Status == "draft")` ([CustomerController.cs:229](backend/RestaurantApi/Controllers/CustomerController.cs#L229)) has non-deterministic ordering, so on next session refresh the SPA may pick *either* draft — surprising UX.

The same race exists in `GetTableSession` itself when called twice in parallel: both calls miss the lookup, both insert, neither has anything to deduplicate against ([line 231-243](backend/RestaurantApi/Controllers/CustomerController.cs#L231-L243)). See also [F7](#f7--low--gettablesession-has-a-race-on-the-implicit-draft-order-creation).

**Recommended fix.** Accept an `Idempotency-Key` header on `POST /orders` and look up an existing order with the same key (store on `Order` as a new `IdempotencyKey` column with a unique-per-table index). Alternatively: a unique partial index on `(TableId)` where `Status='draft'` makes the duplicate insert fail at the DB layer; the controller catches the `DbUpdateException` and returns the existing draft.

---

## F5 — LOW — No rate limit on order placement (checklist asks "document if absent")

`Program.cs` does not register `AddRateLimiter`, no `[EnableRateLimiting]` on any controller, no middleware-level throttling ([Program.cs](backend/RestaurantApi/Program.cs) is short enough to grep visually).

Customer-surface endpoints — all anonymous — have no per-IP, per-table, or per-restaurant ceiling. A malicious caller can:

- Flood `POST /orders` to fill the DB with garbage drafts.
- Flood `POST /orders/{id}/submit` against a single restaurant to spam the staff hub's `OrderSubmitted` notifications ([CustomerController.cs:65-73](backend/RestaurantApi/Controllers/CustomerController.cs#L65-L73)).
- Flood `GET /restaurants/{rid}/orders/{id}/recommendations` to drive AI-service spend (the recommendation path makes an outbound HTTP call to the AI service at [RecommendationService.cs:63-64](backend/RestaurantApi/Services/RecommendationService.cs#L63-L64); a 10-second per-call timeout caps each call but not the call rate).

The checklist explicitly says "document if absent", which is the floor; for a production deployment this surface needs a rate limit before launch, and the AI-service call in particular needs a per-token-bucket guard since it has direct cost.

---

## F6 — LOW — `ClaimFlashDeal` leaks `ex.Message` to the client on 500

[CustomerController.cs:276-294](backend/RestaurantApi/Controllers/CustomerController.cs#L276-L294):

```csharp
public async Task<IActionResult> ClaimFlashDeal(Guid orderId, Guid dealId)
{
    try
    {
        var success = await _flashDealService.ClaimDealAsync(orderId, dealId);
        if (!success)
            return BadRequest(new { message = "Deal is invalid, expired, or already claimed." });

        return Ok(new { message = "Flash deal successfully added to your order!" });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { message = $"Internal error claiming deal: {ex.Message}" });
    }
}
```

A blanket `catch (Exception ex)` that interpolates `ex.Message` into a public response. EF Core's exception messages frequently include the SQL fragment, the parameter values, and (for constraint violations) the constraint name — all internal information. This is the only controller in the whole codebase that returns an exception message verbatim (`grep "ex.Message"` finds it only here in `backend/RestaurantApi/Controllers/`).

**Recommended fix.** Drop the try/catch — let the global exception handler convert it to a generic 500. Or log the exception and return `new { message = "Internal error claiming deal." }` with no interpolation.

---

## F7 — LOW — `GetTableSession` has a race on the implicit draft-order creation

[CustomerController.cs:226-243](backend/RestaurantApi/Controllers/CustomerController.cs#L226-L243):

```csharp
var draftOrder = await _context.Orders
    .Include(o => o.Items)
    .ThenInclude(i => i.Product)
    .FirstOrDefaultAsync(o => o.TableId == table.Id && o.Status == "draft");

if (draftOrder == null)
{
    draftOrder = new Order
    {
        TableId = table.Id,
        Status = "draft",
        PaymentStatus = "unpaid",
        TotalAmount = 0
    };

    _context.Orders.Add(draftOrder);
    await _context.SaveChangesAsync();
}
```

Two concurrent requests (e.g. the customer SPA refreshes on tab focus while a slow first session call is still in flight) both miss the `FirstOrDefault`, both `Add`, both `SaveChanges`. There's no unique partial index on `(TableId)` where `Status='draft'` ([AppDbContext.cs:83-102](backend/RestaurantApi/Data/AppDbContext.cs#L83-L102) declares an index on `(RestaurantId, OrderNumber)` and on `RestaurantId` only — nothing on draft-per-table).

Same root cause as [F4](#f4--medium--post-orders-has-no-idempotency-key--retries-create-duplicate-draft-orders), different symptom. The orphaned draft never expires — there's no TTL on draft orders in [OrderFinalizationService.cs](backend/RestaurantApi/Services/OrderFinalizationService.cs) (segment 10 reviews that surface).

**Recommended fix.** Either accept this as a known minor leak (and add an OrderFinalizationService rule that nukes drafts older than N hours), or add the unique partial index `CREATE UNIQUE INDEX ... ON orders (table_id) WHERE status = 'draft' AND deleted_at IS NULL;` and convert the duplicate insert into a "fetch existing" path.

---

## Appendix

### Customer route inventory

| Method | Route | Endpoint | Anonymous? |
|---|---|---|---|
| GET | `/api/tables/{qrToken:guid}/session` | `CustomerController.GetTableSession` | ✓ (intended) |
| POST | `/api/restaurants/{rid}/orders` | `CustomerController.CreateOrder` | ✓ |
| PATCH | `/api/restaurants/{rid}/orders/{orderId}/items` | `CustomerController.AddItemsOrder` | ✓ |
| POST | `/api/restaurants/{rid}/orders/{orderId}/submit` | `CustomerController.SubmitOrder` | ✓ |
| POST | `/api/restaurants/{rid}/orders/{orderId}/claim-deal/{dealId}` | `CustomerController.ClaimFlashDeal` | ✓ |
| GET | `/api/restaurants/{rid}/orders/{id}/recommendations` | `CustomerController.GetRecommendation` | ✓ |
| GET | `/api/restaurants/{rid}/menu` | `MenuController.GetMenu` | ✓ |
| GET | `/api/restaurants/{rid}/menu/{categoryId}` | `MenuController.GetMenuByCategory` | ✓ |

There is no class-level `[AllowAnonymous]` on either controller and no class-level `[Authorize]` — anonymous because `AddAuthorization()` ([Program.cs:80](backend/RestaurantApi/Program.cs#L80)) is called without a fallback policy. Worth a one-line `// [AllowAnonymous] — customer surface, gated by QR-token resolution in TenantResolutionMiddleware` comment for the next reviewer; not a finding.

### Status-code review

| Scenario | Expected | Actual |
|---|---|---|
| QR token does not resolve a table | `404` | `404` — [CustomerController.cs:223-224](backend/RestaurantApi/Controllers/CustomerController.cs#L223-L224). |
| `restaurantId` does not exist | `404` | `404` — middleware short-circuits at [TenantResolutionMiddleware.cs:40-45](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L40-L45). |
| `restaurantId` exists but `!IsActive` | `403` | `403` — [TenantResolutionMiddleware.cs:47-52](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L47-L52). |
| Body has cross-tenant `TableId` / `ProductId` | `404` / `400` | `404` for table ([line 89-90](backend/RestaurantApi/Controllers/CustomerController.cs#L89-L90)), `400` for product ([line 105-106](backend/RestaurantApi/Controllers/CustomerController.cs#L105-L106)) — both via the global query filter returning null. |
| Empty cart on submit | `400` | `400` — [CustomerController.cs:44-45](backend/RestaurantApi/Controllers/CustomerController.cs#L44-L45). |
| Order not in draft | `404` | `404` — by design at [line 39](backend/RestaurantApi/Controllers/CustomerController.cs#L39), [line 148](backend/RestaurantApi/Controllers/CustomerController.cs#L148). NB: this masks "exists but already submitted" as "not found", which is fine for a public surface. |
| Invalid deal claim | `400` | `400` — [line 284-286](backend/RestaurantApi/Controllers/CustomerController.cs#L284-L286). |
| Server-side exception in claim | `500` with `ex.Message` | See [F6](#f6--low--claimflashdeal-leaks-exmessage-to-the-client-on-500). |

### Trailing roadmap question

> ↳ When a QR token is rotated by an admin, do in-flight customer sessions fail cleanly (401) or silently break?

Today: silently survive, not break. Subsequent calls use the URL `restaurantId`, not the QR token, so a customer whose QR has been rotated keeps operating against `restaurantId` indefinitely. The `Tables.QrToken` change is invisible to them. New scans hit `GET /tables/{newQr}/session` and get a fresh draft order. This is a direct corollary of [F1](#f1--high--qr-token-binds-only-the-session-call-subsequent-customer-endpoints-trust-the-url-restaurantid-): with no per-table authority on the live request, there's no per-table revocation either.

### Cross-segment links

- The `IgnoreQueryFilters()` call at [CustomerController.cs:54-57](backend/RestaurantApi/Controllers/CustomerController.cs#L54-L57) is the one site flagged by segment 04 [F4](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md#f4--low--missing-justifying-comment-on-ignorequeryfilters-in-submitorder) (missing justification).
- The `OrderNumber` race the comment at [line 50-53](backend/RestaurantApi/Controllers/CustomerController.cs#L50-L53) admits to is the same issue the schema-side `(RestaurantId, OrderNumber)` index is non-unique for — see segment 06 for the index-side discussion.
- The free-form `Order.Status = "draft"` / `"submitted"` strings are flagged in segment 07 [F2](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f2--medium--orderstatus-paymentstatus-flashdealofferstatus-invitationstatus-are-free-form-strings-with-no-db-check-constraint).
- The `StampTenant` modification gap from segment 04 [F1](CODE_REVIEW_SEGMENT_04_MULTI_TENANCY.md#f1--high--savechanges-does-not-forbid-restaurantid-changes-on-modified-entities-) / segment 07 [F1](CODE_REVIEW_SEGMENT_07_DOMAIN_MODELS.md#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries) is what makes the customer surface's tenant-via-filter posture load-bearing here too: no controller-side defense exists if a future code path mutates `Order.RestaurantId`.

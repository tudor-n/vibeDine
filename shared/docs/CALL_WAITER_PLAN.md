# Call-Waiter Button — Implementation Plan

## Context

Customers currently have no way to flag a waiter from the customer app. The `OrderSentView` already renders a "Call the waiter" button but its handler is a no-op (`OrderingView.vue:164`). We want that capability available **on every screen**, throttled by a 2-minute cooldown per table, with the alert pushed in realtime to waiters via SignalR and the table number visible so a waiter can recognize their own table.

The waiter-facing UI itself is **deferred** — separating the waiter interface from the admin app requires a broader design discussion (auth/role split, what other views belong there). This plan ships the customer button and the full backend so the future waiter app can simply consume the SignalR events and REST endpoints already in place.

## Scope

**In:** customer floating button + cooldown UX, backend `HelpRequest` model + endpoints, SignalR broadcasts to both waiter group and customer table.

**Out (deferred):** the `frontend-waiter` app itself, any `WaitStaff` membership role work, waiter login flow.

## A. Customer side (`frontend-customer/`)

### A1. New floating component — `frontend-customer/src/components/CallWaiterButton.vue` (CREATE)

Pattern: mirror [FlashDealIcon.vue](../../frontend-customer/src/components/FlashDealIcon.vue) — 56px round FAB, SVG countdown ring during cooldown, pulse animation. Differences:
- **Position:** `position: absolute; bottom: 120px; left: 16px; z-index: 55;` — pinned **left** so it coexists with FlashDealIcon (which owns the bottom-right slot).
- **Icon:** inline SVG of a raised-hand / bell glyph (replace the bolt path in FlashDealIcon at line 136).
- **Color:** `T.accent` for idle, ring color also `T.accent`. When `cooldownRemaining > 0`, render the ring + `mm:ss` text and set `disabled`.
- **Behavior:** tap → `store.callWaiter()`. While `isPending`, show a small spinner state. While cooling down, click is suppressed.
- **a11y:** uses `t('waiter.iconLabel')` for `aria-label`.

### A2. New Pinia store — `frontend-customer/src/stores/useHelpCallStore.ts` (CREATE)

State:
- `cooldownEndsAt: number | null` — millisecond timestamp, persisted under `helpCall.cooldownEndsAt` in `sessionStorage` (mirrors how [useSessionStore.ts](../../frontend-customer/src/stores/useSessionStore.ts) persists keys).
- `isPending: boolean`, `lastError: string | null`
- `now: ref(Date.now())` ticked every 1 s **only while a cooldown is active** (start/stop timer in watchers to avoid background work).

Computed: `cooldownRemaining = max(0, ceil((cooldownEndsAt - now) / 1000))`.

Methods:
- `callWaiter()` — reads `tableId` from `useSessionStore()`, calls the new API helper. On 200, `applyCooldown(Date.parse(res.cooldownEndsAt))`. On 429, `applyCooldown(Date.now() + retryAfterSeconds * 1000)`. On other errors, set `lastError`.
- `applyCooldown(endsAtMs)` — writes state + sessionStorage, starts ticker.
- `clearCooldown()` — wipes state + sessionStorage, stops ticker. Called by the `WaiterOnTheWay` SignalR handler.

Const: `COOLDOWN_TOTAL_SECONDS = 120` exposed so the ring can compute its progress (`remaining / 120`).

### A3. API client — `frontend-customer/src/services/api-client.ts` (MODIFY)

The existing `fetcher` throws a plain `Error` with no status code, which loses the 429 payload. Add a small dedicated helper that reads status + body for this one route — keeps `fetcher` simple:

```ts
callWaiter: async (tableId: string): Promise<
  | { ok: true; cooldownEndsAt: string }
  | { ok: false; status: number; retryAfterSeconds?: number; message?: string }
> => {
  const res = await fetch(`${BASE_URL}/api/restaurants/${rid()}/tables/${tableId}/call-waiter`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
  })
  const body = await res.json().catch(() => ({}))
  if (res.ok) return { ok: true, cooldownEndsAt: body.cooldownEndsAt }
  return { ok: false, status: res.status, retryAfterSeconds: body.retryAfterSeconds, message: body.message }
}
```

### A4. WebSocket handler — `frontend-customer/src/composables/useWebSocket.ts` (MODIFY)

Inside the existing `if (data.type === 1 && data.target)` chain at [useWebSocket.ts:59-95](../../frontend-customer/src/composables/useWebSocket.ts#L59-L95), add:

```ts
if (data.target === 'WaiterOnTheWay') {
  const helpCallStore = useHelpCallStore()
  helpCallStore.clearCooldown()
}
```

No new group join needed — the backend pushes to `TenantGroups.Table(restaurantId, tableId)` which the customer already joins on connect ([useWebSocket.ts:46-51](../../frontend-customer/src/composables/useWebSocket.ts#L46-L51)).

### A5. Mount in master layout — `frontend-customer/src/view/OrderingView.vue` (MODIFY)

After the existing `<FlashDealIcon>` block at [OrderingView.vue:174-177](../../frontend-customer/src/view/OrderingView.vue#L174-L177), add:

```vue
<CallWaiterButton v-if="screen !== 'language' && !showSessionError && !flashOverlayActive" />
```

Hidden on the language picker (no session yet), on the session-error overlay, and during the flash-deal modal. Also import the component.

### A6. Remove the now-duplicate inline button — `frontend-customer/src/components/screens/OrderSentView.vue` (MODIFY)

- Delete the `<button class="secondary" ... @click="emit('call-waiter')">` element ([OrderSentView.vue:195-199](../../frontend-customer/src/components/screens/OrderSentView.vue#L195-L199)).
- Remove the `(e: 'call-waiter'): void` entry from `defineEmits` ([OrderSentView.vue:13](../../frontend-customer/src/components/screens/OrderSentView.vue#L13)).
- Update `.cta` CSS: change `grid-template-columns: 1fr 1fr` → `grid-template-columns: 1fr` so the remaining "Order Again" button takes full width.
- Remove `@call-waiter="() => undefined"` from the parent in [OrderingView.vue:164](../../frontend-customer/src/view/OrderingView.vue#L164).

### A7. Locale keys — `frontend-customer/src/composables/useLocale.ts` (MODIFY)

Add to both the `ro` block (around line 94) and the `en` block (around line 201):
- `waiter.iconLabel` — "Chemați chelnerul" / "Call the waiter"
- `waiter.calling` — "Se trimite…" / "Calling…"
- `waiter.onTheWay` — "Chelnerul vine spre dvs." / "A waiter is on the way"
- `waiter.cooldownHint` — "Mai puteți chema în" / "You can call again in"
- `waiter.errorGeneric` — "Nu s-a putut trimite. Încercați din nou." / "Could not send. Try again."

Keep the existing `sent.callWaiter` key untouched — removing it would touch every locale file for no functional gain.

## B. Backend (`backend/RestaurantApi/`)

### B1. New entity — `backend/RestaurantApi/Models/HelpRequest.cs` (CREATE)

```csharp
namespace RestaurantApi.Models;

public class HelpRequest : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public RestaurantTable? Table { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "open";   // "open" | "acknowledged"
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
}
```

### B2. DbContext — `backend/RestaurantApi/Data/AppDbContext.cs` (MODIFY)

- Add `public DbSet<HelpRequest> HelpRequests => Set<HelpRequest>();`.
- In `OnModelCreating`, configure snake_case columns to match the project convention (table `help_requests`; columns `id`, `restaurant_id`, `table_id`, `created_at`, `status`, `acknowledged_at`, `acknowledged_by_user_id`).
- Index for cooldown lookup: `.HasIndex(h => new { h.TableId, h.CreatedAt })`.
- FK: `.HasOne(h => h.Table).WithMany().HasForeignKey(h => h.TableId).OnDelete(DeleteBehavior.Cascade)`.
- Register the same tenant query filter pattern used by other `ITenantScoped` entities (search the existing file for `HasQueryFilter` on `Order` and mirror it for `HelpRequest`). Tenant stamping via `StampTenant` runs in `SaveChangesAsync` and will fill `RestaurantId` on insert.

### B3. EF migration — `backend/RestaurantApi/Migrations/<timestamp>_AddHelpRequests.cs` (CREATE)

Generate with `dotnet ef migrations add AddHelpRequests --project backend/RestaurantApi`. Verify it only adds the `help_requests` table + index + FK and doesn't pull in unrelated drift.

### B4. New controller — `backend/RestaurantApi/Controllers/HelpController.cs` (CREATE)

```csharp
[ApiController]
[Route("api")]
public class HelpController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly IHubContext<CustomerHub> _customerHub;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    public HelpController(AppDbContext db, IHubContext<StaffHub> staffHub, IHubContext<CustomerHub> customerHub)
    { _db = db; _staffHub = staffHub; _customerHub = customerHub; }

    // Anonymous — customer endpoint. TenantResolutionMiddleware sets the tenant
    // from {restaurantId}, so the query filter scopes Tables/HelpRequests correctly.
    [HttpPost("restaurants/{restaurantId:guid}/tables/{tableId:guid}/call-waiter")]
    public async Task<IActionResult> CallWaiter(Guid restaurantId, Guid tableId)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Id == tableId);
        if (table is null) return NotFound(new { message = "Table not found." });

        var cutoff = DateTime.UtcNow - Cooldown;
        var existing = await _db.HelpRequests
            .Where(h => h.TableId == tableId && h.Status == "open" && h.CreatedAt > cutoff)
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            var retry = (int)Math.Ceiling((existing.CreatedAt + Cooldown - DateTime.UtcNow).TotalSeconds);
            Response.Headers["Retry-After"] = retry.ToString();
            return StatusCode(429, new { message = "Already called.", retryAfterSeconds = retry });
        }

        var req = new HelpRequest { TableId = tableId };
        _db.HelpRequests.Add(req);
        await _db.SaveChangesAsync();

        await _staffHub.Clients
            .Group(TenantGroups.Role(restaurantId, "WaitStaff"))
            .SendAsync("HelpRequested", new {
                requestId = req.Id, tableId, tableNumber = table.TableNumber, createdAt = req.CreatedAt
            });

        return Ok(new { requestId = req.Id, cooldownEndsAt = req.CreatedAt + Cooldown });
    }

    // Stubbed for the future waiter app. Auth model is deliberately TBD —
    // keep [Authorize] as a placeholder so endpoints aren't reachable anonymously
    // until the waiter interface design lands. See out-of-scope note.
    [Authorize]
    [HttpGet("restaurants/{restaurantId:guid}/help-requests")]
    public async Task<IActionResult> List(Guid restaurantId)
    {
        var items = await _db.HelpRequests
            .Where(h => h.Status == "open")
            .OrderBy(h => h.CreatedAt)
            .Join(_db.Tables, h => h.TableId, t => t.Id, (h, t) => new {
                requestId = h.Id, tableId = h.TableId, tableNumber = t.TableNumber, createdAt = h.CreatedAt
            })
            .ToListAsync();
        return Ok(items);
    }

    [Authorize]
    [HttpPost("restaurants/{restaurantId:guid}/help-requests/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid restaurantId, Guid id)
    {
        var req = await _db.HelpRequests.FirstOrDefaultAsync(h => h.Id == id);
        if (req is null) return NotFound();
        if (req.Status != "open") return Ok(new { alreadyAcknowledged = true });

        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        req.Status = "acknowledged";
        req.AcknowledgedAt = DateTime.UtcNow;
        req.AcknowledgedByUserId = Guid.TryParse(userIdRaw, out var uid) ? uid : null;
        await _db.SaveChangesAsync();

        await _staffHub.Clients
            .Group(TenantGroups.Role(restaurantId, "WaitStaff"))
            .SendAsync("HelpAcknowledged", new { requestId = req.Id });

        await _customerHub.Clients
            .Group(TenantGroups.Table(restaurantId, req.TableId))
            .SendAsync("WaiterOnTheWay", new { requestId = req.Id });

        return Ok();
    }
}
```

**Notes:**
- Reuse the existing role string `"WaitStaff"` already used by [FlashDealService.cs:85](../../backend/RestaurantApi/Services/FlashDealService.cs#L85).
- Use `TenantGroups.Role` / `TenantGroups.Table` helpers from [Hubs/TenantGroups.cs](../../backend/RestaurantApi/Hubs/TenantGroups.cs).
- DI for `IHubContext<CustomerHub>` and `IHubContext<StaffHub>` is automatic — both hubs are mapped in `Program.cs`.
- Pattern follows the existing tenant-scoped anonymous customer endpoint at [CustomerController.cs:218-275](../../backend/RestaurantApi/Controllers/CustomerController.cs#L218-L275).

## C. Out of scope (next PR)

- **`frontend-waiter/` app scaffold** — Vue 3 + Pinia client with a Help Calls view, SignalR client at `/hubs/staff`, calling `JoinRoleGroup(restaurantId, "WaitStaff")` and listening for `HelpRequested` / `HelpAcknowledged`.
- **Waiter auth / `WaitStaff` `MembershipRole`** — currently only `OrgOwner` and `RestaurantAdmin` exist; either extend the enum + `ITenantAccess.CanAccessRestaurantAsync`, or reuse admin login. To be decided alongside the waiter app design.
- **Active orders + logs views** in the waiter app.

The acknowledge endpoint and the `WaiterOnTheWay` customer-side handler are kept in this PR so the waiter app is plug-and-play later. Acknowledge can be exercised manually via Postman / `curl` for testing.

## Verification

1. `dotnet ef database update --project backend/RestaurantApi` — applies `AddHelpRequests`.
2. Start backend (`dotnet run` in `backend/RestaurantApi`), then `frontend-customer` (`npm run dev`).
3. Visit `http://localhost:3000/{qrToken}` for a known table, pick a language, land on the menu screen. Confirm the new FAB is visible at bottom-left.
4. Tap it. Confirm:
   - HTTP 200 from `/api/restaurants/{rid}/tables/{tableId}/call-waiter` in DevTools network.
   - Ring + countdown render and tick down from `2:00`.
   - One row in `help_requests` table (`status=open`).
5. Tap again within 2 minutes. Confirm HTTP 429 with `retryAfterSeconds`. UI stays in cooldown.
6. Reload the customer tab mid-cooldown. Confirm the ring resumes (sessionStorage persists `cooldownEndsAt`).
7. From a separate terminal, simulate the waiter ack:
   ```
   curl -X POST http://localhost:5005/api/restaurants/{rid}/help-requests/{requestId}/acknowledge \
        -H "Authorization: Bearer {adminJwt}"
   ```
   Confirm:
   - `help_requests.status` → `acknowledged`, `acknowledged_at` set.
   - Customer FAB resets to idle (the `WaiterOnTheWay` event arrived on the table group).
   - A subsequent customer tap succeeds immediately.
8. Navigate across screens (language → pebbles → menu → cart → sent). Confirm the FAB:
   - **Hidden** on language picker.
   - **Visible** on pebbles, menu, cart, sent.
   - Does not collide with the flash-deal icon (left vs right) or theme toggle (top vs bottom).
9. Visual regression: open the OrderSent screen, confirm the bottom "Order Again" button now spans full width (the duplicate inline "Call Waiter" was removed).

## Files

**Create (4)**
- [frontend-customer/src/components/CallWaiterButton.vue](../../frontend-customer/src/components/CallWaiterButton.vue)
- [frontend-customer/src/stores/useHelpCallStore.ts](../../frontend-customer/src/stores/useHelpCallStore.ts)
- [backend/RestaurantApi/Models/HelpRequest.cs](../../backend/RestaurantApi/Models/HelpRequest.cs)
- [backend/RestaurantApi/Controllers/HelpController.cs](../../backend/RestaurantApi/Controllers/HelpController.cs)
- Migration file under [backend/RestaurantApi/Migrations/](../../backend/RestaurantApi/Migrations/) (generated)

**Modify (6)**
- [frontend-customer/src/services/api-client.ts](../../frontend-customer/src/services/api-client.ts) — add `callWaiter` helper.
- [frontend-customer/src/composables/useWebSocket.ts](../../frontend-customer/src/composables/useWebSocket.ts) — handle `WaiterOnTheWay`.
- [frontend-customer/src/view/OrderingView.vue](../../frontend-customer/src/view/OrderingView.vue) — mount FAB, remove no-op listener.
- [frontend-customer/src/components/screens/OrderSentView.vue](../../frontend-customer/src/components/screens/OrderSentView.vue) — remove duplicate inline button + emit.
- [frontend-customer/src/composables/useLocale.ts](../../frontend-customer/src/composables/useLocale.ts) — add `waiter.*` keys in `ro` + `en`.
- [backend/RestaurantApi/Data/AppDbContext.cs](../../backend/RestaurantApi/Data/AppDbContext.cs) — DbSet + model config + tenant filter.

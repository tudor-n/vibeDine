# Device-Bound Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tie customer QR sessions to a device ID so re-scanning restores orders/flash deals, expire an open link after a configurable time (30 s dev / 15 min prod) without losing the device session, wipe the session only after 3 h idle, and show the diner one cumulative running total across ordering rounds.

**Architecture:** New tenant-scoped `DeviceSession` entity resolved during `POST /api/tables/{qrToken}/join` from a client-persisted `deviceId`; orders carry a nullable `DeviceSessionId` so the join response can return session history (cumulative total + pending flash deal). Link expiry is a frontend UX gate driven by a backend-configured `linkExpirySeconds`. Kitchen flow untouched.

**Tech Stack:** .NET 8 + EF Core/PostgreSQL (Testcontainers tests), Vue 3 + TS + Pinia (vitest).

**Spec:** `docs/superpowers/specs/2026-07-22-device-sessions-design.md` — read it first.

## Global Constraints

- Multi-tenancy invariant: `DeviceSession` implements `ITenantScoped`, gets snake_case mapping + `restaurant_id` index + global query filter in `AppDbContext` (follow the existing ~15 identical blocks). Never accept a client `restaurantId`; everything derives from `qrToken`.
- Order statuses are exact string literals: `draft`, `pending_dispatch`, `submitted`, `preparing`, `ready`, `completed`, `cancelled`, `merged`.
- Money is `decimal` server-side; use `roundMoney` from `utils/money.ts` client-side.
- Customer UI strings need BOTH `ro` and `en` entries in `frontend-customer/src/composables/useLocale.ts`.
- Config section name: `CustomerSession`; env override convention `CustomerSession__LinkExpirySeconds` etc.
- Git: commit after each task, loose Conventional Commits (`feat(session): …`), **no Co-Authored-By lines**.
- Do NOT touch the untracked folder `White label branding feature (2)/`.
- Backend tests require Docker running. Run targeted: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~DeviceSession"`.

---

### Task 1: Backend foundation — options, entity, order column, migration

**Files:**
- Create: `backend/RestaurantApi/Models/DeviceSession.cs`
- Create: `backend/RestaurantApi/Options/CustomerSessionOptions.cs` (new folder OK, or place beside `Auth/JwtOptions.cs` style at `backend/RestaurantApi/CustomerSessionOptions.cs` — match `JwtOptions` namespace conventions; prefer `Models`-adjacent simplicity: put it in `backend/RestaurantApi/Configuration/CustomerSessionOptions.cs` only if a `Configuration` folder exists, otherwise next to `JwtOptions.cs` pattern under a new `Options/` folder)
- Modify: `backend/RestaurantApi/Models/Order.cs` (~line 14, near `GuestId`)
- Modify: `backend/RestaurantApi/Data/AppDbContext.cs` (DbSet ~line 22-35, mapping block near `Order` block ~line 144-177, tenant filter section ~line 554-575)
- Modify: `backend/RestaurantApi/Program.cs` (options binding, near `Configure<MenuImportOptions>` ~line 235)
- Modify: `backend/RestaurantApi/appsettings.json` + `backend/RestaurantApi/appsettings.Development.json`
- Create (generated): `backend/RestaurantApi/Migrations/*_AddDeviceSessions.cs`

**Interfaces (later tasks rely on):**
- `class DeviceSession { Guid Id; Guid RestaurantId; Guid TableId; RestaurantTable? Table; string DeviceId; DateTime CreatedAt; DateTime LastSeenAt; }`
- `class CustomerSessionOptions { const string SectionName = "CustomerSession"; int LinkExpirySeconds = 900; int SessionLifetimeMinutes = 180; }`
- `Order.DeviceSessionId` : `Guid?`
- `AppDbContext.DeviceSessions` : `DbSet<DeviceSession>`

- [ ] **Step 1: Entity**

```csharp
namespace RestaurantApi.Models;

/// <summary>
/// One diner device's visit at a table, keyed by a client-persisted deviceId.
/// Survives order submission (unlike TableGuest, whose seat is freed on submit)
/// so a re-scan restores the visit: submitted rounds, running total, pending
/// flash deal. Swept lazily on join once LastSeenAt falls outside
/// CustomerSessionOptions.SessionLifetimeMinutes.
/// </summary>
public class DeviceSession : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public RestaurantTable? Table { get; set; }
    public string DeviceId { get; set; } = string.Empty; // client UUID, ≤64 chars
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Options class** (mirror `JwtOptions.cs` shape/namespace style)

```csharp
public class CustomerSessionOptions
{
    public const string SectionName = "CustomerSession";
    /// <summary>How long an open customer tab stays usable before demanding a re-scan.</summary>
    public int LinkExpirySeconds { get; set; } = 900;
    /// <summary>Sliding idle window after which a device session is wiped on the next join.</summary>
    public int SessionLifetimeMinutes { get; set; } = 180;
}
```

- [ ] **Step 3: `Order.DeviceSessionId`** — add next to `GuestId` with a doc comment; FK configured `SET NULL` so deleting a session keeps orders as history:

```csharp
// The device-bound visit this order belongs to; null for legacy orders and
// after the session is swept (orders outlive sessions as plain history).
public Guid? DeviceSessionId { get; set; }
```

- [ ] **Step 4: AppDbContext** — `DbSet<DeviceSession> DeviceSessions`, snake_case mapping block (`device_sessions`, columns `id`, `restaurant_id`, `table_id`, `device_id` with `HasMaxLength(64)`, `created_at`, `last_seen_at`), `HasIndex(x => new { x.TableId, x.DeviceId })`, `HasIndex(x => x.RestaurantId)`, FK to table cascade. On `Order` mapping: column `device_session_id`, index, FK → `DeviceSession` with `OnDelete(DeleteBehavior.SetNull)`. Tenant filter line identical to the others:
`modelBuilder.Entity<DeviceSession>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);`

- [ ] **Step 5: Program.cs binding + appsettings** — `builder.Services.Configure<CustomerSessionOptions>(builder.Configuration.GetSection(CustomerSessionOptions.SectionName));` next to the MenuImport binding. `appsettings.json`: `"CustomerSession": { "LinkExpirySeconds": 900, "SessionLifetimeMinutes": 180 }`. `appsettings.Development.json`: `"CustomerSession": { "LinkExpirySeconds": 30, "SessionLifetimeMinutes": 180 }`.

- [ ] **Step 6: Migration** — `dotnet ef migrations add AddDeviceSessions --project backend/RestaurantApi`. Inspect the generated Up/Down: new table + `device_session_id` column + indexes + `SET NULL` FK. Build: `dotnet build backend/RestaurantApi` → 0 errors.

- [ ] **Step 7: Commit** — `feat(session): add DeviceSession entity, order link and config`

---

### Task 2: Backend behavior — join resolves device session, cumulative orders, deal restore (TDD)

**Files:**
- Modify: `backend/RestaurantApi/Controllers/PartyController.cs`
- Modify: `backend/RestaurantApi/DTO/Requests/JoinPartyRequest.cs` (find exact path via grep `class JoinPartyRequest`)
- Modify: `backend/RestaurantApi/Controllers/CustomerController.cs` (`SubmitOrder`, ~line 54-140)
- Create: `backend/RestaurantApi.Tests/DeviceSessionTests.cs` (follow an existing integration test class using `TestWebApplicationFactory` — grep for existing Party/Customer controller tests and mirror their setup helpers)

**Interfaces:**
- Consumes Task 1 (`DeviceSession`, `CustomerSessionOptions`, `Order.DeviceSessionId`).
- Produces (wire contract, camelCase JSON — Task 3/4 depend on exact names):
  - `JoinPartyRequest.DeviceId` : `string?`
  - Join response gains: `session: { id: Guid, linkExpirySeconds: int, cumulativeTotal: decimal, orders: OrderResponse[], pendingDeal: { dealId: Guid, orderId: Guid, productId: Guid, productName: string, imageUrl: string?, originalPrice: decimal, discountPrice: decimal, expiresAt: DateTime (UTC ISO), quantity: int } | null } | null`
  - `session` is `null` iff the request carried no usable `deviceId` (back-compat).

**Behavior requirements:**
1. In `Join`, after the table lookup and `PurgeStaleGuestsAsync`, add `PurgeExpiredDeviceSessionsAsync(table.Id)`: `ExecuteDeleteAsync` on `DeviceSessions` where `TableId == table.Id && LastSeenAt < now − SessionLifetimeMinutes`. (FK `SET NULL` detaches orders automatically at the DB level — verify migration did configure it.)
2. Resolve session when `body?.DeviceId` is a non-empty string (trim; reject >64 chars by treating as absent): find by `(TableId, DeviceId)`; reuse + `LastSeenAt = now`, else create. Inject `IOptions<CustomerSessionOptions>` into the controller.
3. Stamp the guest draft: `draft.DeviceSessionId = session.Id` (both fresh and pre-existing drafts; save).
4. Session orders query (single query, then in-memory shaping): orders with `DeviceSessionId == session.Id` and `Status ∈ { pending_dispatch, submitted, preparing, ready, completed }`, **including soft-deleted rows** (`DeletedAt` is NOT filtered — completed orders get soft-deleted 45 s after ready; they still count toward the visit). Include `Items.ThenInclude(Product)`. `cumulativeTotal` = sum over orders of `Items.Sum(q*unitPrice)` (live from items, like `BuildPartyAsync` — stored `TotalAmount` is stale on drafts and recomputed elsewhere). Reuse `MapOrder` for each.
5. `pendingDeal`: query `FlashDealOffers` (check exact DbSet name) for `OrderId ∈ sessionOrderIds && Status == "pending" && ExpiresAt > now`, newest first, take 1. Map with the **same field names as the `FlashDealAvailable` SignalR payload** (read `Services/FlashDealService.cs` ~lines 207-219 and copy the anonymous-object property names exactly — the SPA reuses one mapping for both). Load product name/image/price for it.
6. `SubmitOrder` (CustomerController): after it loads the order, if `order.DeviceSessionId` is set, bump that session's `LastSeenAt = DateTime.UtcNow` (a submit proves the diner is still here; it extends the wipe window).
7. Existing behavior with no `deviceId` must be byte-for-byte unchanged except the added `session: null` field.

- [ ] **Step 1: Write failing integration tests** in `DeviceSessionTests.cs` (mirror existing factory/fixture patterns; seed a restaurant+table via the same helpers other tests use). Test methods:

```csharp
// 1. Join with a deviceId returns a session with linkExpirySeconds from config,
//    empty orders, cumulativeTotal 0; a device_sessions row exists.
Join_WithDeviceId_CreatesSession()
// 2. Join without deviceId (or empty/oversized) returns session: null and creates no row.
Join_WithoutDeviceId_ReturnsNullSession()
// 3. Same device rejoins: same session id, LastSeenAt bumped, no duplicate row.
Join_SameDevice_ReusesSession()
// 4. Add items → submit → rejoin same device: session.orders contains the
//    submitted round (status pending_dispatch or submitted), cumulativeTotal
//    equals its items sum; a new draft exists stamped with the same DeviceSessionId.
Join_AfterSubmit_ReturnsCumulativeOrders()
// 5. Two submitted rounds → cumulativeTotal is the sum of both.
Join_TwoRounds_AccumulatesTotal()
// 6. Manually age the session row past SessionLifetimeMinutes → rejoin creates a
//    FRESH session (different id), old orders not included, old row deleted,
//    old orders' device_session_id nulled.
Join_ExpiredSession_IsWipedAndRecreated()
// 7. Same deviceId at a DIFFERENT table → separate session, no cross-table orders.
Join_SameDeviceOtherTable_SeparateSession()
// 8. With an enabled FlashDealRule and a submitted qualifying order, rejoin
//    returns pendingDeal with matching dealId/orderId; after ExpiresAt (age the
//    offer row) rejoin returns pendingDeal: null.
Join_PendingDeal_RestoredUntilExpiry()
```

- [ ] **Step 2: Run** `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~DeviceSessionTests"` → all fail (missing behavior), for the right reasons (assertion failures / missing fields, not setup crashes).

- [ ] **Step 3: Implement** the behavior requirements above in `PartyController` + `CustomerController.SubmitOrder`.

- [ ] **Step 4: Run the filter again** → all 8 pass. Then run the FULL backend suite `dotnet test backend/RestaurantApi.Tests` → no regressions (party/merge/submit tests must still pass).

- [ ] **Step 5: Commit** — `feat(session): device-bound sessions with cumulative orders and deal restore on join`

---

### Task 3: Frontend data layer — deviceId, api-client contract, session store, cumulative math (TDD)

**Files:**
- Create: `frontend-customer/src/services/deviceId.ts`
- Create: `frontend-customer/src/__tests__/deviceId.spec.ts`
- Modify: `frontend-customer/src/services/api-client.ts` (`JoinTableResponse` ~lines 38-50, `joinTable` ~lines 109-116)
- Modify: `frontend-customer/src/stores/useGuestStore.ts` (`join()` ~lines 56-76 — pass deviceId)
- Modify: `frontend-customer/src/stores/useSessionStore.ts`
- Modify: `frontend-customer/src/__tests__/useSessionStore.spec.ts` (extend, mirror existing style)
- Modify: `shared/types/models.ts` (add shared shapes; edit `.ts` only, never the committed generated `.js`/`.d.ts`)

**Interfaces:**
- Consumes Task 2's wire contract exactly (field names above).
- Produces (Task 4 relies on):
  - `getDeviceId(): string` — from `localStorage['clarifi.deviceId']`, generated once via `crypto.randomUUID()` (fallback: uuid-v4 from `crypto.getRandomValues`).
  - Shared types in `models.ts`:
    ```ts
    export interface PendingFlashDealPayload { dealId: UUID; orderId: UUID; productId: UUID; productName: string; imageUrl: string | null; originalPrice: number; discountPrice: number; expiresAt: string; quantity: number }
    export interface DeviceSessionSummary { id: UUID; linkExpirySeconds: number; cumulativeTotal: number; orders: Order[]; pendingDeal: PendingFlashDealPayload | null }
    ```
    (align `PendingFlashDealPayload` field names with what Task 2 actually emits — verify against the backend code, not this plan, if they differ)
  - `JoinTableResponse.session: DeviceSessionSummary | null` (the `orders` element shape in api-client should match its existing inline `activeOrder` style: `{ id, status, totalAmount, items }`).
  - Session store new state (all sessionStorage-mirrored like existing fields, cleared in `clearSession`): `deviceSessionId: string | null`, `linkExpirySeconds: number | null`, `cumulativeTotal: number` (default 0), `sessionOrders` (typed like the response orders), `pendingDeal: PendingFlashDealPayload | null` (NOT persisted — restored fresh from each join).
  - Session store new action: `registerSubmittedRound(roundTotal: number): number` — adds `roundTotal` to `cumulativeTotal` (using `roundMoney`), persists, returns the new cumulative. (Authoritative value re-arrives with the next join.)

- [ ] **Step 1: Failing tests** — `deviceId.spec.ts`: returns a UUID-shaped string; stable across calls; persists in localStorage under `clarifi.deviceId`. `useSessionStore.spec.ts` additions (mock `apiClient`/guest store the way the existing spec does): `initializeSession` stores `session` fields incl. `cumulativeTotal` and `pendingDeal`; `session: null` response leaves defaults; `registerSubmittedRound(12.5)` twice accumulates with money rounding; `clearSession` resets the new fields.
- [ ] **Step 2:** `npx vitest run src/__tests__/deviceId.spec.ts src/__tests__/useSessionStore.spec.ts` → new tests fail.
- [ ] **Step 3: Implement.** `useGuestStore.join` calls `apiClient.joinTable(qrToken, storedGuestId, getDeviceId())`; body `{ guestId, deviceId }`.
- [ ] **Step 4:** Same vitest command → pass. Then full `npm test` in frontend-customer → no regressions. `npm run type-check` → clean.
- [ ] **Step 5: Commit** — `feat(session): device id + session contract in customer data layer`

---

### Task 4: Frontend UX — link-expiry overlay, cumulative sent screen, flash-deal restore

**Files:**
- Create: `frontend-customer/src/composables/useLinkExpiry.ts`
- Create: `frontend-customer/src/components/screens/LinkExpiredView.vue`
- Create: `frontend-customer/src/__tests__/useLinkExpiry.spec.ts`
- Modify: `frontend-customer/src/view/OrderingView.vue` (onMounted ~163-202, `handleSubmitted` ~234-248, `orderAgain` ~252-262, template overlay near the invalid-QR overlay ~377-390)
- Modify: `frontend-customer/src/composables/useWebSocket.ts` (extract deal mapping; expose disconnect for expiry)
- Modify: `frontend-customer/src/stores/useFlashDealStore.ts` (restore-from-payload helper if cleanest there)
- Modify: `frontend-customer/src/composables/useLocale.ts` (both `ro` and `en` dicts)

**Interfaces:**
- Consumes Task 3 (`sessionStore.linkExpirySeconds`, `cumulativeTotal`, `sessionOrders`, `pendingDeal`, `registerSubmittedRound`).
- Produces: `useLinkExpiry()` → `{ expired: Ref<boolean>, start(seconds: number): void, stop(): void }` — single `setTimeout`, restartable (start clears any prior timer).

**Behavior requirements:**
1. **Deal mapping reuse**: extract the `FlashDealAvailable` payload→`FlashDeal` conversion in `useWebSocket.ts` (~lines 104-144) into an exported helper (e.g. `buildFlashDealFromPayload(raw)` in `useFlashDealStore.ts` or a small module) and use it BOTH from the WS handler and for `sessionStore.pendingDeal` after join in `OrderingView.onMounted` (only when unexpired; set `activeDealOrderId` from the payload's orderId so claiming works).
2. **Link expiry**: in `OrderingView.onMounted` after a successful `initializeSession`, `start(sessionStore.linkExpirySeconds ?? 900)`. On `expired` becoming true: render `LinkExpiredView` as a full-screen top-layer overlay (above everything incl. flash-deal modal); disconnect the WebSocket and stop the party poll (find the 15 s poll — likely in `PartyPanel.vue` or a composable — and guard it with the expired flag; the point is an abandoned open tab must stop sending heartbeats so the backend wipe can happen). `orderAgain`/other actions unreachable behind the overlay. The overlay offers no button — copy tells the diner to re-scan the QR (a re-scan = fresh page load = fresh timer + restored session).
3. **Cumulative sent screen**: in `handleSubmitted`, replace `lastTotal.value = cartStore.total` with `lastTotal.value = sessionStore.registerSubmittedRound(cartStore.total)`. In `onMounted` after join, if `sessionStore.cumulativeTotal > 0` set `lastTotal.value = sessionStore.cumulativeTotal` (covers reload/re-scan onto the sent screen; join's value already includes all submitted rounds). If the restored session has in-flight orders (`sessionOrders.length > 0`) and no stored screen, restore to the `sent` screen with `orderInProgress = true` so a re-scanning diner lands on their order status rather than the language picker.
4. **Locale**: add keys (both dicts): `expired.title` (RO `Sesiunea a expirat`, EN `Session expired`), `expired.message` (RO `Scanează din nou codul QR de pe masă pentru a continua.`, EN `Scan the table QR code again to continue.`). `LinkExpiredView` uses tokens/design language consistent with the invalid-QR overlay.
5. `useLinkExpiry.spec.ts` with `vi.useFakeTimers()`: not expired before N seconds, expired after; `start` twice restarts; `stop` prevents expiry.

- [ ] **Step 1:** Write `useLinkExpiry` tests → fail. Implement → pass.
- [ ] **Step 2:** Implement overlay + wiring + deal restore + cumulative behavior.
- [ ] **Step 3:** `npm test`, `npm run type-check`, `npm run lint` in frontend-customer → all clean.
- [ ] **Step 4: Commit** — `feat(session): link-expiry overlay, cumulative total, flash-deal restore`

---

### Task 5: End-to-end verification + code review

- [ ] Full backend suite: `dotnet test backend/RestaurantApi.Tests` (Docker running).
- [ ] Frontend: `npm test`, `npm run type-check`, `npm run lint` (frontend-customer); `npm run type-check` (frontend-admin — should be untouched, verify no shared-types breakage; admin imports from `@shared` too).
- [ ] Manual smoke against dev stack (backend + customer SPA), dev config (30 s expiry): scan seeded table token → order → submit → reload → cumulative total + status restored; wait >30 s → overlay appears, WS closed; reload → app works again, same totals; second round → total accumulates.
- [ ] Code review of the whole branch diff (superpowers:requesting-code-review) focusing on: tenant isolation of every new query, qrToken-only trust (no client restaurantId/orderId trusted without ownership), deviceId input capping, soft-delete handling in cumulative query, SignalR payload-shape parity for the deal restore, no behavior change for deviceId-less clients.
- [ ] Fix findings, re-run affected suites, commit fixes.

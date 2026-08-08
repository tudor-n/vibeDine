# Waiter Mode — table service from the waiter's phone

> A mobile-first mode inside the existing admin console: a waiter claims a table, takes
> orders at the table, sees the live floor, and closes the bill with a physical payment
> method — cash, card or a split of both.

Waiter Mode is **not a fourth app**. It is a second shell inside `frontend-admin` talking to
a new controller inside `RestaurantApi`, so it inherits the platform's tenancy, auth and
realtime wiring unchanged:

| App | Frontend | Backend | Audience |
|-----|----------|---------|----------|
| Customer | `frontend-customer` (:3000) | `RestaurantApi` (:5005) | Diners scanning a table QR |
| Admin | `frontend-admin` (:3001) | `RestaurantApi` (:5005) | A restaurant's owner/admin/waiter |
| **Waiter Mode** | **`frontend-admin` (:3001), routes `/waiter/*`** | **`RestaurantApi` (:5005), `/waiter/*`** | **Waiters on a phone, at the table** |
| Logistic | `frontend-logistic` (:3004) | `LogisticApi` (:5008) | Stock, costs and fiscal receipts — see `docs/logistic-app.md` |

---

## 1. What it is for

Until now a restaurant could only take orders one way: the diner scans the QR code and
orders from their own phone. Waiter Mode adds the other half of a real restaurant — the
waiter working the floor:

- **See the floor live** — which tables are free, which are claimed, whose they are, which
  ones are calling for help, how many orders are still moving.
- **Claim a table** — one waiter owns a table for the length of a visit (a *service
  session*). Exactly one open session per table, guaranteed by the database.
- **Take orders at the table** — pick from the live menu (with real-time availability),
  set quantities, add a kitchen remark per line ("fără smântână"), submit. A waiter-placed
  order goes **straight to the kitchen**.
- **Keep the bill** — every order on that table, whether the waiter typed it or the diner
  scanned the QR, rolls into one running total.
- **Close the bill** — record `cash` / `card` / `mixed`, and the session closes. That close
  is what triggers the fiscal receipt in Logistic.
- **Hand over / rescue a table** — transfer a session to a colleague at shift change;
  a manager can force-release a table whose waiter went home without closing it.

There is **no online payment**. The money changes hands at the table; the system records
*how* it was paid and issues the receipt.

---

## 2. How it is wired (the request flow)

```
Waiter's phone (browser)
   │  1. open  http://localhost:3001  → lands on /waiter (waiters default here)
   ▼
frontend-admin  (Vue 3 SPA, :3001)
   │     WaiterShell (bottom nav, big touch targets) instead of the sidebar shell
   │  2. GET :5005/api/restaurants/{rid}/waiter/floor        (REST = truth)
   │  3. StaffHub /hubs/staff, JoinRoleGroup(rid, 'WaitStaff')  (events = triggers)
   ▼
RestaurantApi  (.NET 8, :5005)
   │  4. TenantResolutionMiddleware: membership check + waiter allow-list
   │  5. WaiterModeController → TableSessionService / existing order services
   ▼
postgres  (:5433)
   │  6. table_service_sessions, orders (+3 columns), order_items.notes
   │  7. order_events  ← the outbox row, written in the SAME transaction
   ▼
LogisticApi  (:5008, separate repo)
   8. leases order_events rows → valuation, reporting, and the fiscal receipt
```

Two rules make this reliable and are worth stating up front:

1. **Events are triggers, REST is truth.** Every StaffHub event the waiter frontend receives
   funnels into one 250 ms-debounced `GET /waiter/floor`. Nothing is patched into local
   state from an event payload, so a missed or out-of-order event costs at most one stale
   render. `useWaiterRealtimeStore` also re-seeds on reconnect and polls every 15 s.
   The single exception is `MenuItemAvailabilityChanged`, whose payload is complete and is
   folded straight in.
2. **The outbox row is part of the transaction.** `IOrderEventWriter.Enqueue` adds the
   `order_events` row to the same `AppDbContext` as the domain change, so an order can never
   be dispatched without its event, nor an event emitted for a rolled-back order.

---

## 3. Data model

Waiter Mode adds **two tables** and **four columns** to the schema `RestaurantApi` owns. All
of it ships in one EF migration: `AddWaiterModeAndOrderEvents`.

| Table / column | Purpose |
|---|---|
| `table_service_sessions` | one row per table claim: who owns the table, since when, and — once closed — how it was paid |
| `orders.table_service_session_id` | which bill this order belongs to (`NULL` for an order placed with no session open) |
| `orders.source` | `'qr'` \| `'waiter'` — where the order **originated** |
| `orders.placed_by_user_id` | the staffer who typed a waiter order (`NULL` for QR orders) |
| `order_items.notes` | the waiter's kitchen remark for that line, ≤200 chars |
| `order_events` | append-only outbox: every dispatch/cancel/complete and every session event, consumed by `LogisticApi` |

### The exclusivity guarantee

```sql
CREATE UNIQUE INDEX ix_table_service_sessions_open_table
    ON table_service_sessions (table_id) WHERE status = 'open';
```

That partial unique index — not application logic — is what makes "one open session per
table" true. `ClaimTableAsync` is a plain INSERT; when two waiters tap the same table at the
same moment, Postgres rejects one with `23505`, the service catches the `DbUpdateException`,
re-reads the winning session and returns `409` naming its owner. There is no lock, no
retry loop, and no window where both succeed.

### `table_service_sessions`

`restaurant_id` (tenant-scoped, indexed) · `table_id` → `tables` CASCADE ·
`waiter_user_id` → `AspNetUsers` SET NULL · `waiter_name` (snapshot of `FullName`, so the
bill still reads correctly after the account is gone) · `status` `'open'|'closed'` ·
`opened_at` / `closed_at` · `closed_reason` `'paid'|'force_released'` ·
`payment_method` `'cash'|'card'|'mixed'` · `cash_amount` / `card_amount` / `total_amount`
(`numeric(10,2)`) · `closed_by_user_id` / `closed_by_name` — the audit trail of *who*
closed it, which is the point of force-release.

### `order_events` (the outbox)

Deliberately **not** tenant-filtered: `LogisticApi` reads it across restaurants, so it
carries `restaurant_id` as a plain indexed column and gets no global query filter. The
ownership split is strict:

- `RestaurantApi` **only ever INSERTs**. It never touches `processed_at`, `attempts`,
  `lease_id` or `lease_expires_at`.
- `LogisticApi` owns those columns and leases rows with `FOR UPDATE SKIP LOCKED`.

| `event_type` | Emitted when | Notable payload fields |
|---|---|---|
| `order.dispatched` | an order is released to the kitchen (same transaction as the stock decrement) | `source`, `tableServiceSessionId?`, `items[]` incl. `notes` |
| `order.cancelled` | either cancel endpoint, after the status flip | same shape as `order.dispatched` |
| `order.completed` | `OrderFinalizationService` archives a `ready` order | `orderId`, `totalAmount`, `occurredAt` |
| `session.opened` | a table is claimed | `waiterUserId`, `waiterName`, `openedAt` |
| `session.transferred` | a session changes owner | `fromUserId`, `toUserId`, `toName` |
| `session.closed` | close **and** force-release | `paymentMethod`, `totalAmount`, `closedReason`, `orderIds[]` — **the fiscal trigger** |

`closedReason = 'paid'` enqueues exactly one fiscal receipt in Logistic;
`'force_released'` does not.

### Session totals

`cumulativeTotal` is computed live — never stored while the session is open — as the sum of
`quantity × unitPriceAtPurchase` over the session's orders in
`pending_dispatch`, `submitted`, `preparing`, `ready`, `completed`. Two subtleties:

- **Completed orders count even though they are soft-deleted.** `OrderFinalizationService`
  archives a `ready` order by stamping `DeletedAt`; the total query does not filter it out,
  or the bill would shrink as the kitchen finished rounds. (Same precedent as the customer
  device-session cumulative display.)
- `draft`, `cancelled` and `merged` never count.

`total_amount` on the session row is the snapshot of that number taken at close.

---

## 4. Auth model

Waiter Mode adds **no new auth realm, no new roles and no `[Authorize]` policies**. It rides
the restaurant realm exactly like the rest of the admin console:

- The JWT is the ordinary restaurant staff token (`POST /api/auth/login`).
- `WaiterModeController` carries a class-level `[Authorize]`; the real gate is
  `TenantResolutionMiddleware`, which resolves the tenant from the `{restaurantId}` route
  value, verifies **membership**, and 403s a non-member before the controller runs.
- Role separation is the **path allow-list**: a `Waiter` may only reach the suffixes below.
  Everything else 403s even though the membership check passed.

### The twelve waiter-allowed pairs

`Tenancy/TenantResolutionMiddleware.WaiterAllowedPaths` — anchored regexes,
`IgnoreCase | Compiled`, matched against the path **after** `/api/restaurants/{rid}`:

| Method | Path suffix |
|---|---|
| `GET` | `^/waiter/floor/?$` |
| `GET` | `^/waiter/unclaimed-orders/?$` |
| `GET` | `^/waiter/my-sessions/?$` |
| `GET` | `^/waiter/staff/?$` |
| `GET` | `^/waiter/sessions/{guid}/?$` |
| `POST` | `^/waiter/tables/{guid}/claim/?$` |
| `POST` | `^/waiter/tables/{guid}/orders/?$` |
| `POST` | `^/waiter/sessions/{guid}/transfer/?$` |
| `POST` | `^/waiter/sessions/{guid}/close/?$` |
| `PATCH` | `^/waiter/orders/{guid}/items/?$` |
| `POST` | `^/waiter/orders/{guid}/submit/?$` |
| `POST` | `^/waiter/orders/{guid}/cancel/?$` |

`{guid}` is the existing `[0-9a-fA-F-]{36}` form. The ten pre-existing entries (orders
board, zones/tables read, help queue, branding read) are untouched.

**`force-release` is deliberately absent.** Managers (`OrgOwner` / `RestaurantAdmin`) never
consult the allow-list, so leaving the pair out is exactly what makes that endpoint
manager-only. The controller re-checks can-manage and `Forbid()`s anyway — the two together
are the gate.

Ownership rules sit on top of the allow-list and are enforced in the service: only the
session's own waiter (or a manager) may transfer, close, or write to that session's orders.

---

## 5. Backend — `backend/RestaurantApi`

```
backend/RestaurantApi/
├── Controllers/
│   ├── WaiterModeController.cs      # the /waiter surface below
│   └── WaiterController.cs          # UNRELATED — waiter signup links. Do not touch.
├── Services/
│   ├── TableSessionService.cs       # claim / transfer / close / force-release, totals
│   └── OrderEventWriter.cs          # IOrderEventWriter.Enqueue → the outbox row
├── Models/
│   ├── TableServiceSession.cs       # ITenantScoped
│   └── OrderEvent.cs                # NO query filter — cross-tenant by design
├── DTO/
│   ├── Requests/WaiterRequests.cs
│   └── Responses/WaiterResponses.cs
└── Migrations/…_AddWaiterModeAndOrderEvents.cs
```

### Endpoint reference (all under `api/restaurants/{restaurantId:guid}/waiter`)

| Method & path | Purpose |
|---------------|---------|
| `GET  /floor` | the whole floor: zones + tables + session + help/order flags → `WaiterFloorResponse` |
| `GET  /unclaimed-orders` | live rounds on tables no session covers (incl. `pending_dispatch`), with items + table info → `WaiterUnclaimedOrderDto[]` — the "Comenzi nepreluate" feed; claiming the table adopts them |
| `GET  /my-sessions` | the caller's open sessions → `WaiterSessionDto[]` |
| `GET  /sessions/{id}` | one bill: session + its orders + total → `WaiterSessionDetailDto` |
| `GET  /staff` | transfer targets: active Waiter/RestaurantAdmin members → `WaiterStaffMemberDto[]` |
| `POST /tables/{tableId}/claim` | claim a table → `201 WaiterSessionDto` · `409 { message, session }` if already claimed |
| `POST /sessions/{id}/transfer` | `{ toUserId }` → hand the session to a colleague |
| `POST /sessions/{id}/close` | `{ paymentMethod, cashAmount?, cardAmount? }` → close the bill · `409` per §12 |
| `POST /sessions/{id}/force-release` | **manager only** — close with `closed_reason='force_released'` |
| `POST /tables/{tableId}/orders` | start a draft order on the table's open session → `201 WaiterOrderDto` |
| `PATCH /orders/{id}/items` | `{ items: [{ productId, quantity, notes? }] }` — full replace, `draft` only |
| `POST /orders/{id}/submit` | price it, number it, **dispatch to the kitchen immediately** |
| `POST /orders/{id}/cancel` | draft → hard delete; anything live → `cancelled` + stock restore |

The menu comes from the existing anonymous `GET /api/restaurants/{rid}/menu` — Waiter Mode
adds no menu endpoint.

Caps enforced on `PATCH /orders/{id}/items`: **100** items per order, **99** per line,
**200** characters of notes. They are frozen in the contract and mirrored client-side.

### Ownership: orders and help calls follow the table session

The order-level claim (`orders.claimed_by_waiter_id`, the old "Preia comanda") is no
longer an independent, manual step — it is **stamped from the table session** at every
point where ownership changes hands:

| Moment | Assignment |
|---|---|
| Waiter creates a round (`POST /waiter/tables/{id}/orders`) | the session's waiter (not necessarily the typist — a manager can type on any table) |
| QR submit on a claimed table | the session's waiter |
| Table claim adopts orphan rounds | the claiming waiter |
| `POST /waiter/sessions/{id}/transfer` | the receiving waiter (same transaction as the session hand-over) |

Consequences:

- `GET /admin/orders/active` returns **only the caller's assigned rounds for
  Waiter-role callers**; managers get the whole board. The waiter-mode "Comenzi" tab
  is therefore per-waiter, and the per-card "Preia comanda" survives only as a
  manager affordance for truly unassigned rounds (QR on an unclaimed table).
- Help calls carry `assignedWaiterUserId`/`assignedWaiterName`, derived from the
  table's open session **at read time** (so claiming after the call adopts it).
  `GET /help-requests` filters for Waiter-role callers to unassigned + their own;
  the `HelpRequested` broadcast carries the same fields and every phone re-checks
  relevance on receipt. The waiter-mode Apeluri screen splits into "Chemările mele" /
  "Chemări fără ospătar" (+ manager-only "La colegi"), and `useWaiterCallsStore`
  re-seeds on every `TableSession*` event so session changes move calls between
  phones live.

### What waiter orders do differently

- `source = 'waiter'`, `placed_by_user_id` = the caller, `GuestId = null`.
- **Submit dispatches straight through.** It computes the total, assigns the per-restaurant
  `OrderNumber` (same max+1 logic as the customer flow), sets `pending_dispatch`, saves, then
  calls `DispatchToKitchenAsync` directly — **skipping `EvaluateEligibilityAsync`**. The
  2-minute flash-deal window is a diner-facing feature; a waiter standing at the table does
  not wait for it. Stock still decrements exactly once, through the existing idempotent path.
- QR orders on a claimed table **attach themselves**: `CustomerController.SubmitOrder` stamps
  `TableServiceSessionId` after the flip to `pending_dispatch` if the table has an open
  session. The diner experience is otherwise byte-for-byte unchanged and `source` stays
  `'qr'`.

---

## 6. Realtime — StaffHub

Group names always come from `Hubs/TenantGroups.Role(rid, role)`. The three session events
are sent to **both** `admin` and `WaitStaff`, because managers watch the same floor:

| Event | Payload |
|---|---|
| `TableSessionOpened` | `{ sessionId, tableId, tableNumber, waiterUserId, waiterName, openedAt }` |
| `TableSessionTransferred` | `{ sessionId, tableId, fromUserId, toUserId, toName }` |
| `TableSessionClosed` | `{ sessionId, tableId, tableNumber, paymentMethod, totalAmount, closedReason }` |

Existing events (`OrderSubmitted`, `OrderStatusUpdated`, `OrderClaimed`, `OrderFinalized`,
`HelpRequested`, `HelpAcknowledged`) keep their exact names and shapes — waiter-placed orders
flow through them like any other order, which is why the kitchen kanban needed no changes.
Since the tabs update, the order lifecycle events (`OrderSubmitted`, `OrderStatusUpdated`,
`OrderFinalized`) are broadcast to **both** `admin` and `WaitStaff` — previously they reached
only `admin`, so waiter phones noticed order changes via the 15 s poll instead of the
250 ms event-triggered refresh.

One extension to an existing broadcast: `StockService.BroadcastAvailabilityForProductsAsync`
now also sends the **same** `MenuItemAvailabilityChanged` payload to the `admin` and
`WaitStaff` groups (it previously reached only the anonymous customer menu group). That is
what greys out a sold-out dish on the waiter's order screen mid-service. The customer-side
broadcast is unchanged.

Names are PascalCase, payloads camelCase, and every `SendAsync` argument is an anonymous
DTO — never an EF entity.

---

## 7. Frontend — `frontend-admin`, Waiter Mode

```
frontend-admin/src/
├── App.vue                          # picks WaiterShell vs the sidebar shell by route name
├── components/waiter/
│   └── WaiterShell.vue              # mobile-first shell: bottom nav (5 tabs), large touch targets
├── views/waiter/                    # floor / table / order / active-orders / unclaimed / calls screens
├── services/waiterApi.ts            # the twelve functions of §5, via authedFetcher + scoped
├── stores/
│   ├── useWaiterRealtimeStore.ts    # floor + live events + polling fallback  (defineStore 'waiterRealtime')
│   └── useWaiterCartStore.ts        # the draft cart before submit            (defineStore 'waiterCart')
└── __tests__/
    ├── useWaiterRealtimeStore.spec.ts
    ├── waiterApi.spec.ts
    ├── useWaiterCartStore.spec.ts
    └── waiterTypes.spec.ts + waiterContractFixtures.ts   # the contract guard
```

### Routes

| Path | Route name | View |
|------|------------|------|
| `/waiter` | `waiter-floor` | the live floor plan |
| `/waiter/table/:tableId` | `waiter-table` | one table: session, bill, actions |
| `/waiter/table/:tableId/order` | `waiter-order` | menu + cart for that table |
| `/waiter/orders` | `waiter-active-orders` | "Comenzi" — the active-orders feed, phone-first (status segments, claim/advance; cancel manager-only) |
| `/waiter/unclaimed` | `waiter-unclaimed` | "Nepreluate" — QR rounds on unclaimed tables, grouped per table, with "Preia masa" |
| `/waiter/calls` | `waiter-mode-calls` | "Apeluri" — the live help queue, in-shell (the admin `waiter-calls` page is separate) |

Waiters **land on `/waiter`**: the router guard's waiter fallback redirects to
`waiter-floor` instead of `orders-active`. The old orders board stays reachable — it is
still in the waiter-allowed route set.

The bottom tab bar has five slots: **Mese** (floor) · **Mesele mele** (floor filtered by
`?filtru=ale-mele`) · **Comenzi** · **Nepreluate** (badge = Σ `unclaimedOrderCount` over the
floor) · **Apeluri** (badge = open help requests).

### Gating lives in three places

This repo gates imperatively, not declaratively, and Waiter Mode follows that convention:

1. the router guard's `WAITER_ALLOWED` set (which route names a waiter may open),
2. `App.vue` (whether the mode-switch button renders),
3. per-view computeds wherever behaviour differs by role (e.g. force-release is
   manager-only).

⚠️ Shell selection uses an **exact set**, never a prefix test:

```ts
const WAITER_MODE_ROUTES = new Set([
  'waiter-floor', 'waiter-table', 'waiter-order',
  'waiter-active-orders', 'waiter-unclaimed', 'waiter-mode-calls',
])
```

`waiter-calls`, `waiter-signup` and `waiters` are pre-existing route names — a
`startsWith('waiter')` check would drag three unrelated screens into the waiter shell.
(Yes, `waiter-calls` vs `waiter-mode-calls` is confusing: the former is the admin-console
page, the latter the in-shell tab. Both stay — managers use one, waiters the other.)

### The realtime store's public interface

`useWaiterRealtimeStore` is the only thing the waiter views talk to for live state:

- **state** — `floor`, `isConnected`, `isLoading`
- **getters** — `tables`, `zones`, `myTables` (`session.waiterUserId === auth.user?.id`),
  `freeTables`, `unclaimedOrderCount` (Σ per-table unclaimed rounds — the "Nepreluate"
  badge), `tableById(id)`, `unavailableProductIds`
- **actions** — `start(restaurantId)`, `stop()`, `refreshFloor()`

Lifecycle is driven from `App.vue`'s existing `watch([isAuthenticated, activeRestaurantId])`,
exactly like `useFloorStore` and `useWaiterCallsStore`. It joins
`auth.isWaiter ? 'WaitStaff' : 'admin'`.

### UI language

Romanian only, like the rest of the console — no i18n layer. The floor vocabulary is fixed:
**"Liberă"** (free), **"Masa mea"** (mine), **"Ocupată — {waiterName}"** (someone else's),
**"Cere ajutor"** (open help request), **"Notă de plată"** (the bill).
Payment labels: **"Numerar"** / **"Card"** / **"Mixt"**.

---

## 8. Local development

Waiter Mode needs no extra services — it is the normal dev stack.

```bash
# database only, everything else on the host
docker compose up -d postgres

# API (auto-migrates + seeds in Development)
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project backend/RestaurantApi

# admin console
cd frontend-admin && npm run dev        # http://localhost:3001
```

⚠️ Both frontends default `VITE_API_URL` to `http://localhost:5005` — the **docker**
mapping. A host-run API listens on **5000**, so either run the API in docker
(`docker compose up -d backend-api`) or set `VITE_API_URL=http://localhost:5000`.

Sign in with the dev seed `supporter@demo.test` / `Demo1234!` and open
<http://localhost:3001/waiter>.

### Smoke script

1. **Claim** — `/waiter`, tap a free table. It flips to "Masa mea"; every other open session
   sees `TableSessionOpened` and the tile changes there too within ~250 ms.
2. **Order** — "Comandă nouă", add two products, put a note on one line, submit. The kitchen
   kanban (`/orders`) shows it in `submitted` **immediately**, with the note and a waiter tag.
3. **Mixed round** — open the same table's QR page (`http://localhost:3000/<qrToken>`, tokens
   in `Data/SeedData.cs`) and submit a diner order. It appears on the same bill: one session,
   both orders, one growing `cumulativeTotal`.
4. **Availability** — set a product's stock to zero from `/inventory`. The waiter order screen
   greys it out without a refresh.
5. **Close** — "Notă de plată" → "Numerar" → close. The table returns to "Liberă".
6. **Verify the outbox**:
   ```bash
   docker compose exec postgres psql -U dev -d restaurant \
     -c "select event_type, created_at from order_events order by id desc limit 10;"
   ```
   Expect `session.opened`, one `order.dispatched` per round, and `session.closed`.

### Tests

```bash
cd frontend-admin && npm run type-check && npm test
DOTNET_ROLL_FORWARD=LatestMajor dotnet test backend/RestaurantApi.Tests   # needs Docker
```

---

## 9. Config / env

**Waiter Mode introduces no new environment variables** — no feature flag, no new secret, no
new base URL. It reuses the API's existing JWT, connection string and CORS config, and the
frontend's existing `VITE_API_URL` / `VITE_WS_URL`.

The one compose change in this repo's Waiter Mode work is unrelated to Waiter Mode itself:
`AllowedOrigins__2: "http://localhost:3004"` on `backend-api`, the CORS companion step for
the Logistic frontend (see `docs/logistic-app.md` §5).

---

## 10. How to add a feature (the pattern)

**Add a field to the bill**, e.g. a guest count on the session:

1. **Migration** — add `guest_count integer NULL` to `table_service_sessions` in a new
   `RestaurantApi` migration (never hand-run SQL, never a migration in another app), map it
   snake_case in `AppDbContext.OnModelCreating`, and give `Down()` the matching drop.
2. **C# DTO** — add it to the `WaiterSessionDto` record in `DTO/Responses/WaiterResponses.cs`
   and to whichever request record sets it.
3. **TS contract** — add the same field, same name, same nullability, to
   `shared/types/waiter.ts`. These two are hand-synced; changing one alone is the classic
   silent break.
4. **Fixture** — add it to `frontend-admin/src/__tests__/waiterContractFixtures.ts`. That file
   is type-checked (specs are not), so a mismatch fails `npm run type-check` immediately, and
   `waiterTypes.spec.ts` catches the runtime shape.
5. **Service + endpoint** — set/read it in `TableSessionService`; if a waiter must reach a new
   route, add the pair to `WaiterAllowedPaths` or it 403s for waiters only (and passes all
   your manager-account testing).
6. **Frontend** — a function in `services/waiterApi.ts`, then the view. Views call `load()` in
   `onMounted`, never `fetch` directly, and reuse `.card-paper` / `.pill-btn` / `.field-input`.
7. **Outbox?** — if Logistic needs to know, add an `IOrderEventWriter.Enqueue` call in the
   **same** `SaveChangesAsync` as the domain change, and document the payload here and in
   `docs/logistic-app.md`.

**Add a realtime signal** → broadcast from the service with an anonymous DTO to
`TenantGroups.Role(rid, "admin")` **and** `TenantGroups.Role(rid, "WaitStaff")`, then add the
event name to `TRIGGER_EVENTS` in `useWaiterRealtimeStore.ts`. It will be debounced into a
floor refresh for free — do not patch state from the payload.

---

## 11. Not built yet / next steps

1. **Transfer UI polish** — the endpoint and the staff picker exist; there is no shift-change
   flow (transfer several tables at once, or hand off a whole zone).
2. **Kitchen printers** — waiter notes reach the kanban screen only. No ticket printing, no
   printer-per-station routing.
3. **Multiple registers / split bills** — one session closes as one payment (possibly split
   cash/card). Splitting a table's bill per guest is not modelled.
4. **Tips** — not recorded anywhere; `cash_amount + card_amount` must equal the total exactly.
5. **Offline / flaky-signal mode** — the cart survives a reload via `sessionStorage`, but a
   submit made with no connectivity is simply an error.
6. **Waiter performance reporting** — the data is in `order_events` and
   `table_service_sessions`; no screen reads it yet. Logistic is the intended home.

---

## 12. Gotchas

- **Claim conflicts are normal, not exceptional.** Two waiters tapping one table is an
  expected race; the `409` carries the winning `session` so the UI can say who owns it. Never
  "fix" this with a pre-check `SELECT` — that reintroduces the window the index closes.
- **You cannot close a bill while an order is `pending_dispatch`.** That status means the
  order is mid-flight to the kitchen (stock not yet decremented), so the close returns `409`.
  Lingering `draft` orders are different: they are silently detached
  (`table_service_session_id = NULL`) and never block a close.
- **`source` is not `OrderType`.** The delivery-storefront spec has locked
  `OrderType = dine_in | delivery | takeaway` for fulfilment channels. A waiter order is a
  `dine_in` order whose *origin* is `waiter`. Do not add an `OrderType` column here.
- **`WaiterController.cs` is not Waiter Mode.** It handles waiter signup links and predates
  all of this. The new controller is `WaiterModeController.cs`.
- **Never `startsWith('waiter')` on a route name** — see §7.
- **Completed orders are soft-deleted but still count** toward `cumulativeTotal`. A total that
  shrinks as the kitchen finishes rounds means someone added a `DeletedAt` filter.
- **`order_events` has no tenant query filter.** That is deliberate and commented in
  `AppDbContext`. Any new query against it must filter `restaurant_id` explicitly.
- **The frontends default to port 5005, a host-run API listens on 5000** — see §8.
- **A close is irreversible.** There is no re-open; a mistaken close means claiming the table
  again and a second bill. Force-release exists for the stuck case, and stamps
  `closed_by_user_id` / `closed_by_name` so it is auditable.

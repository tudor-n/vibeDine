# Multi-person ordering at the same table

## Context

Today every table has **exactly one draft order**: `GetTableSession` looks up the existing draft for `TableId` and returns it, creating one only if none exists (`CustomerController.cs:227-244`). All phones that scan the same QR resolve to the same `orderId`, and the cart-sync endpoint replaces the order's items wholesale (`PATCH /orders/{orderId}/items`, `CustomerController.cs:144-216`). So if two family members at the same table both order from their phones today, **their carts overwrite each other** — last write wins.

We want the opposite: each person at the table can order from their own phone, choose their own items, and at checkout decide either to:
- **Merge** all the phones' carts into a single bill (so the table pays once), or
- **Pay separately** — each phone submits its own order and gets its own bill.

Merging is only allowed for guests currently seated at the same table.

## Recommended approach: per-guest draft orders under a shared table

Introduce a lightweight **TableGuest** concept — one row per phone seated at a table — and let each guest own their own draft Order. Multiple drafts can coexist on the same table. Merging is a pre-submit operation that consolidates several drafts into one. "Pay separately" is the default: each guest submits their own draft to the kitchen independently.

### On your idea of asking "how many people are at the table?" up front

You floated prompting the first scanner to enter the party size before ordering begins. My recommendation: **don't do that.** Reasons:

- It adds friction to the very first interaction with the system — the person who scans first has to speak for the whole table before they've even seen the menu.
- It creates ghost slots when the answer is wrong. Family says "4" but only 2 phones actually scan? You now have 2 empty guest rows the system has to reason about (and the UI has to render). Family says "2" but a third person decides to scan later? Now the model has to handle "promotion" of unaccounted guests.
- It buys you almost nothing in return. Every benefit (knowing the party size, labeling people, showing avatars) can be achieved equally well by letting the party form organically — each phone that scans = one guest, and the count is just `partyMembers.length` at any moment.
- The only place the *declared* headcount would matter is if the kitchen/waiter needed to plan seating or covers — but that's a separate restaurant-ops concern, better handled by staff at seating time, not by a customer-facing prompt.

So the design below uses **organic party formation**: each phone that scans the QR is auto-assigned a guest slot ("Guest 1", "Guest 2", …, in scan order). No upfront prompt, no rename step. Names can be added later as an optional polish (out of scope for this plan).

### Merge action (per your answer)

Any guest at the table can trigger a single-tap **"Merge our bills"** action that consolidates **every** party member's draft into the initiator's draft. There's no per-target picker and no confirmation prompt on the other phones — but the other phones get a SignalR toast ("Guest 1 merged the bills — your items moved to their order") so the action is visible. Pre-merge, the existing per-draft isolation already enforces the rule that **no guest can modify another guest's items** — every phone's PATCH targets only its own `orderId`.

### Why this approach over alternatives

- Per-guest draft Orders (recommended): each phone's cart is isolated; the existing PATCH-replace cart-sync code keeps working unchanged because it now targets *your* draft, not the table's. Submission, total computation, payment status, and the kitchen kanban all remain "one Order = one bill" — no special split-bill logic needed downstream.
- Per-item GuestId on a shared Order (rejected): would require rewriting cart-sync (`useCartStore.ts:25-51` and `CustomerController.cs:144-216`) to filter by guest, plus everywhere the order total or items are read. High blast radius, more places to introduce bugs.

## Data model changes

### New entity: `TableGuest`
`backend/RestaurantApi/Models/TableGuest.cs` (new file)
- `Id: Guid` (PK)
- `RestaurantId: Guid` (tenant)
- `TableId: Guid` (FK → RestaurantTable)
- `DisplayName: string?` ("Guest 1", "Guest 2", …)
- `CreatedAt: DateTime`
- `LastSeenAt: DateTime` (for stale-cleanup later — out of scope to implement now)

EF mapping in `AppDbContext.cs` (after the Order block at line 85): table `table_guests`, indexes on `(RestaurantId)` and `(TableId)`.

### Update `Order`
`backend/RestaurantApi/Models/Order.cs`
- Add `Guid? GuestId` (nullable FK → TableGuest). Null on legacy / merged orders.
- Add `Guid? MergedIntoOrderId` (nullable self-FK). When a draft is merged into another draft, this points to the survivor; the merged draft's `Status` becomes `"merged"` and it stops appearing in active queries.

Index in `AppDbContext.cs`: `HasIndex(o => new { o.TableId, o.GuestId, o.Status })` to make the "is there a draft for this guest" lookup fast.

### Migration
A new EF migration adds the `table_guests` table, `orders.guest_id` and `orders.merged_into_order_id` columns. Existing draft orders keep `guest_id = NULL` — treated as "legacy table-wide drafts" so nothing breaks for tables already mid-session at deploy time.

## Backend endpoints

All new endpoints live in a new controller `backend/RestaurantApi/Controllers/PartyController.cs` (mirrors the pattern in `HelpController.cs`).

1. **`POST /api/tables/{qrToken}/join`** — body: `{ guestId?: Guid }` (the phone passes its stored `guestId` if it has one). Creates a new `TableGuest` + draft Order for first-time scanners; otherwise returns the existing draft for that guestId on this table. Display name auto-assigned as `"Guest {N}"` where N is the next scan-order slot at this table. Returns `{ guestId, orderId, tableInfo, partyMembers: [...] }`.

2. **`GET /api/tables/{qrToken}/party`** — returns all `TableGuests` at this table with each guest's current draft summary (`{ guestId, displayName, itemCount, total, status }`). Used by every phone to render the Party panel.

3. **`POST /api/tables/{tableId}/merge`** — body: `{ initiatorGuestId }`. Backend resolves the initiator's draft as the target, and gathers **all other party members' drafts** at this table that are still in `"draft"` status (matches "any guest can merge everyone" from your requirements — no per-target picker). Moves all `OrderItems` from source orders to the target order (updates `OrderId`), recomputes `target.TotalAmount`, sets each source `Status = "merged"` and `MergedIntoOrderId = target.Id`. Broadcasts `PartyMerged` on the table SignalR group with `{ targetOrderId, mergedOrderIds, initiatorDisplayName }` so other phones can show a toast and clear their local cart.

The rename endpoint is intentionally **omitted** for this scope — auto-assigned "Guest N" labels are sufficient. Can be added later without schema changes.

The existing **`GET /api/tables/{qrToken}/session`** behavior is preserved as a legacy path: if no `guestId` is passed, it still returns the table-wide draft (so deployed phones don't crash). New flow uses `join` instead.

The existing **`POST /restaurants/{rid}/orders/{oid}/submit`** doesn't need code changes — each guest submits their own order. After a merge, the **target** order is what gets submitted; the merged sources are already in `"merged"` status and the submit endpoint's `Status == "draft"` filter prevents double-submit (`CustomerController.cs:39`).

## Real-time sync (SignalR)

Use the existing **CustomerHub** table group (`TenantGroups.Table(restaurantId, tableId)` — `Hubs/TenantGroups.cs:7-16`). When any phone joins or merges, broadcast on the table group so all other phones at the same table refresh their Party panel.

New SignalR events sent from the backend:
- `PartyMemberJoined` — `{ guestId, displayName }`
- `PartyMemberUpdated` — `{ guestId, displayName, itemCount, total }` (sent on cart change with light debounce; alternatively, phones can refetch `/party` on demand to reduce broadcast volume — pick the simpler path during implementation)
- `PartyMerged` — `{ targetOrderId, mergedOrderIds, initiatorDisplayName }`

Frontend handler added to `frontend-customer/src/composables/useWebSocket.ts` next to the existing event handlers around line 61-101 (same pattern as `WaiterOnTheWay`).

## Frontend changes

### New store: `useGuestStore` (Pinia)
`frontend-customer/src/stores/useGuestStore.ts` (new). Backed by **`localStorage`** (not `sessionStorage`, so the guest identity survives reloads). Fields: `guestId`, `displayName`, plus the current `partyMembers` array. Methods: `joinTable(qrToken)`, `refreshParty()`, `mergeAll()`.

### Update `useSessionStore`
`frontend-customer/src/stores/useSessionStore.ts:15-40` — `initializeSession(token)` becomes a thin wrapper that:
1. Reads `guestId` from `localStorage` (via `useGuestStore`).
2. Calls `POST /api/tables/{token}/join` with that `guestId` (or null on first scan).
3. Stores returned `tableId`, `orderId` (now the **guest's** orderId), and party info.

This way the rest of the app (cart sync, submit, etc.) continues using `sessionStore.orderId` — it just now points at the guest's draft, not the table's draft.

### New UI: Party panel
A new component `frontend-customer/src/components/PartyPanel.vue` shown:
- As a chip/avatar row in the header of `PebblesView.vue`, `MenuView.vue`, and `CartView.vue` (e.g., "👥 You + 2 others").
- Expanded into a sheet in **`CartView.vue`** with each member, their items count and subtotal, plus a primary action "Merge bills" (offered when ≥2 members have items in their drafts).

### Update `CartView.vue` flow
`frontend-customer/src/components/screens/CartView.vue:50-69` — the existing single "Submit" button stays (this is the **pay-separately** path — submits only this phone's draft, kitchen sees one order, waiter brings one receipt). Above it, add a **"Merge our bills"** button (only enabled when the party has 2+ members with items). On click:
1. Calls `POST /tables/{tableId}/merge` with `initiatorGuestId = my guestId`.
2. Backend consolidates all other drafts into mine; SignalR broadcasts `PartyMerged` to the table group.
3. The local cart refetches and now shows everyone's items combined. The user can still review, then tap Submit to send the merged order to the kitchen as one bill.

Other phones at the table receive the `PartyMerged` event, show a toast ("Guest 1 merged the bills — your items moved to their order"), clear their local cart, and route to a "merged into Guest 1's order" status screen (a small new state, similar to `OrderSentView`).

### First-scan UX
No name prompt. The first time `localStorage.guestId` is empty, the join endpoint silently auto-creates the guest and stores the returned id. The user lands on `LanguageSelectView` exactly as today, and the new Party panel chip in the next screens reveals the assigned label ("You · Guest 1") once at least one other phone joins.

## Files to touch

| Area | File | Change |
| --- | --- | --- |
| Model | `backend/RestaurantApi/Models/TableGuest.cs` | NEW entity |
| Model | `backend/RestaurantApi/Models/Order.cs` | Add `GuestId`, `MergedIntoOrderId` |
| Schema | `backend/RestaurantApi/Data/AppDbContext.cs` (line 85+) | Map new entity & columns |
| Migration | `backend/RestaurantApi/Migrations/` | NEW EF migration |
| API | `backend/RestaurantApi/Controllers/PartyController.cs` | NEW: join/party/merge |
| API | `backend/RestaurantApi/Controllers/CustomerController.cs:218-275` | Optional `guestId` query param on session GET |
| Hub | `backend/RestaurantApi/Hubs/CustomerHub.cs` | No code change (reuse table group) |
| Shared | `shared/types/api-responses.ts`, `api-requests.ts` | New DTOs for party/merge |
| Store | `frontend-customer/src/stores/useGuestStore.ts` | NEW Pinia store |
| Store | `frontend-customer/src/stores/useSessionStore.ts` | Use guest-aware init |
| WS | `frontend-customer/src/composables/useWebSocket.ts:36-107` | Handle new events |
| UI | `frontend-customer/src/components/PartyPanel.vue` | NEW component |
| UI | `frontend-customer/src/components/screens/CartView.vue:50-181` | Party panel + merge button |
| UI | `frontend-customer/src/view/OrderingView.vue:135-166` | Wire guest-store init; route to "merged into..." status on `PartyMerged` |
| API client | `frontend-customer/src/services/api-client.ts` | `joinTable`, `getParty`, `mergeParty` |

## Verification

End-to-end test in dev:
1. `docker compose up` (Postgres + API + frontend per `docker-compose.yml`).
2. Open the QR URL in **two browser windows** (use incognito for the second so `localStorage` is independent — each one is a "phone").
3. In each window: complete the language step, add different items to cart, open the cart screen. Verify the Party panel on both phones shows the other member with their current item count and total, updated live via SignalR.
4. **Pay-separately path**: Phone A taps "Submit" without merging. Phone A advances to the "order sent" screen. Phone B's Party panel updates ("Member 1 has submitted") and Phone B can still submit its own order independently. Confirm two distinct rows appear on the kitchen kanban (admin app).
5. **Merge path**: With both phones holding items in their drafts, Phone A taps "Merge our bills". Phone B's draft shows "merged into Guest 1's order", Phone A's cart now shows all items combined, and submit produces a single kitchen order with combined totals.
6. **Refresh resilience**: reload Phone A's window — `localStorage.guestId` is preserved, the existing draft (with same items) is restored, the Party panel re-renders correctly.
7. Backend unit/integration tests in `backend/RestaurantApi.Tests/` covering: join creates draft, party returns all guests, merge moves items and marks sources merged, merge rejects orders from different tables, merge rejects non-draft orders.

## Out of scope (note for future work)

- Real payment-provider integration (Stripe etc.) — `PaymentStatus` remains the existing string field. "Pay separately" today means "separate Orders, each marked paid by the waiter at the till"; nothing about the cash/card flow changes.
- Custom guest names — auto "Guest N" only. Renaming endpoint and UI can be added later without schema changes.
- Item-level bill splitting within a single merged order (e.g., "I'll pay for these two items, you pay for those three"). Current scope is whole-cart merge or whole-cart separate.
- Targeted/partial merge (merging with only some of the party). Current scope is "merge everyone" per your requirement.
- Auto-cleanup of stale `TableGuest` rows after the table is vacated. `LastSeenAt` is captured but not yet swept.
- Admin/waiter UI showing the full party at a table (only the per-Order view exists today on the kanban).

## Implemented — deviations from the plan above

Shipped in this branch (migration `20260529100900_AddMultiPersonOrdering`). Key changes made to the plan after review:

- **Merge route is keyed on `qrToken`, not `tableId`.** A bare `/tables/{tableId}/merge` carries **no tenant context** (`TenantResolutionMiddleware` only resolves from `restaurantId` or `qrToken`), so the query filter would fail closed and find nothing. All three endpoints use the tenant-resolving `qrToken` form: `POST /api/tables/{qrToken}/join`, `GET /api/tables/{qrToken}/party`, `POST /api/tables/{qrToken}/merge`.
- **Merge is transactional and re-parents items at the DB level.** `ExecuteUpdateAsync` moves `OrderItems` to the target order (no EF navigation-fixup ambiguity), sources become `Status="merged"` + `MergedIntoOrderId`, and the target total is **recomputed from the rows** (`SumAsync`). Submitted orders are left alone (those guests already chose to pay separately). Initiator/table ownership is validated: the initiator guest must belong to this table, else 400.
- **Party totals are summed from items, not `Order.TotalAmount`.** Pre-existing bug: `CustomerController.AddItemsOrder` writes a **doubled** `TotalAmount` on drafts (it both `order.Items.Add` and `_context.OrderItems.Add`, and EF fixup also appends to the loaded collection). The DB rows are correct; submit/merge recompute correctly. The party panel sums live from items so it shows the right number. *(Recommend fixing the root cause in `AddItemsOrder` separately.)*
- **Guest identity is stored in `localStorage` keyed by `qrToken`** (`party.guest.{qrToken}`) so a phone that later scans a different table starts a fresh slot. `join` only restores a `guestId` that actually belongs to this table (forged/foreign ids start a new guest).
- **No per-keystroke `PartyMemberUpdated` broadcast.** Per the scope critique, phones refetch `/party` on `PartyMemberJoined` / `PartyMerged` and when the cart opens — avoids the O(N²) broadcast storm without touching the cart-sync hot path.
- **Merged-phone safety:** `useCartStore` gains `frozen` + `abandonForMerge()` (cancels the pending debounced sync, clears the cart) so a merged phone can't 404 against its now-merged draft; `useGuestStore` is a **leaf store** (imports no other store) to avoid the circular-dependency trap. The merged phone routes to a new `MergedView`; the initiator's cart is hydrated from the merge response via `replaceFromServer`.
- **`DesignTimeDbContextFactory`** added so `dotnet ef` can scaffold migrations without booting the web host (which would run dev-time `Migrate()`/seeding and need a live DB).

## Follow-up fixes (post-review)

Two bugs made the feature look broken when tested with two browser tabs:

- **Guest identity moved from `localStorage` → `sessionStorage`** (`useGuestStore`). `localStorage` is shared across all tabs/windows of one browser, so a second tab read the *same* `guestId`, both phones resolved to the *same* `TableGuest` + draft, and there was only ever one party member — which is why "one person controlled everything," the party panel (shown only with ≥2 members) never appeared, and the merge button never appeared. `sessionStorage` is per-tab yet still survives reloads, so each phone/tab is its own guest. (The plan's stated reason for `localStorage` — "survives reloads" — is also satisfied by `sessionStorage`, which `useSessionStore` already uses for `orderId`.) On real devices either storage works since each phone is a separate device; this fix is what makes same-browser testing — and any two-tab usage — behave correctly.
- **Live party sync added (`PartyMemberUpdated`)** — the previously-deferred event. `CustomerController.AddItemsOrder` now broadcasts `PartyMemberUpdated` to the table's `CustomerHub` group after a guest draft's items change; every phone refetches `/party`. The merge button now appears the moment a peer adds items, instead of only after the user leaves and reopens the cart. The cart PATCH is already debounced (~500 ms) on the client, so the broadcast is naturally rate-limited.

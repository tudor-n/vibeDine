# Delivery via Embedded Storefront — End-to-End Plan

**Status:** Draft / proposal · **Date:** 2026-07-09 · **Owner:** TBD
**Goal:** Let a restaurant take **delivery orders from its own public website** by embedding a VibeDine ordering page, reusing the existing order pipeline (cart → submit → kitchen → SignalR status) adapted for a table-less, address-based flow.

> This plan is grounded in the current code (`development` branch). It marks clearly what we **reuse as-is**, what we **extend**, and what is **net-new**. It deliberately proposes the smallest change that keeps the multi-tenancy invariant intact and ships a usable MVP fast, with online payment as a gated follow-up.

---

## 1. TL;DR — the recommendation

The user's instinct is correct and the codebase supports it well: **the customer ordering machine is already anonymous and URL-scoped, with no cookies or auth token, so it is well-suited to embedding.** We do **not** rebuild ordering; we add a *delivery channel* on top of it.

Recommended shape:

1. **One new order channel.** Add `OrderType` (`dine_in` | `delivery` | `takeaway`, default `dine_in`) to `Order`, make `TableId` **nullable**, and attach a 1:1 `DeliveryDetails` row (customer name/phone/address/notes/fee/requested-time). No synthetic "delivery table" hack.
2. **Reuse the anonymous restaurant-scoped entry point.** The menu endpoint is *already* anonymous and scoped by `restaurantId` (that is how `MenuController` works today). Delivery orders resolve tenancy the same way — no per-table QR needed. Public URL keyed on the existing `Restaurant.Slug`, gated by a new `Restaurant.DeliveryEnabled` toggle.
3. **A dedicated storefront route in `frontend-customer`** (`/order/:slug`) reusing the existing menu + cart + submit + SignalR-status pipeline, dropping the dine-in-only layers (party/merge, call-waiter, table QR).
4. **Embed via iframe or link-out** from the restaurant's site. Delivery embedding is *more robust* than dine-in embedding because it does **not** rely on persistent cross-device identity — a single checkout in the iframe's partitioned storage is fine.
5. **Payment is phased.** MVP ships **Cash/Card on delivery (COD)** reusing today's no-payment flow. **Online payment is a follow-up** that lands with the payments workstream — and is **security-gated** on customer session-token binding (see §6), which matters far more for internet-facing delivery than for dine-in.

**Effort estimate (rough):** MVP (COD, iframe/link, single board) ≈ 2–3 weeks of full-stack work across 5 vertical slices (§10). Online payment + delivery-zone logic + polished admin config is a second phase of similar size.

---

## 2. User stories

**Diner (on restaurant's website):**
- I open the restaurant's site, click "Order delivery", and see the menu embedded inline (or in a new tab).
- I add items to a cart, enter my address + phone, choose "deliver now / at a time", and place the order.
- I see live status (received → preparing → out for delivery → delivered) without refreshing.

**Restaurant staff (admin console):**
- A new delivery order pops in with a sound, clearly badged **DELIVERY** and showing the customer's address/phone.
- I accept or reject it, then advance it through the kitchen board and mark it out-for-delivery / delivered.
- I configure delivery (on/off, hours, minimum order, delivery fee, delivery radius/zones) and copy an **embed snippet** for our website.

---

## 3. What we already have (reuse directly)

| Capability | Where | Reuse for delivery |
|---|---|---|
| **Anonymous, cookieless customer flow** scoped purely by URL identifiers | `frontend-customer` uses plain `fetch`, no `credentials`, no bearer; identity is `qrToken`/`restaurantId` in URL + `guestId` in `localStorage` | Iframe-friendly: no third-party-cookie blocker applies |
| **Anonymous restaurant-scoped menu** | `MenuController` `GET /api/restaurants/{restaurantId:guid}/menu` (no `[Authorize]`, tenant resolved from route) | Storefront reads the menu with **zero backend change** |
| **Tenant middleware already scopes anonymous `restaurantId` routes** | `Tenancy/TenantResolutionMiddleware.cs` — membership check only runs *if authenticated* | Delivery endpoints under `api/restaurants/{restaurantId}/delivery/...` get auto-scoping for free |
| **Cart → sync → submit pipeline** (table-agnostic once `orderId`+`restaurantId` exist) | `useCartStore.ts` → `PATCH .../orders/{orderId}/items` → `POST .../orders/{orderId}/submit`; backend `CustomerController` | Directly reusable; only order *creation* differs |
| **Order lifecycle + kitchen board** | `Order.Status` string flow `pending_dispatch→submitted→preparing→ready→completed`; `OrderNumber`; `OrderFinalizationService`; `AdminController` board/claim; `ActiveOrdersBoardView.vue` | Delivery orders ride the same board (with channel-aware extra states) |
| **Realtime, both sides** | `CustomerHub` (anonymous, table groups) + `StaffHub` (role groups); `TenantGroups.cs`; admin `useFloorStore.ts` already consumes `OrderSubmitted/OrderStatusUpdated/OrderFinalized/OrderClaimed` | Extend group scheme + payloads; admin feed needs minimal change |
| **Tenant-scoped entity pattern** | `ITenantScoped` + global query filter + auto-stamp in `AppDbContext` | New `DeliveryDetails` entity is a one-line filter + migration |
| **Money handling** | `decimal(10,2)`, `utils/money.ts` `roundMoney` | Delivery fee + subtotal totals reuse it |
| **RO/EN localization** | `useLocale.ts` inline dictionary | Add delivery strings in both languages |
| **`Restaurant.Slug` (unique)** | `Models/Restaurant.cs`, set at creation | Natural public storefront key: `/order/{slug}` |
| **No frame-busting headers today** | No `X-Frame-Options`/CSP anywhere in backend or `frontend-customer` | Framing is technically possible now; we *add* a `frame-ancestors` allow-list for security |

## 4. Gaps (must build)

| Gap | Reality today | Needed for delivery |
|---|---|---|
| **Order channel/type** | No `type`/`channel` column anywhere; every order is dine-in-via-QR | Add `OrderType` discriminator |
| **Table coupling** | `Order.TableId` is **non-nullable** FK; `CreateOrder` requires a real table | Make `TableId` nullable (or channel-gated) |
| **Customer contact + address** | No `Customer` entity; no name/phone/email/address fields on `Order` | New `DeliveryDetails` (name, phone, email?, address, geo?, notes, requested time, fee, ETA) |
| **Restaurant-level storefront config** | Only per-table `qrToken`; no restaurant settings page; no delivery toggle/hours/fee/zone | New `Restaurant` columns + admin settings surface + endpoint |
| **Order accept/reject + cancel** | **No cancel/reject endpoint exists at all**; `cancelled` status is referenced but never set | Add accept/reject + delivery-specific states |
| **Delivery fulfillment states** | `ready` auto-archives after 45s (`OrderFinalizationService`) | Add `out_for_delivery`/`delivered`; make finalization channel-aware |
| **Payment** | None. `Order.PaymentStatus` is an inert string, never mutated | COD MVP now; online payment later (gated) |
| **Ownership binding on anonymous writes** | Customer endpoints trust `orderId`+`restaurantId` with no per-session ownership check | Session-token binding before internet-facing writes (see §6) |
| **CORS for third-party domains** | Explicit allow-list, `AllowCredentials()`, only localhost:3000/3001 | Per-restaurant embedding origin registration |
| **Cross-site iframe storage** | Guest identity relies on `localStorage`/`sessionStorage`; cross-site iframes partition/block these | Delivery doesn't need cross-context persistence — design around it (§5C) |

> **Note:** `CLAUDE.md`/`backend/CLAUDE.md` describe a **stock/inventory engine and stock-decrement-at-dispatch** — verified **not present** in the code (no `StockService`, no `StockDecremented`, no cancel-restores-stock). Delivery does **not** depend on it; if stock is wanted later it's greenfield for both channels.

---

## 5. Architecture decisions

### A. Order model — `OrderType` + nullable `TableId` + `DeliveryDetails` (Recommended)

**Decision:** Add `OrderType` to `Order`, make `TableId` nullable, and store delivery-specific data in a separate 1:1 `DeliveryDetails : ITenantScoped` entity.

**Why not the alternatives:**
- *Synthetic per-restaurant "delivery table"* (piggyback on `qrToken`): zero migration, but pollutes the floor plan / tables UI with fake rows, forces every delivery order to share one "table", breaks party/presence assumptions, and leaks a delivery concept into dine-in code. Rejected.
- *Stuff delivery columns onto `Order`*: simpler joins but bloats every dine-in order row with 6+ null columns and mixes concerns. A 1:1 side table keeps `Order` lean and dine-in untouched. (An EF **owned entity** is an acceptable equivalent — same table, `HasOne().WithOne()`.)

`OrderType` also cleanly enables **takeaway** later (same channel machinery, no delivery address).

### B. Tenant entry — anonymous `restaurantId` route + `Restaurant.DeliveryEnabled` (Recommended)

**Decision:** Delivery uses the **same anonymous-scoping mechanism the menu already uses**: routes under `api/restaurants/{restaurantId:guid}/delivery/...`, tenancy resolved by the existing middleware. Public storefront URL keyed on `Restaurant.Slug` → the SPA resolves slug → `restaurantId`. Gate the whole surface behind a new `Restaurant.DeliveryEnabled` bool.

**On tokens:** the `restaurantId` is already effectively public (it's in every menu URL), so a rotatable storefront token adds *opacity*, not real security. Recommend **not** adding a token for MVP. If revocability is later wanted, add an optional `Restaurant.StorefrontToken` (Guid) mirroring `RestaurantTable.QrToken`, plus one new branch in `TenantResolutionMiddleware` — deferred.

Add a public read `GET /api/storefront/{slug}` → `{ restaurantId, name, address, deliveryEnabled, deliveryFee, minOrder, hours, deliveryZone }` so the SPA can bootstrap from a slug and render "closed / not accepting delivery" states.

### C. Embedding model — hosted storefront route, iframe **or** link-out (Recommended)

**Decision:** Ship a dedicated storefront route `/order/:slug` in `frontend-customer`, served from the customer host. Restaurants embed it two ways (we generate both snippets in admin):
1. **Inline iframe:** `<iframe src="https://order.vibedine.app/order/{slug}" …>` — inline experience.
2. **Link-out / button:** open the same URL in a new tab (or a per-restaurant subdomain `order.<their-domain>` via CNAME later).

**Key feasibility insight — delivery embeds better than dine-in.** The dine-in guest identity relies on persistent `localStorage['party.guest.<qrToken>']` surviving QR re-scans; **cross-site iframes partition/block that storage**, which would break dine-in resume. Delivery has **no cross-device / cross-context resume requirement** — the checkout is a single, self-contained session. The delivery draft order lives entirely in the iframe's own (partitioned) `sessionStorage` for the duration of checkout; if the iframe is reloaded mid-checkout the cart may reset, which is acceptable for MVP. So the storage-partitioning risk that would block dine-in embedding **does not block delivery embedding**.

Header work required:
- **CORS:** register each restaurant's embedding origin in `AllowedOrigins` (or move to a DB-backed dynamic origin policy — see §7.6). Even though the client sends no credentials, the current policy uses `AllowCredentials()` + explicit origins and will reject unknown origins on any preflighted request.
- **CSP `frame-ancestors`:** add a per-restaurant `Content-Security-Policy: frame-ancestors` allow-list on the storefront responses so only registered restaurant domains may frame it (security, not currently enforced).

### D. Payment — COD MVP → online payment later (gated)

**Decision:** MVP records **Cash/Card on delivery**: order is placed with `PaymentStatus = "unpaid"` and a `PaymentMethod = "cod"`, no money moves — this reuses today's flow verbatim. **Online prepayment is a separate phase** built with the payments workstream (Stripe/again provider-pluggable per house style), flipping `PaymentStatus` via webhook.

**Why phased:** payment is entirely greenfield (no provider, no webhook, no money-movement code today), and it is **gated on customer session-token binding (B2/B3)** — see §6. Taking prepayment from an anonymous, internet-facing embedded page **before** that binding lands would expose order tampering/enumeration. COD lets the whole delivery UX ship and be validated first.

### E. Security — the delivery-specific threat model (must address before online payment / prepay)

Dine-in tolerates weak per-order ownership because writes require physical proximity to a table QR. **Delivery is open to the internet**, so:
- **Session-token ownership binding (B2/B3):** bind each delivery draft to a per-session opaque token returned at order creation and required (header or body) on every subsequent `PATCH items` / `submit` / status read. Without it, `orderId` is a guessable GUID an attacker could enumerate/mutate. This is the **top security prerequisite** and matters more here than anywhere in dine-in.
- **Input caps & validation:** cap cart size, per-line quantity, address/notes length; reject items not on the tenant's menu; server-side re-price (never trust client prices) — the existing PATCH re-prices already; keep that.
- **Rate limiting** on order creation + submit per IP/origin (abuse/spam via a public form).
- **Tenant isolation (the #1 invariant):** every new delivery query/endpoint filters by tenant; the delivery SignalR group must verify tenant/order ownership on join. Never accept a client-supplied `restaurantId`/`orderId` that isn't ownership-checked.
- **PII:** delivery captures name/phone/address → treat as personal data (retention, access limited to the tenant, `timestamptz` UTC).

---

## 6. End-to-end flow (MVP, COD)

```
DINER (restaurant's website)                BACKEND                         ADMIN CONSOLE
──────────────────────────                  ───────                         ─────────────
iframe/tab loads /order/{slug}
  └─ GET /api/storefront/{slug} ──────────▶ resolve slug→restaurantId
                                            return {deliveryEnabled, fee, hours, zone}
  └─ GET /api/restaurants/{rid}/menu ─────▶ (existing, anonymous)
browse menu, build cart (client only)
"Checkout" → enter name/phone/address,
  choose time, method=COD
POST /api/restaurants/{rid}/delivery/orders ▶ create Order{type:delivery,
   {items, contact, address, time, method}    tableId:null, status:draft} +
                                               DeliveryDetails; return
                                               {orderId, sessionToken}
(optional edits) PATCH .../orders/{id}/items ▶ re-price, recompute total
POST .../orders/{id}/submit ────────────────▶ validate zone/hours/min-order;
                                               status→submitted (delivery skips
                                               the dine-in flash-deal window);
                                               assign OrderNumber; broadcast
                                               OrderSubmitted{channel:delivery} ──▶ useFloorStore: toast+chime,
                                                                                      board shows DELIVERY card
                                                                                      w/ address+phone
show live status via SignalR  ◀─────────────  OrderStatusUpdated{...} ◀───────────  staff Accept → preparing
  (received→preparing→out for                                                        → ready → Out for delivery
   delivery→delivered)                                                               → Delivered
```

Dropped dine-in layers for delivery: party/merge, call-waiter, per-table presence, the 2-minute flash-deal upsell window (delivery dispatches straight through, or through an explicit Accept step).

---

## 7. Backend changes (`backend/RestaurantApi`)

### 7.1 Data model
- **`Order.cs`**: add `string OrderType` (default `"dine_in"`); change `TableId` to `Guid?` + relax the FK; keep the `(TableId, GuestId, Status)` index but ensure it tolerates nulls; add index on `(RestaurantId, OrderType, Status)` for the delivery board query.
- **New `Models/DeliveryDetails.cs : ITenantScoped`** (1:1 with `Order`): `Id`, `RestaurantId`, `OrderId`, `CustomerName`, `CustomerPhone`, `CustomerEmail?`, `AddressLine`, `AddressNotes?`, `Lat?`, `Lng?`, `RequestedFor` (`timestamptz?`, null = ASAP), `DeliveryFee decimal(10,2)`, `PaymentMethod` (`"cod"`|`"online"`), `QuotedEtaMinutes?`, `CreatedAt`.
- **`Restaurant.cs`**: add `DeliveryEnabled bool`, `DeliveryFee decimal(10,2)`, `MinOrderAmount decimal(10,2)`, `DeliveryHours` (json/text), `DeliveryZone` (radius or polygon json), optional `StorefrontToken Guid?` (deferred).
- **`AppDbContext.cs`**: register `DeliveryDetails` with global tenant filter + snake_case mapping + `restaurant_id` index + `HasOne(Order).WithOne()`; add the new indexes.
- **Migration:** `dotnet ef migrations add AddDeliveryChannel` (prod never auto-migrates — Development auto-migrates/seeds).

### 7.2 Order status vocabulary (free-form strings, keep the pattern)
Add delivery-only states alongside the existing set: `accepted` (optional), `rejected`, `out_for_delivery`, `delivered`. Dine-in states unchanged. Document the delivery flow: `draft → submitted → (accepted) → preparing → ready → out_for_delivery → delivered`, with `rejected`/`cancelled` as terminal side-states.

### 7.3 Controllers / endpoints (all anonymous, restaurant-scoped, tenant-checked)
- **New `StorefrontController`**: `GET /api/storefront/{slug}` → public storefront config (resolve slug→restaurant; 404/`deliveryEnabled:false` handled).
- **New `DeliveryController`** (or extend `CustomerController`):
  - `POST /api/restaurants/{rid}/delivery/orders` — create delivery draft (items + `DeliveryDetails`); returns `{ orderId, sessionToken }`. Reuses happy-hour pricing path.
  - Reuse existing `PATCH /api/restaurants/{rid}/orders/{orderId}/items` and `POST .../submit` (add: session-token check; on submit validate hours/zone/min-order; set `OrderType`-aware dispatch — delivery bypasses the flash-deal window and goes to `submitted` directly).
  - `GET /api/restaurants/{rid}/delivery/orders/{orderId}` — status poll fallback (session-token gated).
- **Admin (`AdminController` / new)**: `POST .../admin/orders/{id}/accept`, `.../reject`, `.../out-for-delivery`, `.../delivered`; extend `getActiveOrders` DTO with `orderType` + delivery contact/address; add `PATCH /api/restaurants/{rid}/admin/delivery-settings` for restaurant delivery config.

### 7.4 Services
- **`OrderFinalizationService`**: make channel-aware — **do not** auto-archive delivery orders at `ready`; archive only after `delivered` (or a longer grace).
- **Dispatch**: reuse the `DispatchToKitchenAsync` status-guard idempotency pattern; delivery skips the 2-min flash-deal eligibility window.
- **New**: hours/zone/min-order validation helper used at submit.

### 7.5 SignalR
- **`TenantGroups.cs`**: add a customer-side delivery group, e.g. `Order(rid, orderId) => "r:{rid}:o:{orderId}"` (per-order, since there is no table). Staff reuse `Role(rid,"admin")`.
- **`CustomerHub.cs`**: add `JoinOrderGroup(restaurantId, orderId)` (+ leave). Verify the order belongs to the tenant/session before joining.
- Broadcast `OrderStatusUpdated` (and new `out_for_delivery`/`delivered`) to the per-order group; broadcast `OrderSubmitted`/status to `Role(rid,"admin")` with an added `orderType` field. **Keep event names PascalCase, payloads camelCase** — the customer SPA hand-parses SignalR frames.

### 7.6 CORS / CSP / embedding infra (`Program.cs`)
- Move `AllowedOrigins` from static config to a **DB-backed dynamic CORS policy** that also admits each restaurant's registered embedding origin(s); or, MVP: keep the static list and add restaurant domains via env/config.
- Add a security-headers step emitting `Content-Security-Policy: frame-ancestors <registered restaurant domains>` on storefront/customer responses (no such middleware exists today).
- WebSockets aren't CORS-preflighted and the customer flow is cookieless, so the SignalR handshake works cross-origin unchanged.

## 8. Shared contracts (`shared/types`)
Edit the `.ts` sources (not the generated `.js`/`.d.ts`), keep them hand-synced with the C# DTOs:
- `models.ts`: add `OrderType`; extend `OrderStatus` union with the delivery states (note the union is already a lossy subset of the free-form backend strings — keep it honest); add a `DeliveryDetails`/`StorefrontConfig` type; add `orderType`/delivery fields to `Order`.
- `api-requests.ts`: `CreateDeliveryOrderRequest`, `DeliverySettingsRequest`.
- `api-responses.ts`: `StorefrontConfigResponse`, extend `ActiveOrder`/`OrderResponse` with `orderType` + contact/address.
- If any validation limits are added (phone/address lengths), mirror them in `menu-validation.ts` ↔ C# validators.

## 9. Frontend-customer changes (`frontend-customer`)
- **Router**: add `/order/:slug` (delivery storefront) alongside the existing `/:qrToken?`. Keep them separate entry modes.
- **New `useDeliveryStore` / storefront bootstrap**: resolve slug → `GET /api/storefront/{slug}` → set `restaurantId`, delivery config, render closed/disabled states. Set `api-client` `restaurantId` (the `rid()` helper) from this instead of from a table join.
- **Reuse**: `useMenu.ts` (already `restaurantId`-scoped, unchanged), `useCartStore.ts` (cart→PATCH sync, unchanged), submit path, `useOrderStatusStore.ts`, `useLocale.ts`.
- **New checkout screen**: contact (name/phone), address (+ optional map/geocode later), requested time (ASAP/scheduled), payment method (COD for MVP), min-order/fee display, place-order.
- **Order creation**: call `POST .../delivery/orders`, store the returned `sessionToken` (in-memory / `sessionStorage`), attach it to subsequent PATCH/submit/status calls.
- **Realtime**: in `useWebSocket.ts`, when in delivery mode, join `JoinOrderGroup(restaurantId, orderId)` instead of `JoinTableGroup`; map `out_for_delivery`/`delivered` into the status timeline. Handshake/framing unchanged.
- **Drop for delivery**: party/merge UI, call-waiter, table-number chrome. Guard these behind the entry mode.
- **Storage-partitioning**: rely only on the iframe's own `sessionStorage` for the checkout session; do not assume cross-context persistence.

## 10. Frontend-admin changes (`frontend-admin`)
- **Board (`ActiveOrdersBoardView.vue`)**: add a **DELIVERY** channel badge; show customer name/phone/address on delivery cards; add delivery terminal steps (Accept/Reject, Out-for-delivery, Delivered). Decide: keep the board's 5s polling and just add fields (fast), or switch the board to consume the SignalR-backed `useFloorStore.activeOrders` (better; delivery orders then arrive live). Recommend the latter as a small refactor.
- **Realtime (`useFloorStore.ts`)**: already handles `OrderSubmitted/OrderStatusUpdated/OrderFinalized/OrderClaimed` — extend the announce toast to say "Comandă nouă de livrare" when `orderType==='delivery'`; no new plumbing.
- **New restaurant settings surface**: there is **no restaurant settings page today** — add a "Livrare" (Delivery) tab under `/settings` with: enable toggle, delivery fee, min order, hours, zone/radius, and an **embed-snippet generator** (iframe + link, copy button) using the `slug`. Backed by `PATCH .../admin/delivery-settings`.
- **Copy**: all admin strings Romanian.

## 11. Phased delivery plan (vertical slices)

Land as numbered single-concern full-stack "chunks" (house convention).

**Phase 1 — Delivery MVP (COD, link-out first, then iframe)**
1. **Chunk 1 — Data model & channel:** `OrderType`, nullable `TableId`, `DeliveryDetails`, `Restaurant` delivery columns, migration, `AppDbContext`, shared types. *Accept:* migration applies; dine-in unaffected (existing tests green); a delivery order can be created via API.
2. **Chunk 2 — Storefront + create/submit (backend):** `StorefrontController`, `DeliveryController` create/submit with session-token binding, hours/zone/min-order validation, channel-aware dispatch + finalization. *Accept:* full order lifecycle works over the API; tenant isolation verified.
3. **Chunk 3 — Storefront SPA:** `/order/:slug`, delivery bootstrap, checkout, reuse cart/menu, SignalR per-order group, RO/EN. *Accept:* place a delivery order end-to-end in a browser (link-out).
4. **Chunk 4 — Admin delivery board + settings:** channel badge, contact/address, accept/reject/out-for-delivery/delivered, delivery settings tab + endpoint + embed-snippet generator. *Accept:* staff see, accept, and fulfill a delivery order; can toggle delivery + copy embed snippet.
5. **Chunk 5 — Embedding hardening:** CORS origin registration, `frame-ancestors` CSP, iframe snippet verified on a test third-party page, rate limiting on create/submit. *Accept:* order placed from an iframe on a different origin; unknown origins blocked.

**Phase 2 — Online payment & polish (gated on session-token binding B2/B3 shipping)**
6. Online prepayment (provider-pluggable), `PaymentStatus` via webhook, refund on reject.
7. Address geocoding + map, delivery-zone polygon, scheduled-time slots, ETA quoting.
8. Optional: per-restaurant subdomain (`order.<domain>` via CNAME), takeaway channel reusing the same machinery.

## 12. Risks & open questions

- **[Security, high]** Anonymous internet-facing writes need **session-token ownership binding** before online payment / real money. Confirm B2/B3 is on the near roadmap; MVP (COD) can ship first but should still add the token.
- **[Product]** Is delivery **self-delivery** (restaurant's own driver) or third-party (Glovo/Bolt handoff)? MVP assumes self-delivery + manual status. Third-party integration is out of scope.
- **[Product]** Prepayment required for launch, or is COD acceptable to start? Drives Phase-1-vs-2 sequencing.
- **[UX]** Iframe vs link-out vs subdomain as the *primary* recommendation to restaurants — link-out avoids all storage-partitioning edge cases; iframe is more seamless. Suggest offering both, defaulting docs to link-out.
- **[Infra]** Dynamic per-restaurant CORS origins vs a static list — dynamic is cleaner at scale but adds a DB lookup on the CORS path.
- **[Ops]** Delivery hours/zones validation location (client UX + server enforcement) and timezone correctness (`Restaurant.Timezone`, UTC storage).
- **[Menu]** Should delivery have a separate menu/pricing (delivery markup) or share the dine-in menu? MVP shares it; a delivery price list is future work.

## 13. Testing
- **Backend** (`RestaurantApi.Tests`, Testcontainers Postgres): delivery order create/submit/lifecycle; **tenant isolation** (can't read/mutate another tenant's delivery order); session-token enforcement; hours/zone/min-order rejection; channel-aware finalization (delivery not archived at `ready`). *(Env caveat: these tests need Docker; roll-forward/Testcontainers issues are known — verify logic via unit tests + build where the container isn't available.)*
- **Frontend-admin** (`vitest` + MSW): board renders delivery channel + contact; accept/reject/out-for-delivery transitions; settings tab PATCH.
- **Frontend-customer** (no test harness today): manual smoke runbook for the storefront + iframe, mirroring `docs/menu-import-pdf-smoke.md` style.
- **CI caveat:** the backend CI job discovers zero tests and the customer `npm test` is a stub — never take green CI as proof; run tests locally.

## 14. Out of scope (for now)
Third-party courier integrations; driver app/tracking map; delivery-specific menu pricing; stock/inventory (doesn't exist for any channel); multi-address/saved-profile customer accounts; loyalty. All are natural follow-ups once the channel exists.

---

### Appendix — key files touched

**Backend:** `Models/Order.cs`, new `Models/DeliveryDetails.cs`, `Models/Restaurant.cs`, `Data/AppDbContext.cs`, new migration, `Tenancy/TenantResolutionMiddleware.cs` (only if adding a token — deferred), new `Controllers/StorefrontController.cs` + `DeliveryController.cs`, `Controllers/AdminController.cs`, `Services/OrderFinalizationService.cs`, `Services/FlashDealService.cs` (dispatch), `Hubs/CustomerHub.cs`, `Hubs/TenantGroups.cs`, `Program.cs` (CORS/CSP), `DTO/Requests` + `DTO/Responses`.

**Shared:** `shared/types/models.ts`, `api-requests.ts`, `api-responses.ts`, (`menu-validation.ts` if new limits).

**Customer:** `src/router/index.ts`, new `stores/useDeliveryStore.ts`, `services/api-client.ts`, `composables/useMenu.ts` (reuse), `stores/useCartStore.ts` (reuse), new checkout screen(s), `composables/useWebSocket.ts`, `composables/useLocale.ts`.

**Admin:** `src/views/ActiveOrdersBoardView.vue`, `stores/useFloorStore.ts`, new `views/Settings/DeliveryTab.vue`, `services/api-client.ts`, `router/index.ts`, `App.vue` nav.

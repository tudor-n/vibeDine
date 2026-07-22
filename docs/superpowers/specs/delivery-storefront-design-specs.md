# Licensed Delivery Storefront + Branded Landing + Two-Channel Admin — Design Spec

**Status:** Draft for review · **Date:** 2026-07-17 · **Owner:** Tudor
**Goal:** License the vibeDine ordering engine as an **online delivery shop** attached to a restaurant's own **branded landing page**, with an **optional per-restaurant customer account** and **Netopia** online payment — while the same restaurant keeps running in-restaurant **vibeDine QR** ordering. The admin console must let one owner manage **both channels** coherently.

> **Relationship to the existing plan.** [`docs/delivery-embedded-storefront-plan.md`](../../delivery-embedded-storefront-plan.md) already designed the *table-less delivery channel* mechanics (nullable `TableId` + `OrderType` + `DeliveryDetails`, reuse of the anonymous menu→cart→submit→SignalR pipeline, `Restaurant.Slug`-keyed storefront). This spec **builds on it and supersedes it in three areas that plan left out or deferred**: (1) a **per-restaurant customer userbase/accounts**, (2) **Netopia online payment as a launch requirement** (that plan deferred online pay to a gated phase 2 and assumed Stripe/COD), and (3) a **first-class two-channel admin experience** (that plan's admin section was a badge + one settings tab). Where this spec and the older plan disagree, this spec wins.

---

## 1. Locked decisions (from brainstorming)

| # | Decision | Choice | Consequence |
|---|---|---|---|
| D1 | Customer accounts | **Per-restaurant** (tenant-scoped) | `Customer` is an `ITenantScoped` entity; a diner has separate accounts per restaurant; admin gets a per-restaurant CRM. |
| D2 | Payment at launch | **Netopia online, required** (COD kept as an optional toggle, not launch-blocking) | Payment is greenfield; provider-pluggable; the anonymous-write **session-ownership token is launch-blocking** because real money crosses an internet-facing form. |
| D3 | Landing page | **Config-driven white-label template, we host** | Restaurant sets a few content fields + branding in admin; we render + host at `{slug}.vibedine.app` or a custom domain (CNAME). "Owned" = their brand + domain, our engine. |
| D4 | Admin order-management IA | **Dedicated "Livrare" section**, dine-in untouched, Dashboard becomes the channel-aware rollup | Delivery gets its own board tuned to its lifecycle; zero regression to the dine-in kanban/floor/waiter flow. |

**Design principle threaded through everything:** *channels are separate in day-to-day operations, unified in insight.* Fulfillment is channel-specific; Dashboard + Customers roll both up. The admin nav is **channel-adaptive** — surfaces appear based on what the restaurant has enabled.

---

## 2. The three customer surfaces

All three are painted by the **existing white-label brand** (`Restaurant.BrandingEnabled` + derived palette from `@shared/branding`). No new theming system.

| Surface | URL | What it is | Build status |
|---|---|---|---|
| **Landing** | `{slug}.vibedine.app` (or custom domain via CNAME) | Config-driven marketing home we host: hero, hours, about, gallery, socials, a **"Comandă livrare"** CTA, and optional account **login/register**. | **New** (reuses branding) |
| **Delivery storefront** | `/comanda` on that domain (slug-scoped) | The vibeDine ordering engine, **table-less**: menu → cart (happy hour, Vibey recs, flash deals) → checkout (address/contact, **autofilled if logged in**) → **Netopia payment** → live order status. | **Extend** the engine |
| **In-restaurant QR** | `/:qrToken` (existing customer SPA) | Existing dine-in flow. | **Untouched** |

### Reuse map

- **Reuse as-is:** menu read (`GET /api/restaurants/{rid}/menu`, already anonymous + tenant-scoped), cart→PATCH→submit pipeline, SignalR order-status, happy hour, Vibey recommendations, branding + paint-on-boot, inventory/stock decrement, org/team/roles.
- **Extend:** `Order` (nullable `TableId` + `OrderType` + `CustomerId`), Dashboard (channel metrics), active-orders realtime feed, `OrderFinalizationService` (channel-aware archival), `TenantResolutionMiddleware` (customer-JWT branch — see §7).
- **Net-new:** customer accounts + CRM, `DeliveryDetails`, Netopia payments + webhooks + `PaymentTransaction`, landing config + hosting, delivery board + settings tabs, per-session ownership token.

**Channel nuance:** the flash-deal **2-minute post-submit window is dine-in-only** — delivery orders dispatch straight through (there is no waiter to claim). Happy hour and Vibey **do** apply to delivery.

---

## 3. Admin experience (the centerpiece)

### 3.1 Guiding principles

1. **Separate in operations, unified in insight** — delivery has its own board; Dashboard + Customers unify.
2. **Dine-in untouched** — the kanban / floor / waiter-calls flow does not change.
3. **Channel-adaptive nav** — a QR-only restaurant sees today's console unchanged; a delivery-only one hides floor/QR/waiter; a both-channels one sees everything cleanly separated.
4. **Shared config stays shared** — menu, inventory, happy hour, flash deals, Vibey rules, branding, team all apply to both channels from one place.

### 3.2 Navigation (★ new, ⟳ changed; everything else unchanged)

```
Operațiuni
  Dashboard          ⟳ channel-aware rollup
  Comenzi active       dine-in kanban — unchanged
  Chemări              unchanged
  Jurnal comenzi     ⟳ add channel filter + delivery rows
Livrare              ★ NEW GROUP  (visible only when Restaurant.DeliveryEnabled)
  Livrări active     ★ delivery board (own lifecycle)
  Clienți            ★ per-restaurant customer CRM
Meniu & AI            Meniu / Inventar / Happy Hour / Flash Deal / Reguli — shared, unchanged
Sală & locație         Sala / Coduri QR — gated on "has tables"
Echipă                 Ospătari
Setări
  … Brand …            existing
  Livrare            ★ toggle, hours, fee, zone, min order, prep/ETA, embed snippet
  Pagina de start    ★ landing content + domain
  Plăți / Netopia    ★ merchant creds, transactions, refunds
```

Enforcement note: role/visibility gating in this codebase is **imperative and duplicated in three places** — `App.vue` nav `show` flags, the router `beforeEach` `WAITER_ALLOWED` whitelist, and per-view computeds. Every new surface below must add gating in **all three**. There is no declarative `meta.roles`.

### 3.3 ① Livrări active — the delivery board

A dedicated board tuned to the delivery lifecycle (not the kitchen kanban). Columns:

```
Noi (Accept/Reject) → În pregătire → Gata → În livrare → Livrat     [+ Respinse / Anulate filter]
```

Card content — **no table/waiter chrome**; instead what delivery ops need:
- Order # + placed-at + **requested-for** (ASAP or scheduled slot)
- Customer **name**, **tap-to-call phone**, **delivery address** (map link — phase 2)
- Items + subtotal + **delivery fee** + total
- **Payment badge:** `PLĂTIT (Netopia)` / `Rambursat` / `COD` (if COD enabled)
- **State-specific actions:** `Accept` / `Respinge (→ Netopia refund)` on new; `Preia la livrare` at *Gata*; `Marchează livrat` at *În livrare*.
- **New-order sound + toast** (reuse the waiter-call chime pattern from `useWaiterCallsStore`).

Realtime: consumes the staff SignalR `admin` group, payload extended with `orderType` + delivery contact/address/payment. **No 45-second auto-archive** — a delivery order is not "done" at *ready*; it archives only after *Livrat* (delivered), so `OrderFinalizationService` becomes channel-aware.

**Kitchen reality (OPEN — see §10):** the kitchen prepares food for both channels. MVP has kitchen staff watch both boards. A unified **"Bucătărie" prep queue** (food-to-cook across both channels, no fulfillment chrome) is flagged as phase-2 unless you want it in scope now.

### 3.4 ② Clienți — per-restaurant CRM

Because accounts are tenant-scoped (D1), the admin gets a customer database for that location:
- **List:** name, phone, email, # orders, total spent, last order, saved-address count. Search by name/phone.
- **Detail:** contact, saved addresses, order history (delivery; dine-in only if ever linked), marketing-consent flag.
- **GDPR:** PII → manager-only; **export** + **delete** a customer.
- CRM-lite; loyalty/campaigns are future.

### 3.5 ③ Settings — three new tabs

- **Livrare:** master **enable toggle** (reveals the whole section + the storefront), delivery **hours** (in `Restaurant.Timezone`), **delivery fee**, **minimum order**, **delivery zone** (radius or postal-code list for MVP; polygon later), default **prep-time / ETA**, optional **COD toggle**, and the **storefront URL + embed/link snippet**.
- **Pagina de start (Landing):** hero photo + headline/tagline, about text, hours, address/map, gallery, socials, and the **domain** (`{slug}.vibedine.app` default; optional **custom domain** via CNAME + verification). **Live preview**, reusing brand colors/logo.
- **Plăți / Netopia:** merchant **credentials** (signature/API key — **encrypted at rest, write-only in the UI**), **sandbox/live** environment, and a **transactions view** (amount, status, Netopia reference, refunds) with **manual refund**. Status here is authoritative, driven by IPN webhooks (§6).

### 3.6 ④ Dashboard — the "manage everything" rollup

Today's dashboard is entirely promo/AI KPIs assuming dine-in. It gains:
- A **channel split**: `Toate / La masă / Livrare` on the KPI row.
- **Delivery KPIs:** orders + revenue by channel, delivery avg ticket, **fulfillment time** (accept→delivered), **acceptance/rejection rate**, prepaid vs COD.
- Existing promo KPIs preserved, now **channel-attributable**.

This is the single screen where the owner sees the whole business across both channels.

### 3.7 ⑤ Roles

Reuse OrgOwner / RestaurantAdmin / Waiter:
- Delivery board, Customers, and the three new Settings tabs are **manager-gated** (OrgOwner/RestaurantAdmin), like other management surfaces.
- Waiters: unchanged whitelist; delivery is not their concern by default.
- A dedicated **"Dispecer" delivery-operator role** (handle delivery orders without full admin) is a natural **phase-2** — it touches the `Membership` model, so out of scope now.

### 3.8 ⑥ Channel-adaptive nav in practice

| Restaurant setup | What they see |
|---|---|
| **QR-only** (`DeliveryEnabled=false`) | Exactly today's console. No Livrare group, no delivery/landing/payment settings tabs; Dashboard = dine-in only. |
| **Delivery-only** (no tables) | Sala / Coduri QR / Chemări hidden or de-emphasized; Livrare front-and-center. |
| **Both** | Everything, cleanly separated by channel. |

---

## 4. Customer accounts (per-restaurant)

Fully net-new — the customer side is 100% anonymous today (no login anywhere). Accounts are **optional**: guest checkout always works.

### 4.1 Entities (all `ITenantScoped`)

- **`Customer`** — `Id`, `RestaurantId`, `Name`, `Phone`, `Email`, `EmailConfirmed`, `PasswordHash`, `MarketingConsent`, `CreatedAt`, `LastOrderAt`. Uniqueness of email/phone is **per restaurant**, not global.
- **`CustomerAddress`** — `Id`, `RestaurantId`, `CustomerId`, `Label`, `AddressLine`, `Notes`, `Lat?`, `Lng?`, `IsDefault`. Powers checkout autofill.
- `Order.CustomerId Guid?` links a delivery order to an account (null for dine-in and guest-delivery orders).

### 4.2 Auth

Customer auth is **separate from staff auth** (staff = `AppUser` + membership + JWT). A `CustomerAuthController` issues a **customer JWT scoped to `{restaurantId, customerId}`** with a distinct audience/claim so it can never be mistaken for a staff token. `TenantResolutionMiddleware` gets a branch that accepts a customer JWT and resolves tenant from its `restaurantId` claim (membership check is skipped, as it already is for anonymous `restaurantId`-route customers).

- **MVP recommendation:** email + password, reusing the existing email sender (`IEmailSender`: mailpit dev / Resend prod) for optional confirmation + reset. Phone captured as a field.
- **Alternative (noted):** passwordless — phone OTP (needs SMS infra/cost) or email magic-link.
- ⚠️ Customer auth is a **new internet-facing auth surface**. Do **not** naively reuse the refresh-token / email-confirmation flows flagged as weak in `CR_notes/CODE_REVIEW_SEGMENT_05_AUTH.md`; harden them here.

### 4.3 Autofill

A logged-in customer's default address + contact **pre-fills** the delivery checkout — this is the "which can be done automatically if you have an account" requirement. Guests fill it manually; after a guest order we may offer "create an account to save this."

---

## 5. Landing page (config-driven, hosted)

- **Content model:** a `LandingConfig` **jsonb** blob on `Restaurant` (mirrors the `jsonb` precedent `MenuImportJob.ResultJson`): `{ heroPhotoUrl, headline, tagline, about, hours, addressText, mapLat/Lng, gallery[], socials{}, ctaLabel }`. Colors/logo come from existing branding.
- **Rendering:** a config-driven template (RO/EN). Reuses branding paint-on-boot so the first frame is on-brand.
- **Hosting:** we serve it at `{slug}.vibedine.app`. **Custom domain** = restaurant adds a CNAME → our host; admin shows a **verification** step + status. (Beware the Railway host/PORT + wildcard pitfalls documented in `frontend-customer/CLAUDE.md` around the reverted `74ff4fc→83ff64c` page-server work — do not reintroduce an Express page-server without accounting for both.)
- **Entry to ordering:** the "Comandă livrare" CTA links to `/comanda` (same domain, slug-scoped). Account login/register lives on the landing.

---

## 6. Netopia payment integration

Greenfield — no payment code exists today (`Order.PaymentStatus` is an inert string, never advanced). Provider-pluggable in the house style (mirrors `AI_*_PROVIDER`).

### 6.1 Abstraction

`IPaymentProvider` with a `NetopiaPaymentProvider` implementation: `CreatePaymentSession(order) → redirect params`, `VerifyCallback(payload) → result`, `Refund(transaction) → result`. Keeps room for other processors without touching order code.

### 6.2 Flow — prepaid, pay-first (matches the described UX)

```
1. Checkout submitted → POST /delivery/orders  → Order{type:delivery, status:pending_payment}
                                                   + DeliveryDetails; returns {orderId, sessionToken}
2. POST /delivery/orders/{id}/pay              → backend builds a SIGNED Netopia session; returns redirect
3. Customer pays on Netopia's hosted page
4. Netopia IPN → POST /api/payments/netopia/ipn (anonymous, SIGNATURE-VERIFIED, idempotent)
     success → PaymentStatus=paid, write PaymentTransaction, status→submitted (enters board "Noi"),
               broadcast to admin
     failure → status→payment_failed (customer may retry)
5. Customer redirected to /comanda/status/{orderId} → live SignalR status
```

- **Authoritative confirmation is the IPN**, never the browser redirect.
- **Refund-on-reject:** staff `Respinge` → `POST /admin/orders/{id}/reject` → `IPaymentProvider.Refund` → `PaymentStatus=refunded`, order `rejected`.
- **MVP = charge + refund.** `authorize + capture` (capture on Accept, void on Reject — avoids refund friction) is noted as a refinement; Netopia's hosted flow typically captures immediately, so charge+refund is simpler for MVP.
- **Idempotency:** IPN retries are de-duplicated on the Netopia order reference via `PaymentTransaction`.
- **COD (if enabled, D2):** skip steps 2–4; order goes straight to `submitted` with `PaymentMethod=cod`, `PaymentStatus=unpaid` (reuses today's flow); staff mark paid on delivery.

### 6.3 `PaymentTransaction` (`ITenantScoped`)

`Id`, `RestaurantId`, `OrderId`, `Provider`, `ProviderRef`, `Amount decimal(10,2)`, `Status`, `RawPayload jsonb`, `CreatedAt`. Audit trail + idempotency + refund source.

### 6.4 Credentials

Per-restaurant Netopia merchant config (each restaurant has its own merchant account), **encrypted at rest** — either encrypted columns on `Restaurant` or a dedicated `PaymentConfig` entity; `Environment` = sandbox|live.

---

## 7. Backend data model, endpoints, realtime

### 7.1 Data model changes

- **`Order`:** add `OrderType string` (`dine_in`|`delivery`|`takeaway`, default `dine_in`); make **`TableId` nullable** (`Guid?`) + relax the FK (currently non-nullable at `AppDbContext` Order mapping); add `CustomerId Guid?`; add `PaymentMethod string?` (`online`|`cod`); **actually use** `PaymentStatus` (`unpaid`→`paid`→`refunded`). New index `(RestaurantId, OrderType, Status)` for the board query.
- **`DeliveryDetails`** (`ITenantScoped`, 1:1 with `Order`): `CustomerName`, `CustomerPhone`, `CustomerEmail?`, `AddressLine`, `AddressNotes?`, `Lat?`, `Lng?`, `RequestedFor timestamptz?` (null = ASAP), `DeliveryFee decimal(10,2)`, `QuotedEtaMinutes?`, `CreatedAt`.
- **`Customer`**, **`CustomerAddress`**, **`PaymentTransaction`** — as above.
- **`Restaurant`:** delivery config (`DeliveryEnabled`, `DeliveryFee`, `MinOrderAmount`, `DeliveryHours jsonb`, `DeliveryZone jsonb`, `DefaultPrepMinutes`, `CodEnabled`), landing config (`LandingEnabled`, `LandingConfig jsonb`, `CustomDomain`, `DomainVerified`), payment config (encrypted Netopia creds + `Environment`) or a `PaymentConfig` side entity.
- **`AppDbContext`:** register every new entity with the tenant global query filter + snake_case mapping + `restaurant_id` index; `HasOne(Order).WithOne(DeliveryDetails)`. **Migration:** `AddDeliveryChannelAndAccounts` (prod never auto-migrates; Dev auto-migrates + seeds).

### 7.2 Order status vocabulary (free-form strings — keep the pattern, no enum)

- **Dine-in (unchanged):** `draft → pending_dispatch → submitted → preparing → ready → completed` (+ `cancelled`, `merged`).
- **Delivery (new states added alongside):** `draft → pending_payment → submitted → preparing → ready → out_for_delivery → delivered` (+ `payment_failed`, `rejected`, `cancelled`). "Accept" = `submitted→preparing`; "Reject" = `submitted→rejected` (+refund). No separate `accepted` state (the board's *Noi* column = paid `submitted` delivery orders awaiting Accept).

### 7.3 Endpoints (all customer endpoints anonymous or customer-JWT; tenant-checked)

- **Public bootstrap:** `GET /api/storefront/{slug}` → `{ restaurantId, name, branding, deliveryEnabled, deliveryFee, minOrder, hours, zone, landing }` (resolve slug→restaurant; handle closed/disabled). Reuse `GET /api/restaurants/{rid}/menu`.
- **Customer accounts:** `POST /customer/register`, `/login`, `/refresh`, `GET/PUT /customer/me`, `GET/POST/DELETE /customer/addresses` (customer-JWT).
- **Delivery ordering:** `POST /api/restaurants/{rid}/delivery/orders` (create draft + `DeliveryDetails`; returns `{orderId, sessionToken}`; links `CustomerId` if authed) · reuse `PATCH .../orders/{id}/items` (+ **session-token** check) · `POST .../delivery/orders/{id}/pay` (Netopia session) · `GET .../delivery/orders/{id}` (status poll, session-token gated) · submit is folded into pay/IPN for the online path.
- **Payments webhook:** `POST /api/payments/netopia/ipn` (anonymous, **signature-verified, idempotent**).
- **Admin:** extend `GET .../admin/orders` DTO with `orderType` + delivery contact/address/payment · `POST .../admin/orders/{id}/accept|reject|out-for-delivery|delivered` · `.../admin/orders/{id}/refund` · `GET/PATCH .../admin/delivery-settings` · `GET/PATCH .../admin/landing` · `GET/PATCH .../admin/payment-config` · `GET .../admin/customers` + `/{id}` + delete/export · `GET .../admin/payments/transactions`.

### 7.4 SignalR

- `TenantGroups.cs`: add a per-order customer group `r:{rid}:o:{orderId}` (no table for delivery).
- `CustomerHub`: add `JoinOrderGroup(restaurantId, orderId)` (+ leave); **verify order belongs to tenant/session before joining**.
- Broadcast `OrderStatusUpdated` (incl. new `out_for_delivery`/`delivered` status values) to the per-order group; broadcast to staff `admin` group with an added `orderType`. **Event names PascalCase, payloads camelCase** — the customer SPA hand-parses frames (`useWebSocket.ts`); renaming/recasing breaks it silently.

### 7.5 Services

- `OrderFinalizationService`: **channel-aware** — do not auto-archive delivery at `ready`; archive after `delivered` (grace).
- Dispatch: reuse the `DispatchToKitchenAsync` status-guard idempotency; **delivery bypasses the 2-minute flash-deal window**.
- New: hours/zone/min-order validation helper used at order create/pay.

---

## 8. Security & multi-tenancy

- **Session-token ownership binding (LAUNCH-BLOCKING, D2):** each delivery draft gets an opaque per-session token returned at create, required on every `PATCH items` / `pay` / status read. Without it, `orderId` is a guessable GUID an attacker could enumerate/mutate — unacceptable once real money and PII cross an internet-facing form. This is the top prerequisite.
- **Tenant isolation (#1 invariant):** every new entity is `ITenantScoped`; every new query filtered; the SignalR order-group join verifies tenant/order ownership; never accept a client-supplied `restaurantId`/`orderId` without an ownership check.
- **Netopia IPN:** signature-verified per Netopia's spec, idempotent, treated as the **authoritative** money signal (never the browser redirect).
- **Encrypted payment credentials** at rest; write-only in the admin UI.
- **PII / GDPR:** customer + delivery data is personal — retention policy, access limited to the tenant, export + delete, timestamps `timestamptz` UTC.
- **Abuse:** rate-limit order create / pay / register / login per IP+origin (public forms).
- **CORS/CSP:** we host landing + storefront on our-managed domains, so third-party framing is less exposed than the older iframe plan; still add `frame-ancestors` if any embedding is offered, and keep the CORS allow-list honest for custom domains.

---

## 9. Phased delivery (vertical slices / "chunks", house convention)

Each chunk is a coherent full-stack slice; dine-in stays green throughout.

1. **Data model & channel** — `Order` changes, `DeliveryDetails`, `Restaurant` delivery/landing/payment columns, `AppDbContext`, migration, shared types. *Accept:* migration applies; dine-in unaffected; a delivery order can be created via API.
2. **Delivery ordering backend** — `StorefrontController`, `DeliveryController` create + **session-token binding**, hours/zone/min-order validation, channel-aware dispatch + finalization. *Accept:* full delivery lifecycle over the API; tenant isolation verified.
3. **Customer accounts** — `Customer`/`CustomerAddress`, `CustomerAuthController`, addresses, order linkage, autofill contract, customer-JWT middleware branch. *Accept:* register/login/save-address/autofill work; guest path intact.
4. **Netopia payments** *(launch-blocking with chunk 2's token)* — `IPaymentProvider` + `NetopiaPaymentProvider`, `pay` endpoint, **IPN webhook**, `PaymentTransaction`, refund, encrypted payment-config. *Accept:* sandbox pay→IPN→board; reject→refund; idempotent IPN.
5. **Storefront SPA** — `/comanda` slug route, bootstrap, reuse menu/cart, checkout (address/contact + autofill), Netopia redirect + return, live status, RO/EN. *Accept:* place + pay a delivery order end-to-end in a browser.
6. **Landing SPA + hosting** — config-driven landing, `{slug}` subdomain + custom-domain verification, account login/register UI. *Accept:* branded landing renders from config; CTA → storefront; custom domain verifies.
7. **Admin two-channel** — Livrare nav group, delivery board + lifecycle actions, Customers CRM, Settings tabs (Livrare/Landing/Plăți), channel-aware Dashboard, role gating in all three enforcement points. *Accept:* staff see/accept/fulfill delivery orders; configure delivery + landing + Netopia; Dashboard splits by channel.
8. **Hardening** — rate limiting, CSP/CORS, GDPR export/delete, transaction audit, tests + manual smoke runbook.

---

## 10. Open questions & non-goals

**Open (need your call before/at implementation):**
- **Unified "Bucătărie" prep queue** — one food-to-cook screen across both channels: in scope now, or phase-2? (Spec assumes phase-2.)
- **Jurnal comenzi** — one unified log with a channel filter (spec's default), or split per channel?
- **Payment capture model** — charge+refund (MVP default) vs authorize+capture+void.
- **Customer auth method** — email+password (default) vs phone-OTP vs magic-link.
- **Delivery zone** — radius vs postal-code list for MVP (polygon deferred).
- **Custom-domain hosting** — confirm the Railway host/PORT/wildcard constraints from `frontend-customer/CLAUDE.md` are accounted for before promising custom domains at launch.

**Non-goals (natural follow-ups):** third-party courier integration (Glovo/Bolt), driver app / live tracking map, delivery-specific menu pricing, loyalty/marketing campaigns, cross-restaurant shared customer identity, `takeaway` channel (the `OrderType` value exists but the UX is out of scope), the dedicated "Dispecer" role.

---

### Appendix — key existing files this touches

**Backend:** `Models/Order.cs`, `Models/Restaurant.cs`, `Data/AppDbContext.cs`, `Tenancy/TenantResolutionMiddleware.cs`, `Controllers/CustomerController.cs` + `AdminController.cs`, `Services/OrderFinalizationService.cs` + `FlashDealService.cs`, `Hubs/CustomerHub.cs` + `TenantGroups.cs`, `Program.cs` (CORS/CSP/DI); new `Models/DeliveryDetails.cs` + `Customer.cs` + `CustomerAddress.cs` + `PaymentTransaction.cs`, new `Controllers/StorefrontController.cs` + `DeliveryController.cs` + `CustomerAuthController.cs` + `PaymentsController.cs`, new `Services/Payments/IPaymentProvider.cs` + `NetopiaPaymentProvider.cs`, migration.
**Shared:** `shared/types/models.ts`, `api-requests.ts`, `api-responses.ts` (+ `menu-validation.ts` for new field limits) — edit `.ts`, keep hand-synced with C# DTOs.
**Customer:** `src/router/index.ts`, new `stores/useDeliveryStore.ts` + `useCustomerAuthStore.ts`, reuse `composables/useMenu.ts` + `stores/useCartStore.ts` + `composables/useWebSocket.ts`, new checkout + landing screens, `useLocale.ts`.
**Admin:** `App.vue` nav, `router/index.ts` guard, new `views/DeliveryBoardView.vue` + `CustomersView.vue` + `Settings/{DeliveryTab,LandingTab,PaymentsTab}.vue`, `DashboardView.vue` (channel split), `services/api-client.ts`.

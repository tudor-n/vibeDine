# vibeDine Logistic — the sibling app, seen from this repo

> Stock, suppliers, costing, VAT and **fiscal receipts**. It lives in a **separate
> repository** with its own compose file, but it attaches to *this* stack: the same
> Postgres database, the same Docker network, and the same restaurant JWTs.

This document is written for someone working in **this** monorepo. It explains what Logistic
touches here, what this repo owes it, and where the line between the two sits. Logistic's own
architecture, endpoints and screens are documented in **its** repo (`CLAUDE.md` + `docs/`) —
that is the source of truth for anything inside it.

| App | Frontend | Backend | Repo |
|-----|----------|---------|------|
| Customer | `frontend-customer` (:3000) | `RestaurantApi` (:5005) | this one |
| Admin + Waiter Mode | `frontend-admin` (:3001) | `RestaurantApi` (:5005) | this one |
| HQ | `frontend-platform` (:3002) | `PlatformApi` (:5006) | this one |
| Ops | `frontend-ops` (:3003) | `OpsApi` (:5007) | this one |
| **Logistic** | **`frontend-logistic` (:3004)** | **`LogisticApi` (:5008)** | **separate** |

---

## 1. What it is for

Logistic is the back-of-house half of the product — everything that happens between buying
an ingredient and printing a fiscal receipt:

- **Suppliers & purchasing** — supplier catalogue, price lists, purchase orders.
- **Receiving & stock locations** — goods-receiving notes, multi-location stock, stock counts,
  waste and manual adjustments, each with a movement-journal row.
- **Costing** — weighted-average cost per ingredient, dish cost, margin reporting.
- **VAT** — rates and per-product assignments.
- **Fiscal receipts** — the legally required Romanian receipt, issued through a fiscal
  printer/register via the FiscalLink providers, plus Z/X reports and storno.

The last one is why Logistic matters to this repo: **closing a bill in Waiter Mode is what
triggers a fiscal receipt.** See `docs/waiter-app.md`.

---

## 2. How it attaches to this stack

```
frontend-logistic (:3004)                 frontend-admin (:3001)
   │  VITE_API_URL      → :5008              │
   │  VITE_PLATFORM_API_URL → :5005 (login)  │
   ▼                                          ▼
LogisticApi (:5008)  ─── validates the SAME restaurant JWTs ───  RestaurantApi (:5005)
   │                                          │
   │  leases order_events rows                │  INSERTs order_events rows
   ▼                                          ▼
            postgres (shared "restaurant" DB, :5433)
            └── docker network: restaurant-net (external, owned by THIS compose file)
```

Four attachment points, all of them deliberate:

1. **Same database.** `LogisticApi` connects to the same `restaurant` DB with
   `Host=postgres;Database=restaurant;…`.
2. **Same network.** This repo's `docker-compose.yml` names its default network
   `restaurant-net`; Logistic's compose file joins it as an **external** network. Bring this
   stack up first, or Logistic's `docker compose up` fails with "network not found".
3. **Same auth realm.** Logistic does **not** create a fourth JWT realm. It validates the
   restaurant realm's tokens (issuer/audience `smart-restaurant`), so a manager signs in once
   and moves between the admin console and Logistic. Its login and refresh calls go to
   `RestaurantApi` (`POST /api/auth/login`, `POST /api/auth/refresh`); everything else goes to
   `:5008`. Access is granted by **membership**, not by a claim in the token.
4. **The `order_events` outbox.** Logistic's only inbound coupling to the restaurant domain is
   this table. It polls with a `FOR UPDATE SKIP LOCKED` lease (default 2 s, 120 s lease, 5
   attempts) and processes idempotently per event id.

---

## 3. ⚠️ Who owns the schema

`RestaurantApi` owns the shared schema. Logistic's own tables are all prefixed `logistic_*`
and are created by **idempotent bootstrap SQL** (`CREATE TABLE IF NOT EXISTS` /
`CREATE INDEX IF NOT EXISTS`) at startup — there are **no EF migrations in `LogisticApi`**,
the same rule HQ and Ops follow.

**The only writes Logistic makes to shared-schema tables:**

| Table | Column(s) | Why |
|---|---|---|
| `ingredients` | `quantity_on_hand` | Logistic-originated movements only: goods receiving, manual adjustments, stock-count corrections, waste — each written atomically alongside its `logistic_stock_movements` journal row |
| `order_events` | `processed_at`, `attempts`, `lease_id`, `lease_expires_at` | the consumer's lease/progress bookkeeping |

Everything else on the shared schema is **read-only** to Logistic.

The mirror-image rule holds here: `RestaurantApi` only ever **INSERTs** `order_events` rows
and never touches the lease/processed columns.

**Stock is decremented for dispatch exactly once, in `RestaurantApi`**, guarded by
`Order.StockDecremented`. When Logistic consumes an `order.dispatched` event it re-derives the
ingredient deltas purely for **valuation** — consumption movements at current weighted-average
cost — and must never apply that quantity change to `ingredients.quantity_on_hand` a second
time. `order.cancelled` writes compensating `order_restore` movements the same way.

If Logistic ever needs a new column on a shared table, it is added **here**, by an EF
migration in `backend/RestaurantApi`, and then mapped over there. Never the other way round,
and never by hand-run SQL.

---

## 4. The fiscal flow

```
Waiter Mode: "Notă de plată" → close with cash / card / mixed
   ▼
RestaurantApi  TableSessionService.CloseAsync
   │  session row → status='closed', closed_reason='paid', payment/total snapshot
   │  + order_events row  event_type='session.closed'   ← SAME transaction
   ▼
LogisticApi  outbox consumer (lease, exactly-once per event id)
   │  closedReason='paid'         → enqueue ONE fiscal job, keyed on the session id
   │  closedReason='force_released' → ignored fiscally (audit only)
   ▼
fiscal job queue (logistic_fiscal_jobs)  → IFiscalProvider
   │  mock (Development default) | FiscalLink LAN | FiscalLink Cloud
   ▼
receipt journal (logistic_fiscal_receipts) + Z/X reports (logistic_fiscal_reports)
```

The `session.closed` payload this repo emits is the contract between the two:

```jsonc
{
  "sessionId": "…", "tableId": "…", "tableNumber": "12",
  "waiterUserId": "…", "waiterName": "Ana Popescu",
  "paymentMethod": "mixed",        // null when closedReason = 'force_released'
  "cashAmount": 100.00, "cardAmount": 87.50, "totalAmount": 187.50,
  "closedReason": "paid",          // 'paid' | 'force_released'
  "orderIds": ["…", "…"],
  "closedAt": "2026-08-05T19:42:00Z"
}
```

Two invariants worth remembering when touching the close path here:

- **A receipt is never issued until the provider confirms it.** Provider failures leave the
  job retryable; they do not fabricate a receipt row.
- **No online payment, ever.** The money changes hands at the table. This platform records
  *which method* was used and issues the receipt — there is no PSP and no card data anywhere.

The full event catalogue (`order.dispatched`, `order.cancelled`, `order.completed`,
`session.opened`, `session.transferred`, `session.closed`) is documented in
`docs/waiter-app.md` §3 — that is the authoritative list, since this repo emits it.

---

## 5. What this repo carries for Logistic

Only two things. Both are small, and both are here rather than there because of the ownership
rules above.

| Companion step | Where | Why |
|---|---|---|
| The `order_events` table (+ `table_service_sessions`, the `orders` columns, `order_items.notes`) | EF migration `AddWaiterModeAndOrderEvents` in `backend/RestaurantApi` | shared schema — only `RestaurantApi` may migrate it |
| `AllowedOrigins__2: "http://localhost:3004"` | `docker-compose.yml`, service `backend-api` | `frontend-logistic` calls `RestaurantApi` directly for login/refresh, so its origin needs CORS |

Apply the CORS line with:

```bash
docker compose up -d --build backend-api
```

Nothing else in this repo knows Logistic exists — no client, no HTTP call, no shared code. If
you find yourself adding an outbound call from `RestaurantApi` to `:5008`, stop: the coupling
is meant to stay one-way and asynchronous, through `order_events`.

---

## 6. Config / env (Logistic's side, for reference)

These live in **Logistic's** compose file, not this one. Listed so the two stacks can be
reasoned about together.

| Env var | Value in dev | Meaning |
|---------|--------------|---------|
| `ConnectionStrings__Default` | `Host=postgres;Database=restaurant;Username=dev;Password=devpass` | the shared DB, over `restaurant-net` |
| `Jwt__Key` / `Jwt__Issuer` / `Jwt__Audience` | **the restaurant realm's values** | must match `RestaurantApi` exactly or every token is rejected |
| `Logistic__Encryption__Key` | dev key | AES-GCM at rest for external credentials (FiscalLink API keys) — never sent to a browser |
| `Logistic__Sync__PollIntervalMs` | `2000` | `order_events` poll interval |
| `Logistic__Fiscal__*` | mock provider | fiscal provider selection + per-register settings |
| `AllowedOrigins__0` | `http://localhost:3004` | CORS for `frontend-logistic` |
| `VITE_API_URL` (frontend) | `http://localhost:5008/` | LogisticApi |
| `VITE_PLATFORM_API_URL` (frontend) | `http://localhost:5005/` | RestaurantApi — **auth calls only** |

In this repo, the only Logistic-related setting is the `AllowedOrigins__2` line in §5.

---

## 7. Running both stacks together

```bash
# 1. this repo first — it creates the restaurant-net network and the database
docker compose up -d

# 2. then, in the Logistic repo
docker compose up -d --build
```

Then: admin console <http://localhost:3001>, Logistic <http://localhost:3004> — signing in
with the same restaurant credentials in both.

Check the outbox is flowing:

```bash
# unprocessed events (should drain within a couple of seconds while Logistic is up)
docker compose exec postgres psql -U dev -d restaurant \
  -c "select event_type, count(*) from order_events where processed_at is null group by 1;"
```

A backlog that never drains means Logistic is down, pointed at the wrong DB, or its JWT
config does not match this repo's.

---

## 8. Not built yet / next steps (from this repo's point of view)

1. **No dead-letter surfacing.** Events that exhaust their attempts are marked processed with
   a logged error on Logistic's side; nothing in the admin console shows a stuck receipt.
2. **No reconciliation view.** There is no screen here that answers "did every closed session
   get a receipt?" — the join exists (`table_service_sessions` × Logistic's journal), the UI
   does not.
3. **Waiter/session reporting** lives nowhere yet; `order_events` +
   `table_service_sessions` are the intended source and Logistic is the intended home.
4. **`order_events` is never pruned.** It grows forever. A retention job (delete processed
   rows older than N months) belongs on the Logistic side, since it owns `processed_at`.

---

## 9. Gotchas

- **Bring this stack up first.** `restaurant-net` is created by *this* compose file; Logistic
  joins it as an external network and fails to start if it does not exist.
- **JWT config must match byte-for-byte.** Same key, issuer and audience as `RestaurantApi`.
  A mismatch looks like "Logistic logs me out immediately" — the login succeeded (it went to
  `:5005`) and only the `:5008` calls are 401ing.
- **`order_events` has no tenant query filter here** and is cross-restaurant by design;
  any query against it in `RestaurantApi` must filter `restaurant_id` explicitly.
- **Do not add an EF migration to Logistic**, and do not add shared-schema columns from
  Logistic — that direction is what silently breaks this repo's migration history.
- **Do not double-decrement stock.** Dispatch consumption is `RestaurantApi`'s, exclusively.
- **A force-released session still emits `session.closed`.** It is an audit event, not a
  fiscal one; `closedReason` is the only thing separating them.
- **Logistic's own docs are in its repo.** Anything in this file about Logistic's internals
  is a summary written for this repo's readers — if the two disagree, its repo wins.

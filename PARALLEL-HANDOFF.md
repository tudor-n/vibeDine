# PARALLEL-HANDOFF

Cross-stream notes for the Waiter Mode work. Three streams run in this repo against the same
frozen contract (Appendix B): **W1** backend (`RestaurantApi`), **W2** waiter-mode frontend,
**W3** contracts + realtime + tests + docs.

**Append-only. Write only under your own heading.**

## W1

_(RestaurantApi: sessions, waiter endpoints, allow-list, SignalR events, the `order_events`
outbox, the migration.)_

## W2

_(Waiter mode frontend: WaiterShell, mode switch, `/waiter/*` views, cart,
`services/waiterApi.ts`.)_

## W3

Branch: `feature/waiter-mode-contracts-realtime`. Landed in five commits, one per task.

### Delivered

| File | Note |
|---|---|
| `shared/types/waiter.ts` | the frozen B.7 contract — landed **first**, W2 can import it now |
| `frontend-admin/src/stores/useWaiterRealtimeStore.ts` | `defineStore('waiterRealtime', …)`, the frozen B.8 interface |
| `frontend-admin/src/__tests__/useWaiterRealtimeStore.spec.ts` | 15 tests, green |
| `frontend-admin/src/__tests__/waiterTypes.spec.ts` + `waiterContractFixtures.ts` | contract guard, 11 tests, green |
| `frontend-admin/src/__tests__/waiterApi.spec.ts` | **`describe.skip`** — waits on W2 |
| `frontend-admin/src/__tests__/useWaiterCartStore.spec.ts` | **`describe.skip`** — waits on W2 |
| `docs/waiter-app.md`, `docs/logistic-app.md` | in the `docs/vibedine-hq.md` shape |
| `docker-compose.yml` | one env line: `AllowedOrigins__2: "http://localhost:3004"` |

### Decisions taken where the prompt left room

1. **The store's REST seed is a module-local `fetch`, not `authedFetcher`.**
   `api-client.ts` keeps `authedFetcher` and `scoped` **module-private** (they are not
   exported), and that file belongs to W2 — so importing them today would not type-check.
   `useWaiterRealtimeStore.ts` therefore has a small `fetchFloor()` that replicates the two
   behaviours that matter: the bearer header and the single refresh-and-retry on 401.
   **TODO(W2 landing):** once `services/waiterApi.ts` exists, replace `fetchFloor()` with
   `waiterApi.getFloor()` and delete the local helper. It is marked with that TODO in the file.

2. **`signalRClient.ts`: no change needed — verdict recorded, file untouched.**
   Its generic `on<T>` / `off` / `onReconnected` / `joinRoleGroup` / `leaveRoleGroup` surface
   already carries every new event and both role groups. Adding typed helpers would have
   bought nothing the contract types in `@shared/types/waiter` don't already give callers.

3. **The contract guard is split across two files.** `tsconfig.json` excludes `*.spec.ts` from
   `type-check`, so annotated literals inside a spec get **no** compile-time coverage at all.
   The literals therefore live in `src/__tests__/waiterContractFixtures.ts` (a plain `.ts`
   module, so `npm run type-check` does cover it) and `waiterTypes.spec.ts` imports them and
   asserts their runtime key sets. Both directions of drift now fail something.

4. **The two conditional specs assemble their module path at runtime**
   (`['@/services', 'waiterApi'].join('/')` + `import(/* @vite-ignore */ …)`). A statically
   analysable `import('@/services/waiterApi')` fails Vite's import analysis and takes the
   **whole spec file** down at collection, even with every `describe` skipped. The runtime form
   resolves the `@` alias correctly (verified against an existing module), so **un-skipping is
   literally deleting the `.skip` markers** — four in `waiterApi.spec.ts`, four in
   `useWaiterCartStore.spec.ts`. No other edit is needed.

5. **Compose: one env line + one comment line.** The comment matches the file's own convention
   of annotating every non-obvious env var. Nothing else in `docker-compose.yml` was touched.

### CI review — verified, no changes

- `backend.yml` already runs `dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj`
  (and `OpsApi.Tests`), on a runner whose Docker daemon Testcontainers picks up. **W1's tests
  ride along with no workflow change.** Path filter `backend/**` covers the migration, the
  controller and the services.
- `frontend.yml` `build-admin` already runs `npm ci` → `type-check` → `build` → `test` in
  `frontend-admin`, and its path filters already include both `frontend-admin/**` and
  `shared/**`, so a change to `shared/types/waiter.ts` triggers it.
- **Expected diff was none, and none was made.**

**Observation, deliberately not changed:** every workflow's `on.push.branches` is
`[main, develop]`, but this repo's integration branch is **`development`** — no `develop`
branch exists (`git ls-remote` shows only `main` and `development`). Push builds on
`development` therefore never fire. Pull-request builds are unfiltered by branch, so PRs into
`development` *are* gated, and the only thing the dead filter skips is the placeholder deploy
job. Fixing it changes CI behaviour for all three streams mid-flight and is outside Waiter
Mode's scope — flagging it for the repo owner instead.

### Known-failing test, pre-existing, not ours

`frontend-admin/src/__tests__/brandCache.spec.ts` — 15 failures, `TypeError: Cannot read
properties of undefined (reading 'clear')` on `localStorage.clear()`. It fails **in
isolation** (`npx vitest run src/__tests__/brandCache.spec.ts`) on this branch and touches
nothing any stream added; there is no `setupFiles`, so no spec can affect another. Cause is
the local Node version (26): Node ≥22 defines its own experimental `globalThis.localStorage`,
which is `undefined` without `--localstorage-file` and shadows the one jsdom would install —
hence the `ExperimentalWarning: localStorage is not available` in every run. CI pins
`node-version: '20'`, which predates that global, so CI is unaffected. **Not fixed here**: the
spec is not a W3-owned file and the fix is an environment/config decision for the repo owner
(pin Node 20 locally via `.nvmrc`, or stub `localStorage` in the spec).

Everything else is green: **17 files passed, 2 skipped (the conditional specs), 141 tests
passed, 25 skipped.**

### Suite status at handoff

```
cd frontend-admin && npm run type-check     # clean
cd frontend-admin && npm test               # 141 passed · 25 skipped · 15 pre-existing failures (brandCache)
docker compose config -q                    # valid
```

---

## Final-integration runbook (W3) — run this AFTER all three streams have landed

1. **Backend suite** (Docker must be running — Testcontainers boots a real Postgres):
   ```bash
   DOTNET_ROLL_FORWARD=LatestMajor dotnet test backend/RestaurantApi.Tests
   ```
   Must include W1's concurrent-claim test: two simultaneous claims on one table ⇒ exactly one
   `201`, one `409`.

2. **Frontend gate:**
   ```bash
   cd frontend-admin && npm run type-check && npm test
   ```
   `type-check` now also covers W2's views and `waiterContractFixtures.ts`, so any drift
   between `shared/types/waiter.ts` and the C# DTOs surfaces here first.

3. **Un-skip the conditional specs** — delete the `.skip` from the four `describe.skip` blocks
   in `frontend-admin/src/__tests__/waiterApi.spec.ts` and the four in
   `useWaiterCartStore.spec.ts`, then re-run `npm test`. Nothing else in those files changes
   (see W3 decision 4). Expect real failures here if W2's function names or payload shapes
   drifted from B.4/B.8 — that is the point of the specs.

4. **Swap the store's REST seed** — replace `fetchFloor()` in
   `stores/useWaiterRealtimeStore.ts` with `waiterApi.getFloor()` (W3 decision 1), delete the
   local helper, re-run `npm run type-check && npm test`. The store spec stubs `fetch`, so it
   needs its mock retargeted to `@/services/waiterApi` at the same time.

5. **Resolve every TODO** under all three headings in this file, and every
   `// TODO(W2 landing)` / `// TODO(W1 landing)` marker in the tree:
   ```bash
   grep -rn "TODO(W1 landing)\|TODO(W2 landing)\|TODO(W3 landing)" \
     backend frontend-admin shared docs
   ```

6. **Apply the compose CORS change** (needed only if the Logistic stack is being run):
   ```bash
   docker compose up -d --build backend-api
   ```

7. **End-to-end smoke** — the real proof. Dev stack up
   (`docker compose up -d postgres backend-api`, `cd frontend-admin && npm run dev`), sign in
   as `supporter@demo.test` / `Demo1234!`:

   1. **Claim** a seeded table from `/waiter`. It becomes "Masa mea"; a second browser session
      sees the tile change within ~250 ms (`TableSessionOpened` → debounced floor refresh).
   2. **Waiter order with a note** — add two products, put a kitchen remark on one line,
      submit. It must land on the kitchen kanban (`/orders`) in `submitted` **immediately** —
      not `pending_dispatch` — showing the note and the waiter tag.
   3. **QR round attaches** — open the same table's customer page
      (`http://localhost:3000/<qrToken>`, tokens in `backend/RestaurantApi/Data/SeedData.cs`)
      and submit a diner order. It must appear on the same bill, with `source` still `'qr'`,
      and the session's `cumulativeTotal` must be the sum of both rounds.
   4. **Availability** — zero a product's stock from `/inventory`; the waiter order screen
      greys it out with no refresh (`MenuItemAvailabilityChanged` reaching `WaitStaff`).
   5. **Close** the bill with `cash`. The table returns to "Liberă".
   6. **Verify the outbox:**
      ```bash
      docker compose exec postgres psql -U dev -d restaurant -c \
        "select event_type, entity_id, created_at from order_events order by id;"
      ```
      Expect, in order: `session.opened`, one `order.dispatched` per round, and
      `session.closed` — with `payment_method = 'cash'` and `closed_reason = 'paid'` on the
      session row, and `total_amount` matching the total from step 3.
   7. **If the Logistic stack is up** (its repo, `docker compose up -d`, after this stack):
      `processed_at` fills in on those rows within a couple of seconds, and the receipt for
      that session appears in Logistic's fiscal journal (mock provider in Development).
      A force-released session must emit `session.closed` **without** producing a receipt.

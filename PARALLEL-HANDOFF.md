# PARALLEL-HANDOFF

Cross-stream notes for the Waiter Mode work. Three streams run in this repo against the same
frozen contract (Appendix B): **W1** backend (`RestaurantApi`), **W2** waiter-mode frontend,
**W3** contracts + realtime + tests + docs.

**Append-only. Write only under your own heading.**

## W1

Branch: `feature/waiter-mode-backend`. Landed in six commits, one per task. The backend
contract is implementable against as-is — every B.4 route, B.6 event and B.2 outbox payload
is live.

### Delivered

| File | Note |
|---|---|
| `backend/RestaurantApi/Models/TableServiceSession.cs` | `ITenantScoped`; exclusivity is a DB partial unique index, not app logic |
| `backend/RestaurantApi/Models/OrderEvent.cs` | outbox row + `OrderEventTypes` literals; **no** query filter (cross-tenant by design) |
| `backend/RestaurantApi/Migrations/20260805163039_AddWaiterModeAndOrderEvents.cs` | the **one** migration; `Down()` drops in FK-aware order |
| `backend/RestaurantApi/Services/OrderEventWriter.cs` | `Enqueue` attaches only — the caller's `SaveChangesAsync` is what makes it atomic; plus `OrderEventPayloads` |
| `backend/RestaurantApi/Services/TableSessionService.cs` | claim/transfer/close/force-release/totals + `session.*` events + `TableSession*` broadcasts |
| `backend/RestaurantApi/Controllers/WaiterModeController.cs` | the twelve B.4 endpoints, Romanian errors |
| `backend/RestaurantApi/DTO/{Requests,Responses}/Waiter*.cs` | waiter-mode records appended to the existing signup-link files |
| `backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs` | the eleven B.5 allow-list pairs |
| `backend/RestaurantApi/Controllers/CustomerController.cs` | B.3-2 QR attach at submit — nothing else changed |
| `backend/RestaurantApi/Services/StockService.cs` | `MenuItemAvailabilityChanged` additionally to `admin` + `WaitStaff` |
| `backend/RestaurantApi.Tests/{TableSession,OrderEvent,WaiterEndpoint}Tests.cs` + `Helpers/WaiterModeTestFixtures.cs` | the frozen edge cases |

Exactly one migration, named `AddWaiterModeAndOrderEvents`. Development auto-migrate applies
it on the next `dotnet run`; the emitted SQL was diffed against B.1 with
`dotnet ef migrations script AddDeviceSessions AddWaiterModeAndOrderEvents`.

### DI additions (`Program.cs`)

```csharp
builder.Services.AddScoped<IOrderEventWriter, OrderEventWriter>();
builder.Services.AddScoped<ITableSessionService, TableSessionService>();
```

Constructor changes, in case anything is ever built by hand rather than through DI:
`StockService` gained `IHubContext<StaffHub>`; `FlashDealService` and `AdminController`
gained `IOrderEventWriter`.

### For W3 — the TS contract

`shared/types/waiter.ts` matches: the C# records in `DTO/Responses/WaiterResponses.cs`
mirror B.7 field-for-field and serialise camelCase via the global `JsonOptions` policy.
One nullability note: `WaiterSessionDto.waiterUserId` is nullable in the DTO *and* the DB
(the FK is `ON DELETE SET NULL`, so deleting a staff account leaves the bill intact with
`waiterName` as the human-readable snapshot) — B.7 already types it `UUID | null`.
`TableSessionOpenedPayload.waiterUserId` stays non-null, which holds in practice: the
claimer is by definition a live user at emission time.

### For W2 — endpoint behaviour worth knowing before wiring views

- **Claim conflict** is `409 { message, session }` where `session` is the *existing*
  session, so "Ocupată — {waiterName}" renders with no second round-trip. (Noted that W2's
  `parseOrThrow` currently collapses this to the message string — the message does name the
  owner, so the UI is correct either way.)
- **Ownership** is `403` with a Romanian `message` when a waiter touches a colleague's order
  or session, and `409` when the order has no session at all. Managers bypass both.
- **`POST /orders/{id}/submit` dispatches immediately** — the response `status` is already
  `submitted`, never `pending_dispatch`. There is no "waiting for flash deal" state to build.
- **Cancelling a draft hard-deletes it**: the response is still `{ id, status: "cancelled" }`,
  but a follow-up `GET` of that order 404s. Drop it from local state rather than re-fetching.
- **`PATCH /orders/{id}/items` is draft-only** (409 otherwise) and full-replace — omit a
  product to remove it, i.e. send the whole cart every time, as the customer app does.
- **`GET /sessions/{id}` includes soft-deleted completed rounds** in `orders`. Deliberate:
  they are on the bill. Do not filter them out.
- **Caps are `MaxOrderItems = 100` distinct product lines**, quantity 1–99, notes ≤200 chars
  — the same reading as W2's client-side `WAITER_CAPS`, so those agree.

### Decisions taken under Appendix B ambiguity

1. **Two extra indexes beyond the frozen B.1 DDL.** EF Core creates a support index for every
   declared FK, so `IX_orders_placed_by_user_id` and `IX_table_service_sessions_waiter_user_id`
   exist alongside the six named ones. They are a mechanical consequence of the `REFERENCES`
   clauses B.1 itself specifies, and useful in their own right. `table_id` needed none — the
   partial unique index already covers it. Everything else (columns, types, defaults, FK
   delete behaviour, index names and filters) matches B.1 byte-for-byte.
2. **`session.closed.orderIds` carries the billable orders** — the same
   `pending_dispatch..completed` set `cumulativeTotal` sums — not every row ever attached, so
   `orderIds` and `totalAmount` describe one consistent receipt. Drafts are detached before
   the list is taken; `cancelled`/`merged` are excluded.
3. **Optional payload fields are emitted as explicit `null`, not omitted**
   (`tableServiceSessionId`, `placedByUserId`, `notes`, `cashAmount`, `cardAmount`). Each
   event type therefore has a stable key set — which is what `OrderEventTests` asserts, and
   what the Logistic consumer can rely on.
4. **Force-release still snapshots `total_amount`** to whatever stood on the table. B.3-8
   only fixes `closed_reason` and the null `payment_method`; recording the standing total
   makes the audit trail useful. Logistic discriminates fiscally on `closedReason`.
5. **`GET /waiter/staff` and transfer-target validation share one query** (active `Waiter` +
   `RestaurantAdmin` members, per B.4 #12). So an `OrgOwner` holding no restaurant-scoped
   membership row is not offered as a transfer target even though they *can* manage the
   restaurant — transferring a table to someone with no floor presence would strand it.
6. **DTO files.** `DTO/Requests/WaiterRequests.cs` and `DTO/Responses/WaiterResponses.cs`
   already existed (waiter *signup* links). The waiter-mode records were appended rather than
   displacing those types, which keeps the frozen file names.
7. **`OrderFinalizationService.RunOnceForTestAsync`** was added so the archive path can be
   driven synchronously in tests instead of waiting out the 45 s grace period, mirroring the
   existing `MenuImportBackgroundService` hook. Production behaviour is unchanged.

### Conflict recorded (rule 10)

Non-negotiable rule 10 forbids state-changing git. The repo owner explicitly instructed,
in-session, to create a branch and commit each step — so this stream ran `git checkout -b`
and six `git commit`s, staging only `backend/**`. Nothing was pushed.

**Working-tree collision between the three streams (needs the repo owner's attention).**
All three streams share one working tree, and the branch that was checked out changed
underneath each of us as the others ran `git checkout -b`. Net result: every stream's
commits are interleaved on **`feature/waiter-mode-contracts-realtime`**, while
`feature/waiter-mode-backend` and `feature/waiter-mode-frontend` were left pointing at the
base commit `8b5503a`. W1 repaired only its own pointer, in an isolated `git worktree` so
the shared checkout was never disturbed: `feature/waiter-mode-backend` now holds W1's six
backend-only commits cherry-picked onto `8b5503a` and nothing else. The interleaved branch
is untouched and still contains everything.

### Suite status at handoff

```
Passed! - Failed: 0, Passed: 234, Skipped: 0, Total: 234, Duration: 2m38s
```

Full backend suite green, including the 38 new Waiter Mode tests and the customer /
device-session suites that are the canary for the B.3-2 QR attach. Build: 0 errors, 1
pre-existing warning (`MenuManagementController.cs:441`), none added.

**Run them with the .NET 8 SDK, not the system one.** The projects target `net8.0` and this
machine's system dotnet only carries the 9.0.15 and 10.0.8 runtimes, which forces
`DOTNET_ROLL_FORWARD` — and that breaks every HTTP-level integration test in the repo,
pre-existing ones included:

```
System.InvalidOperationException : The PipeWriter 'ResponseBodyPipeWriter'
does not implement PipeWriter.UnflushedBytes.
```

`Microsoft.AspNetCore.Mvc.Testing 8.0.8` is a NuGet package, so it is *not* rolled forward
with the shared framework; the rolled-forward `System.Text.Json` then serialises into
TestHost 8's `ResponseBodyPipeWriter`, which predates `UnflushedBytes`. Identical on `Major`
(→9) and `LatestMajor` (→10), and identical on `FloorPlanTests`, which no stream touched.

Fixed by installing the .NET 8 SDK side-by-side (no sudo, nothing in the repo changed):

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
~/.dotnet/dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj   # Docker required
```

Bumping the `Mvc.Testing` package instead would have broken CI, which pins
`dotnet-version: '8.0.x'`. **CI was never affected** — on an 8.0.x runner the package and
runtime already match, so W1's suites run there as written.

## W2

Branch: `feature/waiter-mode-frontend`. Landed in five commits, one per task.
`npm run type-check` is clean; `npm test` is unchanged from the pre-branch baseline
(see "Pre-existing failure" below).

### Delivered

| File | Note |
|---|---|
| `frontend-admin/src/services/waiterApi.ts` | all twelve B.4 endpoints + `getMenu()`; exports `WAITER_CAPS` |
| `frontend-admin/src/components/waiter/WaiterShell.vue` | phone-first frame, 3-tab bottom bar |
| `frontend-admin/src/views/waiter/WaiterFloorView.vue` | floor plan, claim + 409 UX |
| `frontend-admin/src/views/waiter/WaiterTableView.vue` | rounds, transfer, bill/close, force-release |
| `frontend-admin/src/views/waiter/WaiterOrderView.vue` | menu + cart + kitchen notes + submit chain |
| `frontend-admin/src/stores/useWaiterCartStore.ts` | `defineStore('waiterCart', …)`, sessionStorage-persisted |
| `frontend-admin/src/App.vue` | `WAITER_MODE_ROUTES`, WaiterShell branch, mode switch ×2, realtime lifecycle |
| `frontend-admin/src/router/index.ts` | 3 lazy routes, `WAITER_ALLOWED`, waiter fallback → `waiter-floor` |
| `frontend-admin/src/views/ActiveOrdersBoardView.vue` | the one bounded change (item notes + "Ospătar" tag) |

### The api-client export W1/W3 asked for

`services/api-client.ts` now ends with an **additive** `export { fetcher, authedFetcher, scoped }`
(no existing line changed). This resolves the `TODO(W2 landing)` in
`stores/useWaiterRealtimeStore.ts`: its private `fetchFloor()` can be replaced with
`waiterApi.getFloor()`. **Not done here** — that file is W3-owned.

### Needed from W3 at integration

1. **Drop the `.skip`s** in `src/__tests__/waiterApi.spec.ts` (4) and
   `src/__tests__/useWaiterCartStore.spec.ts` (3). Both suites were run locally against
   the landed files via throwaway un-skipped copies: **16/16 and 9/9 green.**
2. **`waiterApi.spec.ts` needs a one-line fix** or it fails once un-skipped. Line 37 sets
   `import.meta.env.VITE_API_URL = baseUrl` in the spec *body*, but the static
   `import { useAuthStore } from '@/stores/useAuthStore'` on line 7 pulls in `api-client.ts`
   first, and that module freezes `BASE_URL` at evaluation time — so requests go to
   `http://localhost:5005` and MSW 'error'-strategy rejects them. Either set `baseUrl`
   to `'http://localhost:5005'` or stub the env before the module graph loads
   (`vi.stubEnv` in a setup file). Verified: with that single change all 16 pass.
3. `useWaiterCartStore` exposes **both** naming sets so the W3 suite and the W2 prompt's
   API are satisfied: `items`/`lines`, `itemCount`/`count`, `setTable`/`open`, plus
   `add(product, qty = 1)`, `setQuantity`, `increment`, `decrement`, `remove`, `setNotes`,
   `clear(tableId?)`. Prefer one set when the docs are written.

### Decisions taken under Appendix B ambiguity

- **"Mesele mele" is not a fourth route.** The bottom tab points at `waiter-floor` with
  `?filtru=ale-mele` and the floor view filters to `store.myTables`. `WAITER_MODE_ROUTES`
  stays exactly the three frozen names.
- **`MaxOrderItems` read as *distinct product lines*** (not summed quantity) on the client.
  If W1's validator counts something else, the client cap is the one to change.
- **No `.text-muted` class exists in frontend-admin** (the prompt's example is from the
  ops-console design system). The kanban note uses a local `.kc-note` in the file's own
  `kc-*` idiom, muted via `var(--ink40)`.
- **`ActiveOrder` / `ActiveOrderItem` gained `source` / `notes` as local type aliases**
  (`BoardOrder` / `BoardOrderItem`) inside `ActiveOrdersBoardView.vue`, because
  `api-client.ts` is additive-exports-only for W2. Fold them into the real interfaces in
  `api-client.ts` and delete the aliases.
- **The waiter top bar carries a theme toggle.** A `Waiter`-role login has no admin console,
  so without it that role could never switch theme. Hidden for branded tenants, exactly
  like the console's.
- **Claim 409 is surfaced via the error message only.** `parseOrThrow` collapses the body
  to `Error(message)`, and the contract guarantees the 409 message names the owner, so the
  `session` object in that body is currently unused by the UI.

### Conflict recorded (rule 10)

Non-negotiable rule 10 forbids state-changing git. The repo owner explicitly instructed,
in-session, to create a branch and commit each step — so this stream ran `git checkout -b`
and five `git commit`s, staging only W2-owned paths. Nothing was pushed. Note that all three
streams share one working tree, so `feature/waiter-mode-frontend` now carries W1's and W3's
commits too.

### Pre-existing failure (not from this stream)

`src/__tests__/brandCache.spec.ts` — 15 failures, `localStorage` undefined under the
Node version in use ("localStorage is not available because --localstorage-file was not
provided"). It fails in isolation, nothing on this branch touches `@shared/branding/cache`
or `useBrandTheme`, and `brandCache.spec.ts` is untouched since `8b5503a`.

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

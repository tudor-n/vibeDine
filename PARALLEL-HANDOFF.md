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

_(waiter-mode frontend: shell, floor plan, ordering, bill.)_

## W3

_(shared/types/waiter.ts, the realtime store, vitest suites, docs, CI review.)_

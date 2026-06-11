# Segment 12 — SignalR hubs & realtime: review findings

Scope review against the [Segment 12 checklist](CODE_REVIEW_ROADMAP.md#segment-12--signalr-hubs--realtime) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). The hubs are three files in [backend/RestaurantApi/Hubs/](backend/RestaurantApi/Hubs/) — [CustomerHub.cs](backend/RestaurantApi/Hubs/CustomerHub.cs), [StaffHub.cs](backend/RestaurantApi/Hubs/StaffHub.cs), [TenantGroups.cs](backend/RestaurantApi/Hubs/TenantGroups.cs) — plus the SignalR/JWT wiring in [Program.cs:49-78, 145-150, 188-189](backend/RestaurantApi/Program.cs#L49-L189), the seven `IHubContext` broadcast sites across [CustomerController](backend/RestaurantApi/Controllers/CustomerController.cs#L65-L73), [AdminController](backend/RestaurantApi/Controllers/AdminController.cs#L388-L407), [FlashDealService](backend/RestaurantApi/Services/FlashDealService.cs#L84-L85), and [OrderFinalizationService](backend/RestaurantApi/Services/OrderFinalizationService.cs#L88-L105), and the customer SPA consumer at [useWebSocket.ts](frontend-customer/src/composables/useWebSocket.ts).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**6 findings, three of them HIGH.** This is the worst segment so far for structural integrity — the multi-tenant refactor in `830f123` ("All the app to be Multi Tenant") parameterised the group names ([TenantGroups.cs:9-15](backend/RestaurantApi/Hubs/TenantGroups.cs#L9-L15)) but did not finish the job: every `restaurantId` that addresses a group still comes **from the client**, with no membership or QR-token check. The 🔥 roadmap item — "Cross-tenant broadcast is structurally impossible" — fails outright; see [F1](#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-).

The customer-facing path is worse. `CustomerHub` carries no `[Authorize]` and no `OnConnectedAsync` ([CustomerHub.cs:5-16](backend/RestaurantApi/Hubs/CustomerHub.cs#L5-L16)). The customer SPA does send a QR token, but at the wrong query-parameter name (`?token=`, [useWebSocket.ts:22](frontend-customer/src/composables/useWebSocket.ts#L22)) — the server's JWT extractor reads `?access_token` ([Program.cs:71](backend/RestaurantApi/Program.cs#L71)) and would reject a QR token even if the names lined up (it's not a JWT). Net effect: the customer WebSocket path has no authentication at all and the QR token is decorative; see [F2](#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-).

Even on the side that does work in principle, the broadcast surface is half-dead. The admin SPA has **no SignalR client at all** (`grep "/hubs/"` / `"HubConnection"` across [frontend-admin/](frontend-admin/) returns zero matches). The four staff-channel events the backend emits — `OrderSubmitted` ([CustomerController.cs:65-73](backend/RestaurantApi/Controllers/CustomerController.cs#L65-L73)), `OrderStatusUpdated` to `"admin"` ([AdminController.cs:399-407](backend/RestaurantApi/Controllers/AdminController.cs#L399-L407)), `OrderFinalized` ([OrderFinalizationService.cs:98-104](backend/RestaurantApi/Services/OrderFinalizationService.cs#L98-L104)), and `FlashDealClaimed` to `"WaitStaff"` ([FlashDealService.cs:84-85](backend/RestaurantApi/Services/FlashDealService.cs#L84-L85)) — broadcast into empty rooms ([F3](#f3--high--admin-spa-has-no-signalr-client-four-staff-broadcasts-have-zero-subscribers)). Segment 10's [F7](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f7--low--flashdealclaimed-broadcasts-to-a-waitstaff-group-with-no-subscribers) flagged the `WaitStaff` instance specifically; this finding is the broader pattern.

The remaining items are operational hygiene: [F4](#f4--medium--addsignalr-uses-default-hub-options-no-message-size-timeout-or-parallelism-caps) — `AddSignalR()` with default options ([Program.cs:145](backend/RestaurantApi/Program.cs#L145)) leaves message-size, client-timeout, keep-alive, and per-client parallelism at the framework defaults (the roadmap calls this a DoS surface); [F5](#f5--low--token-expiry-and-admin-driven-revocation-are-not-handled-mid-connection) — mid-connection JWT expiry and admin-driven QR rotation are silently a no-op (the roadmap's `↳` follow-up); [F6](#f6--low--tenantgroups-has-untyped-string-overloads-that-let-any-caller-fabricate-any-group-name) — `TenantGroups` exposes `string` overloads that accept arbitrary input, removing the type-system guard the helper was supposed to provide.

What does check out, beyond the verdict opener: every `SendAsync` payload is an anonymous DTO, not an EF entity — confirmed at the seven call sites listed above — so the "lazy-load JSON loop" footgun the roadmap warns about is not present. `OnDisconnectedAsync` is not overridden, so SignalR's automatic group cleanup runs unmodified. The three event names the customer SPA listens for (`FlashDealAvailable`, `FlashDealExpired`, `OrderStatusUpdated`) match the server emissions byte-for-byte — checked against [useWebSocket.ts:60, 86, 90](frontend-customer/src/composables/useWebSocket.ts#L60-L90) — so the live customer-side surface is correct *given* the auth and tenancy holes above. The SignalR JWT-from-query-string extractor is properly scoped to `/hubs/*` paths ([Program.cs:73](backend/RestaurantApi/Program.cs#L73)), so JWT bearers don't leak into other access logs.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 On connection, the hub assigns the caller to a group keyed by `RestaurantId` (and **only** that restaurant's group). Cross-tenant broadcast is structurally impossible | ❌ Fail — see [F1](#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-) | Neither hub overrides `OnConnectedAsync`. Group membership comes from client-invoked RPCs `JoinTableGroup(restaurantId, tableId)` ([CustomerHub.cs:7-10](backend/RestaurantApi/Hubs/CustomerHub.cs#L7-L10)) and `JoinRoleGroup(restaurantId, role)` ([StaffHub.cs:9-12](backend/RestaurantApi/Hubs/StaffHub.cs#L9-L12)), both of which trust the client-supplied IDs without any check. |
| CustomerHub accepts QR-token authentication; StaffHub requires JWT auth. Authorization is enforced via `[Authorize]` on the class | ❌ Fail — see [F2](#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) | `StaffHub` has `[Authorize]` ✓ ([StaffHub.cs:6](backend/RestaurantApi/Hubs/StaffHub.cs#L6)). `CustomerHub` has no attribute ([CustomerHub.cs:5](backend/RestaurantApi/Hubs/CustomerHub.cs#L5)), no `OnConnectedAsync`, and the customer SPA's `?token=qrToken` is read into a query parameter the server never inspects (`Query["access_token"]`, [Program.cs:71](backend/RestaurantApi/Program.cs#L71)). |
| No public hub methods that accept a `restaurantId` parameter without re-checking ambient tenant | ❌ Fail — see [F1](#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-) | Both hub methods take `restaurantId` from the client. There is no `Context.User.Claims` lookup, no membership probe, no QR-token resolution. |
| OnDisconnected cleans up group memberships (SignalR does this automatically — confirm no manual override broke it) | ✓ Pass | Neither hub overrides `OnDisconnectedAsync`; default SignalR cleanup runs. |
| Server pushes only DTOs, never EF entities (lazy-load JSON loop) | ✓ Pass | Every `SendAsync` is an anonymous object literal: [CustomerController.cs:67-73](backend/RestaurantApi/Controllers/CustomerController.cs#L67-L73), [AdminController.cs:391-396, 400-405](backend/RestaurantApi/Controllers/AdminController.cs#L391-L405), [FlashDealService.cs:85, 245-255, 282](backend/RestaurantApi/Services/FlashDealService.cs#L85-L282), [OrderFinalizationService.cs:92-95, 100-104](backend/RestaurantApi/Services/OrderFinalizationService.cs#L92-L104). No entity ever escapes through a hub. |
| Connection limits / message-size limits configured — defaults are generous and can be DoS vectors | ❌ Fail — see [F4](#f4--medium--addsignalr-uses-default-hub-options-no-message-size-timeout-or-parallelism-caps) | `builder.Services.AddSignalR().AddJsonProtocol(...)` ([Program.cs:145-150](backend/RestaurantApi/Program.cs#L145-L150)) — JSON casing is set, every other `HubOptions` is at default (32 KB receive cap, 30 s client timeout, 15 s keepalive, parallelism 1). |
| Events emitted match the client's expectations (case-sensitive) | ⚠ Partial — see [F3](#f3--high--admin-spa-has-no-signalr-client-four-staff-broadcasts-have-zero-subscribers) | Customer side ✓ — `FlashDealAvailable`, `FlashDealExpired`, `OrderStatusUpdated` match ([useWebSocket.ts:60, 86, 90](frontend-customer/src/composables/useWebSocket.ts#L60-L90)). Staff side: no admin-side client exists, so the name-match question is moot — there is nothing to match against. |
| Token expiry mid-connection: documented behavior (reconnect required, vs silent drop) | ❌ Fail — see [F5](#f5--low--token-expiry-and-admin-driven-revocation-are-not-handled-mid-connection) | No documentation, no `OnTokenValidated` mid-connection handler, no mechanism to drop connections when an admin revokes a QR token (roadmap `↳`). JWT validation runs once at WS handshake; the connection then survives the token's expiry. |

---

## F1 — HIGH — `Join*Group` RPCs trust the client's `restaurantId`; cross-tenant broadcast is not structurally impossible 🔥

[CustomerHub.cs:7-14](backend/RestaurantApi/Hubs/CustomerHub.cs#L7-L14):

```csharp
public async Task JoinTableGroup(string restaurantId, string tableId)
{
    await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.Table(restaurantId, tableId));
}

public async Task LeaveTableGroup(string restaurantId, string tableId)
{
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantGroups.Table(restaurantId, tableId));
}
```

[StaffHub.cs:9-17](backend/RestaurantApi/Hubs/StaffHub.cs#L9-L17):

```csharp
public async Task JoinRoleGroup(string restaurantId, string role)
{
    await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.Role(restaurantId, role));
}
```

Neither method does any of the following:

- Verify the connected `Context.User` has a `Membership` row for the requested `restaurantId`.
- Resolve the caller's QR token (CustomerHub) and constrain the join to *that* token's restaurant/table.
- Cross-check against the `ICurrentTenant` scoped service used elsewhere in the API ([Tenancy/TenantAccess.cs](backend/RestaurantApi/Tenancy/TenantAccess.cs)).

A client can therefore invoke `JoinTableGroup("<any-restaurant-guid>", "<any-table-guid>")` and receive every `FlashDealAvailable`, `FlashDealExpired`, and `OrderStatusUpdated` event the server broadcasts for that table. The corresponding server-side broadcasts at [FlashDealService.cs:244-255](backend/RestaurantApi/Services/FlashDealService.cs#L244-L255), [FlashDealService.cs:281-282](backend/RestaurantApi/Services/FlashDealService.cs#L281-L282), [AdminController.cs:389-396](backend/RestaurantApi/Controllers/AdminController.cs#L389-L396), and [OrderFinalizationService.cs:90-96](backend/RestaurantApi/Services/OrderFinalizationService.cs#L90-L96) all target `TenantGroups.Table(restaurantId, tableId)` — and the group's membership is whatever any anonymous WebSocket client joined.

The payloads include order IDs, order numbers, statuses, table IDs, deal IDs, product IDs, prices, and discount percentages — i.e., menu-and-order metadata for a competing restaurant. Combined with [F2](#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) on the customer side, no authentication gate stands between an internet caller and that stream.

The pre-refactor code at `830f123^` accepted `tableId` only and used it directly as the group name — the same pattern, just less obviously tenant-shaped. The multi-tenant refactor added the `restaurantId` parameter and the `TenantGroups` namespacing helper, but stopped one step short: the hub still trusts the client for the value it's about to namespace with.

The 🔥 invariant the roadmap names — *"Cross-tenant broadcast is structurally impossible"* — depends on the server being the one that chooses the group key. The fix shape (out of scope here) is to read `restaurantId` from `Context.User.Claims` for `StaffHub` (after checking the membership) and from a QR-token lookup for `CustomerHub`, then assign the group in `OnConnectedAsync` and remove the public `Join*Group` methods entirely.

---

## F2 — HIGH — `CustomerHub` is fully anonymous, and the customer's `?token=` query parameter is never read by the server 🔥

Two independent layers of the customer WebSocket auth are broken:

**Layer 1 — the hub class has no `[Authorize]`.** [CustomerHub.cs:5](backend/RestaurantApi/Hubs/CustomerHub.cs#L5):

```csharp
public class CustomerHub : Hub
```

No attribute, no policy, no equivalent guard. Anyone who can reach `/hubs/customer` can negotiate, connect, and invoke `JoinTableGroup`.

**Layer 2 — even if `[Authorize]` were added, the client and server disagree on the query-parameter name.** [useWebSocket.ts:22](frontend-customer/src/composables/useWebSocket.ts#L22):

```ts
const socketUrl = `${BASE_WS_URL}/hubs/customer?token=${sessionStore.qrToken}`
```

The customer SPA sends `?token=<qrToken>`. [Program.cs:69-77](backend/RestaurantApi/Program.cs#L69-L77):

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            context.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

The server reads `Query["access_token"]`. The customer's `?token=` is never consumed. There is also no separate QR-token authentication handler — `IQrTokenService` / `TenantResolutionMiddleware` ([Tenancy/TenantResolutionMiddleware.cs](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs)) is HTTP-only; the SignalR handshake never invokes it.

Even if the names lined up, the value is wrong by type: `sessionStore.qrToken` is the customer's QR-token (a `Guid`), not a JWT. Feeding it to the JwtBearer handler would fail validation (no signature, wrong shape). The QR-token therefore can't authenticate the customer hub by *any* path that currently exists in the codebase.

The roadmap states the intent flatly: *"CustomerHub accepts QR-token authentication."* That has not been implemented. The customer hub authentication is, at best, decorative — the SPA passes a token that nothing reads, against a hub that nothing authorises.

The roadmap's `↳` follow-up — *"What happens if the QR token is revoked while connected?"* — answers itself once you see this: the token isn't checked even at handshake time, so revocation has zero effect on the live socket.

---

## F3 — HIGH — Admin SPA has no SignalR client; four staff broadcasts have zero subscribers

`grep -r "/hubs/"`, `"HubConnection"`, `"signalr"` (case-insensitive) across [frontend-admin/src/](frontend-admin/src/) returns no matches. The admin frontend does not connect to either hub. `JoinRoleGroup` is therefore never invoked by any production client, and the `r:{restaurantId}:admin` and `r:{restaurantId}:WaitStaff` groups are always empty.

The four staff-channel broadcasts:

| Event | Site | Group |
|---|---|---|
| `OrderSubmitted` | [CustomerController.cs:65-73](backend/RestaurantApi/Controllers/CustomerController.cs#L65-L73) | `Role(restaurantId, "admin")` |
| `OrderStatusUpdated` | [AdminController.cs:399-407](backend/RestaurantApi/Controllers/AdminController.cs#L399-L407) | `Role(restaurantId, "admin")` |
| `OrderFinalized` | [OrderFinalizationService.cs:98-104](backend/RestaurantApi/Services/OrderFinalizationService.cs#L98-L104) | `Role(restaurantId, "admin")` |
| `FlashDealClaimed` | [FlashDealService.cs:84-85](backend/RestaurantApi/Services/FlashDealService.cs#L84-L85) | `Role(restaurantId, "WaitStaff")` |

…all hit zero clients. The Segment 23 roadmap entry calls this out indirectly — *"`ActiveOrdersBoard` polls or subscribes via SignalR — confirm which. (If the explore report says no admin WS, this is polling — flag it.)"* — and the answer is clear: the admin orders board is polling-only, and these emissions are operational no-ops.

This is the same pattern Segment 10 surfaced for `FlashDealClaimed` ([CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md F7](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f7--low--flashdealclaimed-broadcasts-to-a-waitstaff-group-with-no-subscribers)), generalised: the staff broadcast layer is end-to-end disconnected. Two follow-up choices, both out of scope here:

1. Add an admin-side SignalR client (segment 23's natural home) — at which point the existing `JoinRoleGroup(restaurantId, "admin")` parameterisation becomes the surface to harden alongside [F1](#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-).
2. Delete the four `SendAsync` calls (and the `IHubContext<StaffHub>` injections that exist only to feed them) until an admin client lands.

Either way, the current state — events broadcast into the void — wastes a small but persistent amount of CPU per order and creates the false impression that a realtime admin view exists.

The `FlashDealClaimed` group name (`"WaitStaff"`) is also internally inconsistent with the other three (`"admin"`); even when an admin client is built, the wait-staff event will need a second `JoinRoleGroup` call to surface, or its group name needs to change to `"admin"` to match.

---

## F4 — MEDIUM — `AddSignalR()` uses default hub options; no message-size, timeout, or parallelism caps

[Program.cs:145-150](backend/RestaurantApi/Program.cs#L145-L150):

```csharp
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });
```

JSON casing is set. The rest of `HubOptions` is unset:

- `MaximumReceiveMessageSize` is 32 KB by default — generous for our payloads (the largest, `FlashDealAvailable`, is well under 1 KB) but allows a hostile client to repeatedly invoke `JoinTableGroup` with 32 KB strings until the connection is closed. Combined with [F1](#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-), nothing throttles abuse.
- `ClientTimeoutInterval` defaults to 30 s; `KeepAliveInterval` to 15 s. Acceptable, but no explicit decision is recorded.
- `MaximumParallelInvocationsPerClient` defaults to 1 — fine for this hub shape, but again undocumented.
- `EnableDetailedErrors` defaults to `false` in prod (good) and is not overridden in dev, so debugging is harder than it needs to be — minor.

The roadmap calls this a DoS surface and asks for the limits to be configured explicitly. Two-line fix shape:

```csharp
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 8 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
})
.AddJsonProtocol(...);
```

`MaximumReceiveMessageSize` at 8 KB comfortably fits every payload the hubs accept while shutting down the slow-loris-style scenario.

---

## F5 — LOW — Token expiry and admin-driven revocation are not handled mid-connection

JWT validation runs once during the WebSocket negotiate exchange. SignalR's JwtBearer integration does not re-validate per message; once the connection is established, the bearer's `exp` claim is no longer consulted. There is no `OnTokenValidated` resubscription, no periodic re-check, and no server-side mechanism to drop a connection when:

- A staff member's refresh token is revoked via `POST /api/auth/logout` (which clears the refresh token but leaves any open WS connection authenticated until disconnect).
- An admin rotates a `RestaurantTable.QrToken` (which — independently of [F2](#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) — should drop any customer WS bound to the old token).

The Segment 12 roadmap asks for the behaviour to be at least documented:

> Token expiry mid-connection: documented behavior (reconnect required, vs silent drop).

It is not. Nothing in [Hubs/](backend/RestaurantApi/Hubs/), [Program.cs](backend/RestaurantApi/Program.cs), or the segment-11 email subsystem comments addresses it. The de-facto behaviour is "the connection stays open until the client closes it or the OS kills the socket" — which on a 30-day `RefreshTokenDays` window ([appsettings.Development.json:13](backend/RestaurantApi/appsettings.Development.json#L13)) is effectively forever.

Two complementary fixes (out of scope here): (1) wire `IHubContext<...>.Clients.User(userId).SendCoreAsync("connection.close", ...)` from the `Logout` and QR-rotation paths; (2) document the current "reconnect-on-disconnect, never proactively dropped" behaviour so the next person doesn't assume otherwise.

---

## F6 — LOW — `TenantGroups` has untyped `string` overloads that let any caller fabricate any group name

[TenantGroups.cs:7-16](backend/RestaurantApi/Hubs/TenantGroups.cs#L7-L16):

```csharp
public static class TenantGroups
{
    public static string Table(Guid restaurantId, Guid tableId) => $"r:{restaurantId}:t:{tableId}";

    public static string Table(string restaurantId, string tableId) => $"r:{restaurantId}:t:{tableId}";

    public static string Role(Guid restaurantId, string role) => $"r:{restaurantId}:{role}";

    public static string Role(string restaurantId, string role) => $"r:{restaurantId}:{role}";
}
```

The purpose of `TenantGroups` is to centralise the group naming scheme — its file header comment says *"so realtime messages (flash deals, staff alerts) never leak across tenants."* The `(Guid, Guid)` overloads are the safe surface: the type system guarantees the values are well-formed Guids and the server already validated them (e.g., from `order.RestaurantId`).

The `(string, string)` overloads exist only to serve the two hub methods that take strings from the client ([CustomerHub.cs:9, 14](backend/RestaurantApi/Hubs/CustomerHub.cs#L9-L14), [StaffHub.cs:11, 16](backend/RestaurantApi/Hubs/StaffHub.cs#L11-L16)). They accept any string — `"<script>"`, `"\n\n"`, an empty string — and produce a corresponding group name. Combined with [F1](#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-), a client can probe for the existence of any group by joining it (`Groups.AddToGroupAsync` doesn't reject malformed names) and waiting for traffic.

Two related concerns:

- A `Guid.ToString()` default-format on the server (lowercase, hyphenated, no braces) is what the customer SPA happens to send via the JSON-serialized session store. If either side ever normalises differently — uppercased Guids, no hyphens, braces — the group name mismatches silently. Pinning the format with `.ToString("D")` on both sides or accepting `Guid` via SignalR's binder (rather than `string`) removes the fragility.
- The `Role` parameter is just a string. The mismatch in [F3](#f3--high--admin-spa-has-no-signalr-client-four-staff-broadcasts-have-zero-subscribers) (`"WaitStaff"` vs `"admin"`) would have been a compile-time error if `Role` were a `MembershipRole` enum value (the type already exists, [Models/MembershipRole.cs](backend/RestaurantApi/Models/MembershipRole.cs)). The two near-duplicate helpers are doing less work than the project's existing type system would do for free.

---

## What's solid

| Item | Why it's good |
|---|---|
| SignalR JWT extractor is path-scoped to `/hubs/*` ([Program.cs:73](backend/RestaurantApi/Program.cs#L73)) | The query-string `access_token` only acts as a credential for hub negotiations; it never leaks into the controller-side access log for other paths. |
| All seven `SendAsync` payloads are anonymous DTOs, not entities | No EF lazy-load JSON loop is reachable through the hub layer. |
| `OnDisconnectedAsync` is not overridden | SignalR's built-in group cleanup runs unmodified, so disconnects don't leak group memberships. |
| Customer-facing event names match on both ends ([useWebSocket.ts:60, 86, 90](frontend-customer/src/composables/useWebSocket.ts#L60-L90) vs server emissions) | Case-sensitive `FlashDealAvailable` / `FlashDealExpired` / `OrderStatusUpdated` match — the part the customer SPA actually depends on works once it gets past the (broken) auth handshake. |
| `MapHub` paths are namespaced under `/hubs/` ([Program.cs:188-189](backend/RestaurantApi/Program.cs#L188-L189)) | Makes the JWT-extraction path-scope clean, and makes future per-hub rate-limit / CORS / WAF rules easy to express. |

---

## Cross-segment follow-ups raised here

- **[Segment 03](CODE_REVIEW_ROADMAP.md#segment-03--api-startup-pipeline-programcs):** [F4](#f4--medium--addsignalr-uses-default-hub-options-no-message-size-timeout-or-parallelism-caps) belongs in the Program.cs hardening pass — `AddSignalR(options => ...)` with explicit caps. Also: the JWT-extraction query-parameter name mismatch in [F2](#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) is a Program.cs-level decision (the customer hub needs its *own* auth scheme, not JwtBearer).
- **[Segment 04](CODE_REVIEW_ROADMAP.md#segment-04--multi-tenancy-enforcement):** [F1](#f1--high--joingroup-rpcs-trust-the-clients-restaurantid-cross-tenant-broadcast-is-not-structurally-impossible-) is a tenancy-enforcement hole that EF query filters can't catch — `Groups.AddToGroupAsync` doesn't go through DbContext. The tenancy review should include "every cross-tenant authority probe runs through `TenantAccess`" and the hub join paths fail that test today.
- **[Segment 05](CODE_REVIEW_ROADMAP.md#segment-05--authentication--token-lifecycle):** [F5](#f5--low--token-expiry-and-admin-driven-revocation-are-not-handled-mid-connection) — the auth/revocation review should consider whether logout, refresh-rotation, and QR-rotation should each drop live hub connections.
- **[Segment 09](CODE_REVIEW_ROADMAP.md#segment-09--controllers--customer-surface):** the QR-token lifecycle is centred here; the customer-hub auth gap in [F2](#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) is the WS instance of the same trust boundary.
- **[Segment 10](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services):** [F3](#f3--high--admin-spa-has-no-signalr-client-four-staff-broadcasts-have-zero-subscribers) generalises Segment 10's [F7](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f7--low--flashdealclaimed-broadcasts-to-a-waitstaff-group-with-no-subscribers) from one event (`FlashDealClaimed`) to all four staff-channel emissions.
- **[Segment 19](CODE_REVIEW_ROADMAP.md#segment-19--customer-spa--realtime-client):** every customer-side concern in the roadmap (event-name match, reconnect backoff with jitter, state reconciliation on reconnect) lands in [useWebSocket.ts](frontend-customer/src/composables/useWebSocket.ts) — but the fixed 5 s reconnect with no jitter ([line 107-109](frontend-customer/src/composables/useWebSocket.ts#L107-L109)) and the absence of a re-fetch on reconnect are visible from here and reinforce the segment-19 follow-up.
- **[Segment 23](CODE_REVIEW_ROADMAP.md#segment-23--admin-spa--orders-metrics-ai-config):** Segment 23 will need to decide between adding an admin SignalR client (and then designing the role-group join securely) and deleting the staff-channel emissions; today, the admin orders board polling is the only thing keeping the UI live.

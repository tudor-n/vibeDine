# Segment 19 — Customer SPA: realtime client — review findings

Scope review against the [Segment 19 checklist](CODE_REVIEW_ROADMAP.md#segment-19--customer-spa--realtime-client) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). One coordinator file: [composables/useWebSocket.ts](frontend-customer/src/composables/useWebSocket.ts). Direct consumers: [stores/useFlashDealStore.ts](frontend-customer/src/stores/useFlashDealStore.ts), [stores/useOrderStatusStore.ts](frontend-customer/src/stores/useOrderStatusStore.ts). Indirect consumers (where the events actually surface): [components/screens/FlashDealView.vue](frontend-customer/src/components/screens/FlashDealView.vue), [components/screens/OrderSentView.vue](frontend-customer/src/components/screens/OrderSentView.vue), [components/FlashDealBanner.vue](frontend-customer/src/components/FlashDealBanner.vue). Cross-references: the hub the client connects to ([backend/RestaurantApi/Hubs/CustomerHub.cs](backend/RestaurantApi/Hubs/CustomerHub.cs)) and the JWT extraction path ([Program.cs:67-77](backend/RestaurantApi/Program.cs#L67-L77)).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**7 findings, three HIGH.** The event-name parity item — the only 🔥 item the roadmap names for this segment — passes cleanly: `FlashDealAvailable`, `FlashDealExpired`, and `OrderStatusUpdated` ([useWebSocket.ts:60, 86, 90](frontend-customer/src/composables/useWebSocket.ts#L60-L90)) match the server emissions byte-for-byte ([Segment 12 checklist coverage](CODE_REVIEW_SEGMENT_12_SIGNALR.md#checklist-coverage)). The SignalR JSON-protocol handshake at [line 31](frontend-customer/src/composables/useWebSocket.ts#L31) and the `\x1e` record-separator framing at [line 36, 47, 50](frontend-customer/src/composables/useWebSocket.ts#L36-L50) are the correct primitives — the roadmap explicitly calls them out as protocol-level concerns that should be handled by SignalR, and the code reaches for them directly rather than reimplementing on top.

Beyond that, the segment's three roadmap-named risks all fail. The findings cluster around three themes:

1. **The WS doesn't authenticate.** [F1](#f1--high--the-qr-token-query-parameter-is-decorative-the-server-never-reads-it-customerhub-is-anonymous-) — `useWebSocket.ts:22` builds the URL as `${BASE_WS_URL}/hubs/customer?token=${sessionStore.qrToken}` while [Program.cs:71](backend/RestaurantApi/Program.cs#L71) reads `Query["access_token"]`, and `CustomerHub` has no `[Authorize]` ([CustomerHub.cs:5](backend/RestaurantApi/Hubs/CustomerHub.cs#L5)). The QR token rides along the URL but reaches nothing; the connection is anonymous from the server's perspective. This is the customer-side view of [Segment 12 F2](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) — surfaced from the *client* file here because the misnamed parameter lives in this composable.
2. **Reconnect is a thundering-herd / infinite-retry surface.** [F2](#f2--high--reconnect-is-a-fixed-5s-no-jitter-no-exponential-no-maxattempt-cap) — `setTimeout(connect, 5000)` ([useWebSocket.ts:107-109](frontend-customer/src/composables/useWebSocket.ts#L107-L109)) has no jitter, no exponential growth, no max-attempt cap. After a backend outage, every connected client reconnects at *exactly* 5 seconds, in lockstep. If `sessionStore.tableId` survives the outage (it lives in `sessionStorage` — which it does), the retry loop runs forever. [F3](#f3--high--no-state-reconciliation-on-reconnect-events-missed-during-the-disconnect-window-are-lost) — on reconnect, the client re-issues `JoinTableGroup` ([line 44-54](frontend-customer/src/composables/useWebSocket.ts#L44-L54)) but does *not* re-fetch the cart, the active order's status, or the active flash deal. Events emitted during the disconnect window — `OrderStatusUpdated → "ready"`, for instance — are gone forever, and the customer never sees that their food is ready.
3. **Connection lifetime doesn't match the SPA lifetime.** [F4](#f4--medium--no-beforeunload-handler-onunmounted-never-fires-on-tab-close-because-orderingview-never-unmounts) — `useWebSocket` only calls `disconnect()` from `onUnmounted` ([line 134-136](frontend-customer/src/composables/useWebSocket.ts#L134-L136)). `OrderingView` is the only consumer ([OrderingView.vue:27](frontend-customer/src/view/OrderingView.vue#L27)) and the only mounted route, so `onUnmounted` fires when the SPA is destroyed — i.e., never. Tab close leaves the WS to die at the OS level; from the backend's perspective the connection is orphaned until `ClientTimeoutInterval` (defaults to 30s, [Segment 12 F4](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f4--medium--addsignalr-uses-default-hub-options-no-message-size-timeout-or-parallelism-caps)) elapses. [F5](#f5--medium--reconnect-settimeout-is-never-cleared-orphan-reconnects-can-fire-after-disconnectunmount) — the reconnect `setTimeout` handle is never captured and never cleared; a pending reconnect can fire after `disconnect()` is called, defeating the cleanup intent.

The remaining items: [F6](#f6--low--ws-errors-are-consoleerrored-only-the-customer-never-sees-realtime-is-broken) — every WS error path (`onerror`, parse-failure, close) ends in `console.error`; the customer has no UI signal that flash deals / order-status updates have stopped flowing (cross-ref [Segment 18 F5](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern)). [F7](#f7--low--handshake-ack-detection-is-an-exactstring-match-msg---) — the ack-recognition path at `if (msg === '{}')` ([useWebSocket.ts:44](frontend-customer/src/composables/useWebSocket.ts#L44)) is fragile if SignalR ever changes the handshake to include whitespace or fields.

What does check out, beyond the verdict opener: connecting is gated on `sessionStore.tableId` via `watch` with `immediate: true` ([line 124-132](frontend-customer/src/composables/useWebSocket.ts#L124-L132)) — the composable doesn't connect until the QR session has resolved, which avoids the timing race the comment at [line 123](frontend-customer/src/composables/useWebSocket.ts#L123) memorialises (*"Key fix: watch tableId instead of connecting blindly in onMounted"*). The `JoinTableGroup` is sent on the handshake-ack tick rather than on a `type=6` ping ([line 41-54](frontend-customer/src/composables/useWebSocket.ts#L41-L54)) — the comment at [line 41-43](frontend-customer/src/composables/useWebSocket.ts#L41-L43) records that the old approach left a 15s window where flash-deal events were missed; the fix is the right one. Logging is sanitary — `console.log` carries no PII ("Conexiune WebSocket stabilită!" / "Conexiune WebSocket închisă.") and the error paths only log the error object, not the raw `event.data`. The `socket.onmessage` parse loop is defensive: the outer `try/catch` ([line 35-100](frontend-customer/src/composables/useWebSocket.ts#L35-L100)) catches JSON parse failures, the per-message `if (!msg) continue` skips empty frames, and `socket?.send` is null-guarded on the handshake send. The `FlashDealAvailable` payload mapping at [line 60-83](frontend-customer/src/composables/useWebSocket.ts#L60-L83) doesn't trust the server's shape blindly — it default-coalesces `discountPercent || 20`, hard-codes `countdownSeconds: 120` rather than trusting wire input, and uses `new Date().toISOString()` for `createdAt` rather than echoing whatever the server might send.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 Event names match backend hub names exactly (`FlashDealAvailable`, `FlashDealExpired`, `OrderStatusUpdated`) — case-sensitive | ✓ Pass | `useWebSocket.ts:60, 86, 90` — all three names match the server emissions in [FlashDealService.cs:245, 282](backend/RestaurantApi/Services/FlashDealService.cs#L245-L282), [AdminController.cs:391](backend/RestaurantApi/Controllers/AdminController.cs#L391), [OrderFinalizationService.cs:92](backend/RestaurantApi/Services/OrderFinalizationService.cs#L92). |
| Reconnect backoff (5s per explore report) has jitter and a max-attempt cap or exponential growth | ❌ Fail — see [F2](#f2--high--reconnect-is-a-fixed-5s-no-jitter-no-exponential-no-maxattempt-cap) | `setTimeout(() => { if (sessionStore.tableId) connect() }, 5000)` ([line 107-109](frontend-customer/src/composables/useWebSocket.ts#L107-L109)). No jitter, no exponential, no cap. |
| On reconnect, the client re-fetches state (orders, deals) so events missed during disconnect are reconciled | ❌ Fail — see [F3](#f3--high--no-state-reconciliation-on-reconnect-events-missed-during-the-disconnect-window-are-lost) | The reconnect path re-issues `JoinTableGroup` ([line 44-54](frontend-customer/src/composables/useWebSocket.ts#L44-L54)) and that's it. No `apiClient.getMenu()` refresh, no `getRecommendations()`, no order-status re-fetch (there's no endpoint to call — see [F3](#f3--high--no-state-reconciliation-on-reconnect-events-missed-during-the-disconnect-window-are-lost) for the upstream gap). |
| Connection closes cleanly on page unload (`beforeunload`) — orphaned connections eat backend resources | ❌ Fail — see [F4](#f4--medium--no-beforeunload-handler-onunmounted-never-fires-on-tab-close-because-orderingview-never-unmounts) | Grep `beforeunload\|window\.addEventListener` across [src/](frontend-customer/src/) returns no matches. `onUnmounted` ([line 134-136](frontend-customer/src/composables/useWebSocket.ts#L134-L136)) is the only cleanup path. |
| Heartbeat / keepalive handled by SignalR protocol (binary `\x1e` frame separator per report) — confirm not reimplemented manually | ✓ Pass | The `\x1e` separator is used as a *parsing* primitive ([line 36, 47, 50](frontend-customer/src/composables/useWebSocket.ts#L36-L50)). Heartbeat is not reimplemented — the client never sends `type: 6` pings; SignalR's protocol-level keepalive runs on the server's `KeepAliveInterval` and is honoured implicitly. |
| Errors from the WS layer don't crash the component tree — they bubble to a global handler | ⚠ Partial — see [F6](#f6--low--ws-errors-are-consoleerrored-only-the-customer-never-sees-realtime-is-broken) | No crash (the `onmessage` handler is wrapped in `try/catch`). No global handler either — `socket.onerror` ([line 112-114](frontend-customer/src/composables/useWebSocket.ts#L112-L114)) only `console.error`s. |
| No PII logged from received messages | ✓ Pass | Five `console.*` calls. None log `event.data` raw or the parsed payload — only error objects and human-readable status strings. |
| How does the customer's WS authenticate? (roadmap `↳`) | ❌ Fail — see [F1](#f1--high--the-qr-token-query-parameter-is-decorative-the-server-never-reads-it-customerhub-is-anonymous-) | The client sends `?token=${qrToken}`; the server reads `?access_token` and `CustomerHub` has no `[Authorize]`. The connection is anonymous. |
| What happens if the token is revoked while connected? (roadmap `↳`) | ❌ Fail | The token is never validated, so revocation has no effect. Even if it were, no server-side mechanism drops connections on QR rotation ([Segment 12 F5](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f5--low--token-expiry-and-admin-driven-revocation-are-not-handled-mid-connection)). |

---

## F1 — HIGH — The QR token query parameter is decorative; the server never reads it, `CustomerHub` is anonymous 🔥

[useWebSocket.ts:22](frontend-customer/src/composables/useWebSocket.ts#L22):

```ts
const socketUrl = `${BASE_WS_URL}/hubs/customer?token=${sessionStore.qrToken}`
```

[Program.cs:67-77](backend/RestaurantApi/Program.cs#L67-L77) — the server-side JWT extraction for hub negotiations:

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

[CustomerHub.cs:5-16](backend/RestaurantApi/Hubs/CustomerHub.cs#L5-L16) — no `[Authorize]`, no `OnConnectedAsync`. Joins are unauthenticated and trust the client's `restaurantId` / `tableId`.

The chain:

- The client labels the query parameter `?token=`. The server reads `?access_token=`. Even if it didn't, a QR token isn't a JWT and would fail the `JwtBearer` validator with `SecurityTokenMalformedException`.
- `CustomerHub` has no `[Authorize]` attribute, so the entire JWT pipeline is bypassed regardless.
- The QR token therefore plays no security role. It's a URL parameter that nothing consumes.

This is the customer-end of [Segment 12 F2](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) and the answer to two of the roadmap's `↳` follow-ups (*"How does the customer's WS authenticate?"* and *"What happens if the token is revoked while connected?"*): it doesn't, and nothing — the revocation has no live-connection consequence.

A surface effect visible from this file: the `?token=` parameter accumulates in access logs and proxy logs as a URL query, where a JWT-style token would be sensitive PII. A QR-token isn't a credential here (because nothing authenticates against it), but if [Segment 12 F2](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-) is fixed by adding a custom QR-auth handler, the token *becomes* a credential — and at that point putting it in a query string (rather than a header or a cookie) is a logging-exposure concern. The fix should land both ends together.

The two-line client fix is to either align the parameter name (`?access_token=`) and a server-side QR-auth handler, or remove the parameter entirely and document the connection as anonymous. The current state is the worst of both: it *looks* like there's auth, there isn't.

---

## F2 — HIGH — Reconnect is a fixed 5s, no jitter, no exponential, no max-attempt cap

[useWebSocket.ts:103-110](frontend-customer/src/composables/useWebSocket.ts#L103-L110):

```ts
socket.onclose = () => {
  console.log('Conexiune WebSocket închisă.')
  isConnected.value = false
  socket = null
  setTimeout(() => {
    if (sessionStore.tableId) connect()
  }, 5000)
}
```

Three roadmap items, three failures:

- **No jitter.** Every connected client times its reconnect attempt against the same 5s interval. After a backend outage (e.g., a 30s deploy window) the entire room of diners hits the server in a single tick, multiplied by the number of clients per restaurant. The default `MaximumParallelInvocationsPerClient` and connection-rate limits ([Segment 12 F4](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f4--medium--addsignalr-uses-default-hub-options-no-message-size-timeout-or-parallelism-caps)) are themselves at framework defaults, so there's no upstream cap either.
- **No exponential growth.** A backend that needs longer than 5s to recover (slow boot, DB migration, blue/green switch) sees a fresh wave of reconnects every 5s — even after the first few have failed.
- **No max-attempt cap.** As long as `sessionStore.tableId` is truthy (and it lives in `sessionStorage` which survives refresh), the retry runs forever. A user who scanned a QR for a table that an admin then *deletes* keeps reconnecting forever. The server returns a 401 (or whatever the QR-auth path returns once it exists), the client treats it as a transient disconnect, retries at 5s, and the cycle repeats.

The right shape is industry-standard:

```ts
function backoff(attempt: number) {
  const base = Math.min(30_000, 1000 * 2 ** attempt)  // exponential up to 30s
  const jitter = Math.random() * 1000
  return base + jitter
}
```

Plus a `maxAttempts` constant (typically 10–20 for a customer-facing app) that bails to the `session-error` overlay once exhausted. SignalR's official JS client (`@microsoft/signalr`) ships with exactly this pattern by default — the custom WebSocket implementation here re-implements the SignalR protocol from scratch, gaining direct control of the `\x1e` framing but losing every reconnect default the official client provides.

---

## F3 — HIGH — No state reconciliation on reconnect; events missed during the disconnect window are lost

The reconnect path at [useWebSocket.ts:107-109](frontend-customer/src/composables/useWebSocket.ts#L107-L109) calls `connect()`, which re-runs the handshake and re-issues `JoinTableGroup` on the ack:

```ts
if (msg === '{}') {
  if (sessionStore.tableId && sessionStore.restaurantId) {
    socket?.send(
      JSON.stringify({
        type: 1,
        target: 'JoinTableGroup',
        arguments: [sessionStore.restaurantId, sessionStore.tableId]
      }) + '\x1e'
    )
  }
  continue
}
```

That's the entire reconnect logic. The Pinia stores ([useFlashDealStore](frontend-customer/src/stores/useFlashDealStore.ts), [useOrderStatusStore](frontend-customer/src/stores/useOrderStatusStore.ts)) are not consulted; no REST call is made to compare local state against authoritative server state.

Concrete loss scenarios:

- **Stuck `OrderStatusUpdated` timeline.** The customer's order is `submitted`. Network drops for 90 seconds. During that window the kitchen marks it `preparing` and then `ready` (two emissions). Connection comes back. The server has already broadcast both transitions; the client has missed them. `OrderSentView` ([screens/OrderSentView.vue:50-84](frontend-customer/src/components/screens/OrderSentView.vue#L50-L84)) reads `orderStatusStore.status` and timestamps each transition on watch — but the status is still `null` because nothing told it otherwise. The customer sits at the "Order sent" screen with no progress indicator while their food is sitting on the pass.
- **Missed `FlashDealAvailable`.** The flash deal is server-issued with a 2-minute expiry ([FlashDealService.cs:262-264](backend/RestaurantApi/Services/FlashDealService.cs#L262-L264)). If the WS is disconnected when the deal is broadcast and the reconnect takes more than a few seconds, the deal is gone. The fire-and-forget timer at [FlashDealService.cs:262-283](backend/RestaurantApi/Services/FlashDealService.cs#L262-L283) ([Segment 10 F1](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f1--high--flash-deal-expiry-runs-in-a-fire-and-forget-taskrun-and-is-lost-on-process-restart-)) does not re-broadcast; the deal exists in the DB as `Status="pending"` but the customer never sees it.
- **`FlashDealExpired` missed.** Conversely, if the deal expires while the client is disconnected, the local `flashDealStore.activeDeal` is still the old one. On reconnect the UI shows the deal still active. The `FlashDealView` tick at [screens/FlashDealView.vue:24-35](frontend-customer/src/components/screens/FlashDealView.vue#L24-L35) catches this client-side (it computes `remaining` from `expiresAt` and self-clears when `<=0`) — so for `FlashDealExpired` specifically the client-side timer rescues the situation. But that's an accidental save, not designed-in.

The roadmap names the fix shape: *"On reconnect, the client re-fetches state (orders, deals) so events missed during disconnect are reconciled."* The C# side has no `GET /api/restaurants/{rid}/orders/{orderId}/status` endpoint to support this today (grep `orders/.*status` across [Controllers/](backend/RestaurantApi/Controllers/) returns nothing) — so the fix needs both ends:

1. Backend: add `GET /api/restaurants/{rid}/orders/{orderId}` returning `{ status, items, total }` (or extend the existing recommendation endpoint at [CustomerController.cs:296](backend/RestaurantApi/Controllers/CustomerController.cs#L296)).
2. Frontend: on `socket.onopen`, after the handshake ack, call the new endpoint and hydrate `orderStatusStore` + `flashDealStore`.

Out of scope for this review; documented as a coupled Segment 9 ↔ Segment 19 follow-up.

---

## F4 — MEDIUM — No `beforeunload` handler; `onUnmounted` never fires on tab close because `OrderingView` never unmounts

[useWebSocket.ts:134-136](frontend-customer/src/composables/useWebSocket.ts#L134-L136):

```ts
onUnmounted(() => {
  disconnect()
})
```

`useWebSocket()` is called exactly once at [OrderingView.vue:27](frontend-customer/src/view/OrderingView.vue#L27). `OrderingView` is the single component bound to the single route `/:qrToken?` ([router/index.ts:7-12](frontend-customer/src/router/index.ts#L7-L12)). In the SPA's lifetime, `OrderingView` mounts at boot and unmounts at SPA destruction — which is what `window.unload` triggers, *not* `beforeunload`. By that point the browser is already tearing the document down; the `socket.close()` call in `disconnect()` is best-effort.

What actually happens on tab close:

- The TCP socket is closed by the OS as the process winds down.
- The server doesn't receive a clean WebSocket close frame; it sees the TCP RST.
- SignalR's server side waits `ClientTimeoutInterval` (default 30s) before reaping the connection from its tracking ([Segment 12 F4](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f4--medium--addsignalr-uses-default-hub-options-no-message-size-timeout-or-parallelism-caps)).
- During those 30s, the broadcast layer still considers the (dead) client part of the table group. `IHubContext<CustomerHub>.Clients.Group(...).SendAsync(...)` calls land on the dead socket and silently drop.

Per-client this is invisible. At scale (~100 simultaneous connections, churn rate of one-per-minute), the server has ~30 phantom connections at any moment. That's well under SignalR's defaults, so it's not a current outage shape — the right fix is hygiene, not load.

Two complementary fixes (out of scope):

```ts
// Inside useWebSocket(), once
window.addEventListener('beforeunload', disconnect)
// And mirror the cleanup
onUnmounted(() => {
  window.removeEventListener('beforeunload', disconnect)
  disconnect()
})
```

`beforeunload` fires *before* the document is torn down, while the JS event loop is still alive — the `socket.close()` actually flushes a WebSocket close frame to the server. The `onUnmounted` cleanup matters less for the current single-route SPA but is correct shape for the future.

---

## F5 — MEDIUM — Reconnect `setTimeout` is never cleared; orphan reconnects can fire after disconnect/unmount

[useWebSocket.ts:103-121](frontend-customer/src/composables/useWebSocket.ts#L103-L121):

```ts
socket.onclose = () => {
  console.log('Conexiune WebSocket închisă.')
  isConnected.value = false
  socket = null
  setTimeout(() => {
    if (sessionStore.tableId) connect()
  }, 5000)
}

socket.onerror = (error) => {
  console.error('Eroare WebSocket:', error)
}

function disconnect() {
  if (socket) {
    socket.close()
  }
}
```

The `setTimeout` handle is never captured. Concrete sequence:

1. Server closes the connection (deploy, transient network).
2. `socket.onclose` fires, sets `socket = null`, schedules `connect()` for +5s.
3. User navigates away (currently impossible due to single route, but the SPA could grow), or `disconnect()` is called for any other reason.
4. `disconnect()` does `if (socket) socket.close()` — but `socket` is already `null`, so this is a no-op.
5. 5 seconds after step 2, the timer fires. `if (sessionStore.tableId) connect()` — `tableId` is still in `sessionStorage`, so a fresh connection is opened. After the cleanup intent.

This is harmless today because `disconnect()` only runs from `onUnmounted` and `onUnmounted` only fires at SPA destruction (where there's no longer any environment for the timer callback to do harm). The bug becomes visible when:

- The SPA grows to multi-route and `OrderingView` actually unmounts.
- A future caller introduces a programmatic `disconnect()` (e.g., on QR-token rotation, on session-clear, on a `clearSession()` button).
- [F4](#f4--medium--no-beforeunload-handler-onunmounted-never-fires-on-tab-close-because-orderingview-never-unmounts) adds the `beforeunload` cleanup hook.

Fix shape:

```ts
let reconnectTimer: ReturnType<typeof setTimeout> | null = null

socket.onclose = () => {
  // ...
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null
    if (sessionStore.tableId) connect()
  }, backoff(attemptCount++))  // also fixes F2
}

function disconnect() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer)
    reconnectTimer = null
  }
  if (socket) {
    socket.close()
    socket = null
  }
}
```

LOW-MEDIUM today; will be the cause of "why am I getting a phantom connection?" bug reports once the SPA shape changes.

---

## F6 — LOW — WS errors are `console.error`'ed only; the customer never sees realtime is broken

[useWebSocket.ts:97-115](frontend-customer/src/composables/useWebSocket.ts#L97-L115):

```ts
} catch (error) {
  console.error('Eroare la parsarea mesajului WebSocket SignalR:', error)
}

socket.onclose = () => {
  console.log('Conexiune WebSocket închisă.')
  isConnected.value = false
  // ...
}

socket.onerror = (error) => {
  console.error('Eroare WebSocket:', error)
}
```

Five console call sites in this file alone ([Segment 18 F3](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f3--medium--consolelog-not-stripped-in-vite-build-no-no-console-lint-rule) names them) and zero UI surfaces. `isConnected` is exported from the composable ([line 138](frontend-customer/src/composables/useWebSocket.ts#L138)) — but no template reads it. A diner sitting at a table whose backend dropped connection sees:

- A normal-looking menu (because the menu loads via REST).
- A normal-looking cart (likewise).
- No realtime indicator that flash deals / order updates won't arrive.
- No banner / toast when the disconnect happens.

The order-status timeline at [OrderSentView.vue:60-84](frontend-customer/src/components/screens/OrderSentView.vue#L60-L84) reads `orderStatusStore.status` and shows `null` as a frozen "submitted" state. The diner's UX is "their app forgot about their order" with no signal that the WS is the culprit.

The fix is one binding: the composable already exports `isConnected` as a `Ref<boolean>`; rendering a small indicator (a dot, a banner) in `OrderingView` is a few lines. The deeper fix — a toast system — is [Segment 18 F5](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern); both fixes share infrastructure.

LOW because the WS is a "nice to have" surface (the customer's order works without it via REST), but the missing signal compounds with [F3](#f3--high--no-state-reconciliation-on-reconnect-events-missed-during-the-disconnect-window-are-lost) — once realtime breaks silently *and* state isn't reconciled on reconnect, the realtime layer's value is gone.

---

## F7 — LOW — Handshake ack detection is an exact-string match `msg === '{}'`

[useWebSocket.ts:41-55](frontend-customer/src/composables/useWebSocket.ts#L41-L55):

```ts
// SignalR handshake ack is an empty object `{}`. Join the table group
// immediately on the ack — waiting for a type=6 ping created a ~15s
// window where flash-deal events could be missed.
if (msg === '{}') {
  if (sessionStore.tableId && sessionStore.restaurantId) {
    socket?.send(
      JSON.stringify({
        type: 1,
        target: 'JoinTableGroup',
        arguments: [sessionStore.restaurantId, sessionStore.tableId]
      }) + '\x1e'
    )
  }
  continue
}
```

The SignalR JSON protocol's handshake acknowledgement is documented as either `{}` (success) or `{"error": "..."}` (failure). The comment at [line 41-43](frontend-customer/src/composables/useWebSocket.ts#L41-L43) is correct that the empty-object form is the success ack. But the test is *exact string equality*:

- A future SignalR protocol revision that pretty-prints (`'{ }'` or `'{\n}'`) wouldn't match.
- A failure ack (`'{"error":"Unsupported version"}'`) would not match, would be parsed as a regular message at [line 57](frontend-customer/src/composables/useWebSocket.ts#L57), `data.type === undefined`, fall through to the bottom of the message loop without sending `JoinTableGroup`. The client connects, never joins a group, and never receives an event — invisible to the user (no error path).
- A pre-`{}` text frame from a load balancer (CloudFlare-style "X-Connection" pings, some reverse-proxy heartbeats) would also miss.

The right shape is JSON-aware:

```ts
let payload: any
try { payload = JSON.parse(msg) } catch { continue }
if (payload && Object.keys(payload).length === 0) {
  // success ack — join the group
} else if (payload?.error) {
  // failure ack — surface to user
}
```

LOW because the current SignalR JSON protocol does emit literal `'{}'` and this code matches it. The cost is a fragility that a SignalR version bump or a deployment topology change will surface.

---

## What's solid

| Item | Why it's good |
|---|---|
| Event-name parity ([useWebSocket.ts:60, 86, 90](frontend-customer/src/composables/useWebSocket.ts#L60-L90)) | The 🔥 roadmap item passes outright. Three case-sensitive names matching the server emissions byte-for-byte. |
| Connection gated on `sessionStore.tableId` ([line 124-132](frontend-customer/src/composables/useWebSocket.ts#L124-L132)) | The `watch` with `immediate: true` correctly avoids connecting before the QR session has resolved. The comment at [line 123](frontend-customer/src/composables/useWebSocket.ts#L123) documents the prior fix; that decision still holds. |
| `JoinTableGroup` on handshake ack ([line 41-54](frontend-customer/src/composables/useWebSocket.ts#L41-L54)) | The comment explains the choice over waiting for `type=6` ping — the right tradeoff (sub-second join, no missed flash deals). |
| `\x1e` framing handled directly ([line 36, 47, 50](frontend-customer/src/composables/useWebSocket.ts#L36-L50)) | The SignalR record-separator framing is the right primitive; the roadmap names it as a checklist item. |
| Sanitary logging | Five `console.*` calls; none log raw `event.data` or parsed payload contents. Only error objects and human-readable status strings. |
| Defensive payload mapping in `FlashDealAvailable` ([line 60-83](frontend-customer/src/composables/useWebSocket.ts#L60-L83)) | Default-coalesces `discountPercent || 20`, hard-codes `countdownSeconds: 120`, uses `new Date().toISOString()` for `createdAt`. The client doesn't trust wire input blindly. |
| `onmessage` handler is wrapped in `try/catch` ([line 35-100](frontend-customer/src/composables/useWebSocket.ts#L35-L100)) | A malformed frame doesn't crash the component tree. Errors land in `console.error` — surfacing is the issue ([F6](#f6--low--ws-errors-are-consoleerrored-only-the-customer-never-sees-realtime-is-broken)), not propagation. |

---

## Cross-segment follow-ups raised here

- **[Segment 09](CODE_REVIEW_ROADMAP.md#segment-09--controllers--customer-surface):** [F3](#f3--high--no-state-reconciliation-on-reconnect-events-missed-during-the-disconnect-window-are-lost) — a `GET /api/restaurants/{rid}/orders/{orderId}` endpoint returning `{ status, items, total, activeFlashDeal? }` is the backend prerequisite for reconnect state reconciliation. Lands in segment 09's territory; the consumer is segment 19.
- **[Segment 12](CODE_REVIEW_ROADMAP.md#segment-12--signalr-hubs--realtime):** [F1](#f1--high--the-qr-token-query-parameter-is-decorative-the-server-never-reads-it-customerhub-is-anonymous-) is the customer-end of [Segment 12 F2](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f2--high--customerhub-is-fully-anonymous-and-the-customers-token-query-parameter-is-never-read-by-the-server-). The fix needs both: a server-side QR-auth handler and a client-side parameter-name correction.
- **[Segment 18](CODE_REVIEW_ROADMAP.md#segment-18--customer-spa--shell--state):** [F6](#f6--low--ws-errors-are-consoleerrored-only-the-customer-never-sees-realtime-is-broken) extends [Segment 18 F5](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern)'s no-toast-system finding to the WS layer specifically. The five `console.*` call sites in this file are also part of [Segment 18 F3](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f3--medium--consolelog-not-stripped-in-vite-build-no-no-console-lint-rule)'s prod-strip concern — the right fix order is "add toast surface first, then strip."
- **[Segment 10](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services):** [F3](#f3--high--no-state-reconciliation-on-reconnect-events-missed-during-the-disconnect-window-are-lost) intersects [Segment 10 F1](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f1--high--flash-deal-expiry-runs-in-a-fire-and-forget-taskrun-and-is-lost-on-process-restart-) — the fire-and-forget `Task.Run` for flash-deal expiry doesn't re-broadcast either, so even if the client *did* reconcile on reconnect, the expiry message it missed is gone. Both ends need to land for the realtime layer to be self-healing.
- **[Segment 22](CODE_REVIEW_ROADMAP.md#segment-22--admin-spa--menu-management--import) / [Segment 23](CODE_REVIEW_ROADMAP.md#segment-23--admin-spa--orders-metrics-ai-config):** the admin SPA has no SignalR client at all per [Segment 12 F3](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f3--high--admin-spa-has-no-signalr-client-four-staff-broadcasts-have-zero-subscribers). When that's built, the reconnect-and-reconcile pattern raised here should land alongside it from the start, not get retrofitted later.

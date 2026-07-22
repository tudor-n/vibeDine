# Device-Bound Sessions, Link Expiry & Cumulative Order Display — Design

**Date:** 2026-07-22 · **Branch:** `feature/device-sessions`

## Problem

Today every QR scan effectively starts over: identity is a per-table `guestId` whose
`TableGuest` row is deleted the moment an order is submitted, flash deals live only in
client memory, and each ordering round is a separate `Order` whose total replaces the
previous one on screen. A diner who re-scans (or reloads) after ordering loses their
order history and any pending flash deal. There is also no limit on how long an open
tab keeps working after the diner leaves.

## Goals

1. **Device-bound session**: re-scanning the same table QR from the same device
   restores the diner's visit — submitted orders, running total, pending flash deal.
2. **Link expiry**: an open tab stops working after a configurable time
   (30 s in Development for testing, 15 min prod default) and demands a re-scan.
   Re-scanning restores the *same* session (device-bound).
3. **Session wipe**: the device session is deleted only after a longer configurable
   idle time (3 h default, matching the existing stale-guest window) — when we're
   confident the diner has left.
4. **Cumulative order display**: after submitting, ordering more just grows one
   running total; to the diner it reads as a single order. Kitchen flow is
   unchanged — rounds remain separate tickets internally.

## Non-goals

- No change to party/merge mechanics, kitchen kanban, stock, or payment.
- No dynamic QR tokens. The printed QR URL is static, so a browser reload is
  technically indistinguishable from a re-scan; link expiry is a UX gate
  (blocking overlay + dead timers/sockets), not a security boundary.

## Design

### Backend

**New entity `DeviceSession`** (`ITenantScoped`, table `device_sessions`):
`Id`, `RestaurantId`, `TableId` (FK cascade), `DeviceId` (string, ≤64), `CreatedAt`,
`LastSeenAt`. Index on `(TableId, DeviceId)` + the standard `restaurant_id` index +
global tenant query filter. `Order` gains nullable `DeviceSessionId`
(FK `SET NULL`, indexed) so orders survive session deletion as plain history.

**Config `CustomerSessionOptions`** (section `CustomerSession`, bound in `Program.cs`,
overridable via `CustomerSession__*` env vars):
- `LinkExpirySeconds` — prod default **900**; `appsettings.Development.json` sets **30**.
- `SessionLifetimeMinutes` — default **180** (sliding, from `LastSeenAt`).

**`POST /api/tables/{qrToken}/join`** (the only session-establishing endpoint the SPA
uses) additionally accepts `deviceId` in the body:
1. Purge: delete this table's `DeviceSession` rows with
   `LastSeenAt < now − SessionLifetimeMinutes` (same lazy-sweep pattern as
   `PurgeStaleGuestsAsync`).
2. Resolve: find session by `(TableId, DeviceId)` → reuse + bump `LastSeenAt`,
   else create. No/empty `deviceId` → no session (back-compat, current behavior).
3. Stamp the guest's draft order with `DeviceSessionId`.
4. Response gains a `session` object:
   ```jsonc
   session: {
     id, linkExpirySeconds,
     cumulativeTotal,          // decimal — sum over session orders below
     orders: [OrderResponse],  // status ∈ {pending_dispatch, submitted, preparing,
                               // ready, completed}, incl. soft-deleted completed
                               // rows (finalization archives after 45 s); excludes
                               // draft/cancelled/merged. Totals summed live from
                               // items (stored TotalAmount on drafts is unreliable).
     pendingDeal | null        // pending, unexpired FlashDealOffer belonging to any
                               // session order — same field shape as the
                               // FlashDealAvailable SignalR payload
   }
   ```
`SubmitOrder` bumps `DeviceSession.LastSeenAt` (activity) via the order's
`DeviceSessionId`.

### Frontend (customer SPA)

- **Device ID**: `src/services/deviceId.ts` — `getDeviceId()` returns a UUID from
  `localStorage['clarifi.deviceId']`, generated once per device (crypto-based).
  Sent with every join.
- **Session store**: new refs `deviceSessionId`, `linkExpirySeconds`,
  `cumulativeTotal`, `sessionOrders` (mirrored to sessionStorage like the rest).
- **Cumulative display**: `OrderingView.lastTotal` becomes the *session* total:
  after join it is `session.cumulativeTotal`; on submit it becomes
  `cumulativeTotal + cart total` (authoritative value returns with the next join).
  `OrderSentView` therefore shows one growing total — one "order" from the diner's
  perspective.
- **Flash deal restore**: if join carries `pendingDeal`, feed it through the same
  mapping the `FlashDealAvailable` WebSocket handler uses (extracted to a shared
  helper) so the countdown pill/modal reappear after reload/re-scan.
- **Link expiry**: after session init, a timer runs for `linkExpirySeconds`; on
  expiry a full-screen blocking overlay (RO/EN: "Sesiunea a expirat — scanează din
  nou codul QR") covers the app, the WebSocket disconnects and party polling stops
  (so a tab left open cannot keep the session alive via heartbeats). Any fresh
  navigation (re-scan) starts a new page → new timer → same device session restored.

### Testing

- Backend: integration tests (Testcontainers) on join/session lifecycle —
  create/reuse, cumulative orders after submit, expiry purge, cross-table
  isolation, pending-deal restore, no-deviceId back-compat.
- Frontend: vitest for `deviceId` service and session-store new fields;
  type-check + lint.

# Embedded QR-Phone Demo Session — Design

**Date:** 2026-08-07
**Status:** Approved
**Area:** `frontend-customer` only — zero backend changes.

## Purpose

A self-contained, hardcoded demo of the diner QR experience, reachable at a special URL and embeddable in an iframe (e.g. on a marketing site). Visitors browse a fictional branded restaurant, order, and see the "live" experience — with no backend, no database rows, and no real AI calls.

**In the demo:** hardcoded categories/items, hardcoded Vibey recommendations, white-label branding (colors/theme/logo), an always-on ("24/7") happy hour, and a flash-deal upsell that fires after the order is sent, followed by a simulated kitchen status progression.

**Not in the demo:** bill merging / other guests joining the session, and session limits (link expiry). These are absent *by construction* — the fixture session has no other party members and a far-future expiry — not by conditionally removing UI.

## Decisions (from brainstorming)

1. **Frontend-only mock.** No API or WebSocket traffic at all in demo mode. No backend changes, no demo tenant in the DB.
2. **One fixed demo brand.** A single fictional restaurant baked into fixtures; no query-param brand configuration.
3. **Simulated kitchen progression** after "Trimite comanda": `submitted → preparing → ready` on timers.
4. **Flash deal fires after the order is sent** (~8 s), as a post-order upsell (discounted dessert). Claiming it appends the item to the sent order at deal price.
5. **Approach A — demo adapter behind the existing API client** (real screens, stores, and theming code run unmodified), not a forked DemoView and not MSW.
6. **Obscured URL**: token-like slug rather than plain `/demo`.

## Entry & Routing

- No new route. The existing catch-all `/:qrToken?` (`src/router/index.ts`) stays.
- New module `src/demo/config.ts`:
  - `export const DEMO_TOKEN = 'demo-vibey-x7k2'`
  - `export function isDemoToken(token: string | null | undefined): boolean`
- Demo URL: `http://localhost:3000/demo-vibey-x7k2` (or the deployed customer origin + `/demo-vibey-x7k2`).
- Iframe embedding: no code changes needed. Constraint for deployment: the customer app's production hosting must not send `X-Frame-Options: DENY`/`SAMEORIGIN` or a restrictive `frame-ancestors` CSP for this path, or embedding breaks.

## New Modules (all under `frontend-customer/src/demo/`)

### `config.ts`
Token constant + `isDemoToken`. Every demo branch in production code imports from here — no scattered string literals.

### `fixtures.ts`
All hardcoded data, **typed against the real `@shared` contracts** so contract drift fails `npm run type-check`:

- **Join response** (`POST /api/tables/:qrToken/join` shape): fake but stable GUIDs for table/restaurant/guest/order; `partyMembers` containing only the current guest; `linkExpirySeconds: 86_400` (24 h — effectively unlimited for a demo visit; NOT 1 year, because `useLinkExpiry` uses a raw `setTimeout` and values over ~24.8 days overflow the 32-bit timer delay and fire immediately); a full `RestaurantBranding` — fictional brand (working name "Aroma Bistro"), `enabled: true`, 3-color scheme (primary/secondary/background), logo and welcome photo served as **same-origin static SVGs from `frontend-customer/public/demo/`**, referenced via `window.location.origin` (data-URIs don't work: `WelcomeView`'s URL sanitizer only accepts http(s)/relative URLs, and `/`-relative paths get rewritten to the API origin by `resolveMediaUrl`). Still zero external requests.
- **Menu** (`GET /api/restaurants/:rid/menu` shape): ~4 categories (starters, mains, drinks, desserts), ~14 items with RO + EN names/descriptions, `decimal`-style prices matching the wire contract, images as data-URIs or gradient placeholders.
- **Happy hour** (`GET /api/restaurants/:rid/happy-hour` shape): always-active deal (e.g. −20% on drinks); `endsAt` recomputed on every poll to ~2 h from now, so the countdown displays but never reaches zero — "24/7" happy hour.
- **Flash deal**: one discounted dessert offer in the exact `FlashDealAvailable` payload shape, consumed via the existing `buildFlashDealFromPayload()` mapper.
- **Vibey recommendations** (`POST /api/restaurants/:rid/recommendations` response shape): hardcoded picks with one-sentence "why" reasons, filtered at serve time against the current cart so Vibey never recommends an item already in it.

### `demoAdapter.ts`
Request-level fixture server. `src/services/api-client.ts` gains a single seam: when the session is in demo mode (qrToken is `DEMO_TOKEN`, or the stashed `restaurantId` equals the fixture restaurant id), the request wrapper delegates to the adapter instead of `fetch`.

- Pattern-matches method + URL → fixture response, with 150–400 ms simulated latency.
- Covered endpoints: join, party (self only), menu, happy hour, recommendations, cart item PATCH (echo success), order submit, claim-deal (returns updated order including the deal line), call-waiter (success; scripter later fires "on the way").
- **Unmatched requests throw loudly** (clear error naming the URL). A newly added endpoint can't silently half-work in the demo.

### `demoScripter.ts`
Replaces the WebSocket's *push* role by invoking the same store actions the real handlers in `useWebSocket.ts` call:

- On order submit: `OrderStatusUpdated`-equivalent progression — `submitted` immediately, `preparing` after ~15 s, `ready` after ~30 s more.
- ~8 s after submit: injects the flash deal into `useFlashDealStore`.
- After a call-waiter tap: `WaiterOnTheWay`-equivalent after a few seconds.
- All timers are cancelled on teardown (view unmount / fresh reset).

## Seams in Existing Code (intentionally few)

| File | Change |
|---|---|
| `src/services/api-client.ts` | one demo-mode check in the request wrapper → delegate to `demoAdapter` |
| `src/composables/useWebSocket.ts` | early-return (never connect) when in demo mode |
| `src/view/OrderingView.vue` | skip the 15 s party poll in demo mode; hook `demoScripter` into submit/call-waiter; `?fresh=1` handling |
| happy-hour polling (`useHappyHourStore`) | unchanged — its REST poll flows through the adapter naturally |

Branding needs **no seam**: the fixture join response carries `RestaurantBranding`, and the existing `applyBrand()` / `deriveBrandPalette()` pipeline does the rest.

## Features Absent by Construction

- **Merging / party join:** fixture party has only the current guest → `useGuestStore.canMerge` is false → `PartyPanel`'s merge button never renders; `PartyMerged` and join-toast paths never fire because the scripter never emits them.
- **Session limits:** `linkExpirySeconds` is one year; `LinkExpiredView` can never appear in practice.
- **Real AI:** the recommendations endpoint is served from fixtures; the Vibey cost invariant (fetch only on tap, once) is preserved because the store code is untouched.

## Reset Behavior

Demo state uses the same sessionStorage/localStorage keys as a real session, already namespaced by qrToken (`party.guest.<token>`, `clarifi.brand.<token>`, screen state). A reload resumes where the visitor left off, matching real-app behavior. The embedding page can force a clean run with `?fresh=1`, which clears the demo-scoped keys (guest identity, screen, cart mirror, brand cache) before init.

## Error Handling

- Adapter latency is bounded and every covered endpoint always succeeds — the demo cannot show error states from network flakiness (there is no network).
- Unknown endpoint in demo mode → thrown `Error` naming the path (fail fast during development; unreachable for visitors as shipped).
- Scripter timers are tracked and cleared on unmount so an embedded iframe that's removed doesn't leak intervals.

## Testing

Vitest (existing jsdom setup in `frontend-customer`):

1. `demoAdapter` returns contract-shaped responses for every covered endpoint; unmatched URL throws.
2. `demoScripter` drives the status progression and flash-deal injection into real store instances (fake timers).
3. `isDemoToken` guard behavior.
4. `useWebSocket` does not open a socket in demo mode.
5. Vibey fixture serving filters out items already in the cart.

Plus `npm run type-check` and `npm run lint`.

## Out of Scope

- Query-param brand configuration / multiple brand presets.
- Backend demo tenant, seeds, or flags.
- Analytics for demo usage.
- Any change to admin, platform, or ops frontends.

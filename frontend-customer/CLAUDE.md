# CLAUDE.md — frontend-customer

Customer QR-ordering SPA ("scan table QR → browse → order"). Vue 3.5 + TypeScript + Vite 7 + Pinia 3 + vue-router 5. Dev server: **port 3000** (`strictPort`). Bilingual RO/EN UI (Romanian default).

> ⚠️ `FRONTEND_DEV_GUIDE.md` and `README.md` in this folder are **stale and misleading** (wrong library versions, dead components described as live, wrong event names). Trust this file and the code.

## Commands

| Task | Command |
|---|---|
| Dev server | `npm run dev` |
| Type check | `npm run type-check` (`vue-tsc --build`) |
| Lint (auto-fix) | `npm run lint` (oxlint, then eslint) |
| Format | `npm run format` |
| Build | `npm run build` (type-check + vite build) |
| Tests | **None exist.** `npm test` is a stub (`echo "no tests yet"`). If adding tests, mirror frontend-admin's vitest + msw setup. |

## Architecture: screen state machine

There is one route, `/:qrToken?` ([src/router/index.ts](src/router/index.ts)), rendering [src/view/OrderingView.vue](src/view/OrderingView.vue) (note: `view/` singular, not `views/`). It orchestrates screens `language → pebbles (categories) → menu → cart → sent | merged` from `components/screens/`. Screen, active category, and cart persist to `sessionStorage` so a same-tab refresh resumes in place.

- **Session join**: `useGuestStore.join(qrToken)` → `POST /api/tables/:qrToken/join` with a per-QR `guestId` from `localStorage['party.guest.<qrToken>']`. Returns tableId, this phone's draft orderId, restaurant info, party roster — mirrored into `sessionStorage`.
- **Party ordering** (multi-person): each phone = own guest + own draft order on a shared table. `PartyPanel.vue` shows live carts; any guest can merge all drafts (`POST /api/tables/:qrToken/merge`) — merged-away phones freeze their cart and show `MergedView`. Presence via 15s polling of `GET .../party`.
- **Cart**: `useCartStore` mutates locally and debounce-syncs (500ms) to the backend draft via `PATCH .../orders/:orderId/items`. Submit → `POST .../submit` → `OrderSentView` timeline.
- **No payments.** Diners pay at the table (cash/card). Do not invent payment flows; `paymentStatus` exists only in shared backend types.

## Realtime — hand-rolled SignalR client (critical)

[src/composables/useWebSocket.ts](src/composables/useWebSocket.ts) implements the SignalR JSON protocol **over a raw `WebSocket`** — `@microsoft/signalr` is NOT a dependency here. It handshakes, then invokes `JoinTableGroup(restaurantId, tableId)` and `JoinRestaurantMenuGroup(restaurantId)` on `${VITE_WS_URL}/hubs/customer?token=<qrToken>`. Reconnect = 5s retry after close.

PascalCase server events handled: `FlashDealAvailable`, `FlashDealExpired`, `OrderStatusUpdated`, `MenuItemAvailabilityChanged` (out-of-stock feature), `WaiterOnTheWay`, `PartyMemberJoined/Left/Updated`, `PartyMerged`. To add an event: handler here + backend broadcast; keep the reconnect path in mind.

## State

Pinia setup-stores in `src/stores/`: `session` (identity, sessionStorage-backed), `guest` (party roster; deliberately a leaf store — no cross-store imports), `cart` (debounced sync, `frozen` flag, server-reconciliation helpers), `orderStatus` (timeline timestamps, survives refresh), `flashDeal`, `helpCall` (call-waiter with 120s cooldown, 429-aware).

Menu and recommendations are **not** stores — module-singleton composables (`useMenu` with a single in-flight `loadPromise`, `useRecommendations`). Out-of-stock: `useMenu.setAvailability()` flips `isAvailable`/`outOfStock` in place; `MenuView` applies `.dish-out` (opacity + grayscale) and disables Add.

## Conventions & gotchas

- **i18n**: [src/composables/useLocale.ts](src/composables/useLocale.ts) inline RO/EN dictionary — every user-facing string needs both languages.
- **Styling**: design tokens from [src/design/tokens.ts](src/design/tokens.ts) (`T`/`TD` objects) via inline `:style` + scoped CSS. Tailwind is installed but barely used. Phone-frame layout: real phones full-screen (`100dvh`), desktop shows a 390×844 preview frame.
- **Dead code**: `components/Cart.vue`, `MenuList.vue`, `RecommendationWidget.vue`, `FlashDealBanner.vue` are unused legacy — don't extend them. Active UI lives in `components/screens/` + `components/ui/`.
- **Aliases**: `@` → `./src`, `@shared` → `../shared` (type-only imports of contracts).
- **Media URLs**: relative `/media`–style paths must be rewritten via `services/mediaUrl.ts`; `api-client.ts` already does this for responses (`resolveResponseMediaUrls`).
- **Env**: `VITE_API_URL` (default `http://localhost:5005`), `VITE_WS_URL` (default `ws://localhost:5005`). Baked at **build time** (Railway build ARGs); docker-compose injects them for dev. Local `npm run dev` works on defaults.
- Persistence keys: theme `localStorage['clarifi.theme']`, locale `localStorage['clarifi.locale']`, guest `localStorage['party.guest.<qrToken>']`, brand cache `localStorage['clarifi.brand.<qrToken>']`; everything session-scoped in `sessionStorage`.
- **Brand paint-on-boot** — branding only arrives with the join response, so without help a branded restaurant shows the default tokens for the length of that round-trip (very visible on Railway). Two mechanisms cover it, and they're deliberately complementary:
  1. `main.ts` calls `hydrateBrandFromCache(qrToken-from-URL)` **before mount**, repainting the last-known palette from `@shared/branding/cache` (`localStorage['clarifi.brand.<qrToken>']`, written by `useSessionStore` when join lands). Covers reloads and repeat scans. Keep the call before `app.mount()`.
  2. `useSessionStore` persists the whole `branding` object to `sessionStorage['branding']` (render hint; join overwrites) and restores it on init **only when `sessionStorage['qrToken']` matches the URL's token** — a mid-session reload skips the splash and renders its restored screen immediately, so without this the branding *images* (welcome photo, logo, menu background — which the colour cache does not carry) pop in when join lands.
  3. `BootSplash.vue` covers the gap until join can show the **final starter screen**. `OrderingView` sets `booting = !brandKnown || (!hadStoredScreen && isBranded)` — it splashes both when the palette is genuinely unknown (a cache miss: first-ever scan, private browsing, cleared storage) **and** on a fresh session of a known-branded table. The second half matters because the cache restores *colours only*, never which screen to open: a branded-with-photo table starts on `WelcomeView` (needs `branding.photoUrl`, which only lands with join), so without the splash the cache-painted palette renders `LanguageSelectView` first and join then flips it to the welcome page — a visible screen swap on every repeat scan. A mid-session refresh (stored screen) and a known-unbranded table both render immediately. Clear `booting` on **every** join outcome (the `finally` in `onMounted`), or a failed join leaves the diner stuck behind the splash.
  - The splash is intentionally un-themed (hard-coded `TD.paper`/`TD.ink`): reading `tokens` there would paint the default light palette, which is the exact flash it exists to hide. Any floating control that can appear during boot needs `&& !booting` (the theme toggle's `v-if="!isBranded"` is true precisely while splashing).
  - ⚠️ Was tried and reverted (74ff4fc → 83ff64c): serving colours with the HTML via a public branding endpoint + an express page server replacing `serve`. It worked locally but broke the Railway deploy twice (Express 5 wildcard 404 on `/`, then a PORT mismatch — `serve` hardcodes 3000 and ignores Railway's PORT). Don't retry without accounting for both.

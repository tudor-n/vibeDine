# Segment 18 — Customer SPA: shell & state — review findings

Scope review against the [Segment 18 checklist](CODE_REVIEW_ROADMAP.md#segment-18--customer-spa--shell--state) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). Files in scope: [main.ts](frontend-customer/src/main.ts), [App.vue](frontend-customer/src/App.vue), [router/index.ts](frontend-customer/src/router/index.ts), [view/OrderingView.vue](frontend-customer/src/view/OrderingView.vue), all four Pinia stores ([useSessionStore.ts](frontend-customer/src/stores/useSessionStore.ts), [useCartStore.ts](frontend-customer/src/stores/useCartStore.ts), [useFlashDealStore.ts](frontend-customer/src/stores/useFlashDealStore.ts), [useOrderStatusStore.ts](frontend-customer/src/stores/useOrderStatusStore.ts)), and [services/api-client.ts](frontend-customer/src/services/api-client.ts). Cross-references: [composables/useMenu.ts](frontend-customer/src/composables/useMenu.ts), [composables/useLocale.ts](frontend-customer/src/composables/useLocale.ts), [composables/useTheme.ts](frontend-customer/src/composables/useTheme.ts), [vite.config.ts](frontend-customer/vite.config.ts), [eslint.config.ts](frontend-customer/eslint.config.ts), [components/screens/CartView.vue](frontend-customer/src/components/screens/CartView.vue).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**7 findings, two of them HIGH.** The shell is well-shaped and most of the roadmap's checklist items pass at first read: the router has a single `/:qrToken?` route ([router/index.ts:8](frontend-customer/src/router/index.ts#L8)), a missing or invalid QR token surfaces a `LanguageSelectView` + `session-error` overlay rather than a stack trace ([OrderingView.vue:34-47, 86-99](frontend-customer/src/view/OrderingView.vue#L34-L99)), `useSessionStore` reads and writes to `sessionStorage` exclusively ([useSessionStore.ts:6-13, 28-35](frontend-customer/src/stores/useSessionStore.ts#L6-L35)), every API call routes through the `apiClient` ([api-client.ts:52-81](frontend-customer/src/services/api-client.ts#L52-L81)) — `grep "\\bfetch\\(" frontend-customer/src` returns one match and it's the `fetcher` helper itself ([line 32](frontend-customer/src/services/api-client.ts#L32)), no `alert(...)` anywhere (grep confirms), `VITE_*` env keys are the only configuration knobs ([api-client.ts:4](frontend-customer/src/services/api-client.ts#L4), [useWebSocket.ts:8](frontend-customer/src/composables/useWebSocket.ts#L8)), and the `RouterView` shell is correctly minimal ([App.vue:1-7](frontend-customer/src/App.vue#L1-L7)). `useLocale` and `useTheme` keep cross-tab preferences in `localStorage` while the QR-session state stays in `sessionStorage` — the right split.

The findings cluster around three themes:

1. **Cart state is fragile across refresh, and its errors are invisible.** [F1](#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution) — `useCartStore` keeps `items` in a fresh `ref<CartItem[]>([])` ([useCartStore.ts:14](frontend-customer/src/stores/useCartStore.ts#L14)) with no `sessionStorage` / `localStorage` rehydration; tab refresh empties the cart. The backend *does* have the cart (`syncCartWithBackend` writes to the active order on every change), but nothing rehydrates the client from `activeOrder` on mount. The `recommendedItemIds: Set<string>` ([line 15](frontend-customer/src/stores/useCartStore.ts#L15)) used for `wasAiRecommended` attribution is also lost — a cart that was 80% AI-recommended pre-refresh becomes 0% AI-recommended after. [F2](#f2--high--cart-sync-errors-are-silent-only-consoleerror-no-toastbanner-no-retry) — `syncCartWithBackend` catches `apiClient.updateCartItems` failures with `console.error` ([useCartStore.ts:45](frontend-customer/src/stores/useCartStore.ts#L45)) and emits no UI signal; the customer sees their local cart fine but the backend has a stale (or empty) order, and `submitOrder` then sends what the backend has. The roadmap is explicit: *"API errors render a toast/banner; never `alert(...)`."* — banners exist on `submit` ([CartView.vue:50-69](frontend-customer/src/components/screens/CartView.vue#L50-L69)) but not on the sync path that's running constantly.
2. **Production hardening on the build is missing.** [F3](#f3--medium--consolelog-not-stripped-in-vite-build-no-no-console-lint-rule) — `vite.config.ts` has no `esbuild.drop`/`terserOptions.compress.drop_console`, and `eslint.config.ts` doesn't add `no-console`. The WebSocket composable emits five `console.log`/`console.error` calls per connection cycle ([useWebSocket.ts:27, 99, 104, 113](frontend-customer/src/composables/useWebSocket.ts#L27-L113)) which ship to the diner's browser. Roadmap explicit ask. [F4](#f4--medium--fetcher-has-no-timeout-or-abortcontroller-a-hung-request-hangs-the-ui-forever) — `fetcher` issues `fetch(...)` with no `signal` / no timeout ([api-client.ts:31-50](frontend-customer/src/services/api-client.ts#L31-L50)); a hung backend leaves the UI in an indefinite "Submitting" / "Loading" state.
3. **Error UX has no global home.** [F5](#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern) — no `app.config.errorHandler`, no global notification system, no shared composable; every consumer decides on its own how to surface errors. `CartView.submit` does the right thing with a local `submitError.value` ref + banner ([CartView.vue:50-69](frontend-customer/src/components/screens/CartView.vue#L50-L69)), but the cart-sync path ([F2](#f2--high--cart-sync-errors-are-silent-only-consoleerror-no-toastbanner-no-retry)), the menu load fallback, and the recommendation fetch all swallow errors silently. The roadmap's `↳` *"When the backend returns a localized error message, does the customer SPA forward it or replace it with a generic message in their language?"* is answered correctly at the `fetcher` layer (it forwards `errorData.message || errorData.error`, [line 42](frontend-customer/src/services/api-client.ts#L42)) — but the forwarding only matters when somebody renders the forwarded message, which happens in exactly one place.

The remaining items are smaller: [F6](#f6--low--localhost5005--wslocalhost5005-fallbacks-mask-misconfigured-env-vars) — the `import.meta.env.VITE_API_URL || 'http://localhost:5005'` fallbacks at [api-client.ts:4](frontend-customer/src/services/api-client.ts#L4) and [useWebSocket.ts:8](frontend-customer/src/composables/useWebSocket.ts#L8) silently target localhost when the env var isn't set in production; the roadmap names this as a hardcoded-localhost concern. [F7](#f7--low--recommendeditemids-reset-on-mount-its-also-a-non-deeply-reactive-set-in-a-ref) — `Set` wrapped in `ref()` doesn't deep-react to `set.add(...)` mutations; consumers that read `wasRecommended()` through `items` happen to re-render because `items` is the reactive source, but the pattern is fragile.

What does check out: the segment-12 hub finding (anonymous `CustomerHub`) is *consumed* correctly by this SPA at least in shape — `useSessionStore.initializeSession` is gated by the QR token in the URL, the QR-error overlay surfaces *before* any session-dependent screen renders, and `useCartStore.syncCartWithBackend` correctly bails when `orderId` is missing ([useCartStore.ts:26-29](frontend-customer/src/stores/useCartStore.ts#L26-L29)). The session-error UI ([OrderingView.vue:86-99](frontend-customer/src/view/OrderingView.vue#L86-L99)) is localised and distinguishes "missing token" from "invalid token" — better than the roadmap's minimum bar. The `useMenu` composable correctly de-duplicates concurrent loads via `loadPromise` ([useMenu.ts:9, 12](frontend-customer/src/composables/useMenu.ts#L9-L12)) and exposes `isLoading` / `error` refs ([line 7-8](frontend-customer/src/composables/useMenu.ts#L7-L8)) — explicit per-feature loading state, which the roadmap asks for. The `apiClient.fetcher` returns the backend's localised message when present, preserving the API's i18n contract.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| App boots from a QR token in the URL (`/:qrToken?`). Missing or invalid token shows a clean "Scan your QR code" screen, not a stack trace | ✓ Pass | [router/index.ts:8](frontend-customer/src/router/index.ts#L8) — `path: '/:qrToken?'`. Missing → `sessionError.value = 'missing'` ([OrderingView.vue:36-38](frontend-customer/src/view/OrderingView.vue#L36-L38)). Invalid → `try/catch` around `initializeSession`, [line 40-46](frontend-customer/src/view/OrderingView.vue#L40-L46). Overlay renders a localised, styled card. |
| `sessionStorage` (not `localStorage`) holds the resolved `restaurantId` — wiped on tab close | ✓ Pass | [useSessionStore.ts:10, 32](frontend-customer/src/stores/useSessionStore.ts#L10-L32), [api-client.ts:18, 22, 26](frontend-customer/src/services/api-client.ts#L18-L26). All session data uses `sessionStorage`. `localStorage` is reserved for cross-session preferences (locale, theme). |
| Cart store persists across tab refresh (which storage? confirm and document expiry) | ❌ Fail — see [F1](#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution) | `items = ref<CartItem[]>([])` ([useCartStore.ts:14](frontend-customer/src/stores/useCartStore.ts#L14)) — no `sessionStorage` / `localStorage`, no rehydration from `activeOrder` on mount. |
| All API calls go through `api-client.ts` — no `fetch` scattered through components | ✓ Pass | Grep `fetch(` across [src/](frontend-customer/src/) returns one hit, in `api-client.ts:32`. Every consumer imports `{ apiClient }`. |
| API errors render a toast/banner; never `alert(...)` | ⚠ Partial — see [F2](#f2--high--cart-sync-errors-are-silent-only-consoleerror-no-toastbanner-no-retry), [F5](#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern) | `alert` ✓ (grep returns zero hits). Toast/banner ❌ — only `CartView.submit` renders an error UI; cart-sync, menu-load, recommendation-fetch errors are silent `console.error`. |
| Loading states are explicit per feature (no global blocking spinner that masks problems) | ✓ Pass (mostly) | [useMenu.ts:7-8](frontend-customer/src/composables/useMenu.ts#L7-L8) — `isLoading`, `error` refs. [CartView.vue:60, 67](frontend-customer/src/components/screens/CartView.vue) — `isSubmitting`. No global blocker. |
| No `console.log` in production builds (lint rule or strip) | ❌ Fail — see [F3](#f3--medium--consolelog-not-stripped-in-vite-build-no-no-console-lint-rule) | [vite.config.ts](frontend-customer/vite.config.ts) has no `esbuild.drop`. [eslint.config.ts](frontend-customer/eslint.config.ts) has no `no-console` rule. WebSocket composable alone ships five console calls to prod. |
| Vite env vars used (`VITE_API_URL`, `VITE_WS_URL`) — no hardcoded localhost:5005 | ⚠ Partial — see [F6](#f6--low--localhost5005--wslocalhost5005-fallbacks-mask-misconfigured-env-vars) | Env vars are honoured ([api-client.ts:4](frontend-customer/src/services/api-client.ts#L4), [useWebSocket.ts:8](frontend-customer/src/composables/useWebSocket.ts#L8)) — but each line falls back to a `localhost` literal if the env isn't set. |
| No secrets in Vite env (`VITE_*` is shipped to the browser) | ✓ Pass | Only `VITE_API_URL` and `VITE_WS_URL` are read. Both are public endpoints. No secrets ever needed (the API is QR-token-authed; no client-side keys). |
| Accessibility: tap targets ≥44px (this is the diner-facing app on phones) | Out of scope — not verified | The shell-level tap targets are the theme toggle (40×40px, [OrderingView.vue:222-223](frontend-customer/src/view/OrderingView.vue#L222-L223), under the 44px Apple HIG minimum) and the close button (size not visible from this segment). Detailed per-component pass belongs in segments 22/23-equivalent for the customer SPA, but the one shell-level tap target *is* under-spec — minor. |
| Backend returns a localized error — does the SPA forward it? (roadmap `↳`) | ✓ Pass (when surfaced at all) | [api-client.ts:42](frontend-customer/src/services/api-client.ts#L42) — `errorData.message || errorData.error || \`API Error: ${response.status}\``. Forwarded as the `Error.message`. Only `CartView.submit` actually renders it ([CartView.vue:65](frontend-customer/src/components/screens/CartView.vue#L65)) — see [F5](#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern). |

---

## F1 — HIGH — Cart store doesn't persist; tab refresh loses cart contents and the `wasAiRecommended` attribution

[useCartStore.ts:12-20](frontend-customer/src/stores/useCartStore.ts#L12-L20):

```ts
export const useCartStore = defineStore('cart', () => {
  const items = ref<CartItem[]>([])
  const recommendedItemIds = ref<Set<string>>(new Set())
  const sessionStore = useSessionStore()

  const total = computed(() => {
    return items.value.reduce((sum, item) => sum + item.price * item.quantity, 0)
  })
```

No `sessionStorage.getItem('cart')` / `JSON.parse` read at construction; no rehydration in any `onMounted` block of the shell; no `watch(items, ...)` that mirrors to storage. After a tab refresh:

- `items.value === []`. The UI's Cart screen reads empty.
- `recommendedItemIds.value === new Set()`. The flag that powers AI-attribution accounting on `submit` ([useCartStore.ts:34](frontend-customer/src/stores/useCartStore.ts#L34) — `wasAiRecommended: recommendedItemIds.value.has(item.id)`) returns `false` for every item.
- The session does survive — `useSessionStore` reads from `sessionStorage` ([line 6-13](frontend-customer/src/stores/useSessionStore.ts#L6-L13)) — so `orderId` is still present. But the cart store has no concept of "re-hydrate from the backend's active order."

Two related downstream effects:

- **The backend's order is still correct, but the customer can't see it.** `syncCartWithBackend` writes through to `PATCH /api/restaurants/{rid}/orders/{orderId}/items` on every add/remove. The backend state lives. Then refresh: client cart empty, backend cart full. When the customer adds an item post-refresh, `syncCartWithBackend` sends a *new* item list (containing only the just-added item) — overwriting the backend with a single item.
- **AI attribution is wiped on refresh.** The recommendation engine's effectiveness metric flows through `OrderItem.WasAiRecommended` ([AdminController.cs:257](backend/RestaurantApi/Controllers/AdminController.cs#L257) — `where i.WasAiRecommended == true`). A customer who refreshes after accepting two AI recommendations now has zero items marked AI-recommended on submit. The metric the admin sees in [PerformanceMetrics](frontend-admin/src/components/PerformanceMetrics.vue) (Segment 23) under-counts.

The roadmap calls this out directly: *"Cart store persists across tab refresh (which storage? confirm and document expiry)."* No persistence, no documented expiry.

Two clean fixes:

- **Mirror to `sessionStorage`** (matches the session's storage class): `watch([items, recommendedItemIds], () => sessionStorage.setItem('cart', JSON.stringify(...)), { deep: true })` + a constructor-time read. `sessionStorage` is per-tab, so it survives refresh but not tab-close — exactly the cart's natural lifetime.
- **Rehydrate from `activeOrder` on mount.** Extend `apiClient.initSession` (or add a sibling endpoint) to return the active order's items; `useCartStore` reads them on first mount. The `wasAiRecommended` flag survives because it's persisted in `OrderItem.WasAiRecommended` on the backend.

The second option is the durable one — survives tab-close too — but requires backend-side cooperation. The first is one-file.

---

## F2 — HIGH — Cart-sync errors are silent (`console.error` only); no toast/banner, no retry

[useCartStore.ts:25-51](frontend-customer/src/stores/useCartStore.ts#L25-L51):

```ts
async function syncCartWithBackend() {
  if(!sessionStore.orderId){
    console.warn('Nu există o comandă activă (orderId lipsește).')
    return
  }
  const payload: UpdateOrderItemsRequest = { ... }

  if (syncTimeout) clearTimeout(syncTimeout)

  lastSyncPromise.value = new Promise<void>((resolve) => {
    syncTimeout = setTimeout(async () => {
      try {
        await apiClient.updateCartItems(sessionStore.orderId!, payload)
      } catch (error) {
        console.error('Eroare la sincronizarea coșului pe backend:', error)
      } finally {
        resolve()
      }
    }, 500)
  })
}
```

Every `addItem` / `removeItem` triggers a 500ms-debounced sync. The error path:

- Catches `Error` from `apiClient.updateCartItems`.
- `console.error`s the message.
- `resolve()`s the promise to "successful" — the consumer awaiting `lastSyncPromise` can't distinguish "sync succeeded" from "sync failed silently."

Three concrete failure modes:

- **Network glitch mid-session.** The customer adds three items, the second sync fails (transient 5xx, network drop). They submit. The backend's order has items 1 only. The customer sees a confirmation screen for items 1+2+3 (from local state) and is then confused when the kitchen brings only item 1.
- **`orderId` is stale.** After [F1](#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution)'s refresh path lands a new `orderId` from `initializeSession`, a stale debounce might fire against the old order. Caught the same way — `console.error`, no UI signal.
- **Backend returns a localised error** (e.g., 409 because the order was finalised by staff). `fetcher` correctly forwards the localized `errorData.message` ([api-client.ts:42](frontend-customer/src/services/api-client.ts#L42)) — but the sync wraps it in a `console.error` and discards it.

The roadmap's "render a toast/banner" item exists *somewhere* — `CartView.submit` does it ([CartView.vue:50-69](frontend-customer/src/components/screens/CartView.vue#L50-L69)) — but the cart-sync path is the operation that runs *constantly* and has no surfacing. A customer interacting with the menu has no signal that their changes aren't being recorded.

Fix shape (out of scope): add a `syncError: Ref<string | null>` to the store, set it in the `catch`, render it as a banner in the cart UI, and add a retry path (re-issue the latest payload on click). The 500ms debounce is the right shape for happy-path UX; the silent-on-failure model is wrong for any actual failure.

---

## F3 — MEDIUM — `console.log` not stripped in Vite build; no `no-console` lint rule

[vite.config.ts:5-17](frontend-customer/vite.config.ts#L5-L17):

```ts
export default defineConfig({
  plugins: [vue()],
  resolve: { ... },
  server: {
    port: 3000,
    strictPort: true
  }
})
```

No `esbuild.drop: ['console']`, no `build.minify: 'terser'` with `terserOptions.compress.drop_console: true`, no production-mode guard around the dev-only `vite-plugin-vue-devtools` either (the plugin is in `devDependencies` so it's tree-shaken, but the absence of an explicit `mode === 'production'` check is the kind of detail that bites in CI).

[eslint.config.ts:12-26](frontend-customer/eslint.config.ts#L12-L26):

```ts
export default defineConfigWithVueTs(
  { name: 'app/files-to-lint', files: ['**/*.{vue,ts,mts,tsx}'] },
  globalIgnores(['**/dist/**', '**/dist-ssr/**', '**/coverage/**']),
  ...pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,
  ...pluginOxlint.buildFromOxlintConfigFile('.oxlintrc.json'),
  skipFormatting,
)
```

No `'no-console': ['error', { allow: ['warn', 'error'] }]` rule. The oxlint config at [.oxlintrc.json](frontend-customer/.oxlintrc.json) sets only `"categories": { "correctness": "error" }` — `no-console` falls under "suspicious" / "style" categories, not "correctness."

Concrete payload of console output that ships to production today (grep `console\.` across [src/](frontend-customer/src/)):

| Site | Level | Message |
|---|---|---|
| [useWebSocket.ts:27](frontend-customer/src/composables/useWebSocket.ts#L27) | `log` | `"Conexiune WebSocket stabilită!"` |
| [useWebSocket.ts:99](frontend-customer/src/composables/useWebSocket.ts#L99) | `error` | `"Eroare la parsarea mesajului WebSocket SignalR:"` |
| [useWebSocket.ts:104](frontend-customer/src/composables/useWebSocket.ts#L104) | `log` | `"Conexiune WebSocket închisă."` |
| [useWebSocket.ts:113](frontend-customer/src/composables/useWebSocket.ts#L113) | `error` | `"Eroare WebSocket:"` |
| [useCartStore.ts:27](frontend-customer/src/stores/useCartStore.ts#L27) | `warn` | `"Nu există o comandă activă (orderId lipsește)."` |
| [useCartStore.ts:45](frontend-customer/src/stores/useCartStore.ts#L45) | `error` | `"Eroare la sincronizarea coșului pe backend:"` |
| [useSessionStore.ts:37](frontend-customer/src/stores/useSessionStore.ts#L37) | `error` | `"Eroare la inițializarea sesiunii:"` |
| [OrderingView.vue:44](frontend-customer/src/view/OrderingView.vue#L44) | `error` | `"Could not initialize session"` |
| [useRecommendations.ts:81](frontend-customer/src/composables/useRecommendations.ts#L81) | `error` | `"Eroare la încărcarea recomandărilor:"` |

Nine call sites, mostly `console.error`. Roadmap's "lint rule or strip" allows either; neither is in place. The `console.error` calls double as the *only* failure-mode signal (per [F2](#f2--high--cart-sync-errors-are-silent-only-consoleerror-no-toastbanner-no-retry) and [F5](#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern)) — stripping them naively would erase the only error trail. The right shape is to wire a real logger first (or a toast), *then* strip — not the reverse.

Two-line fix (after wiring real surfaces): add `esbuild: { drop: ['console'] }` to `vite.config.ts` and `'no-console': ['warn', { allow: ['warn', 'error'] }]` to `eslint.config.ts`. The latter is a guard for new code; the former takes care of the existing surface.

---

## F4 — MEDIUM — `fetcher` has no timeout or `AbortController`; a hung request hangs the UI forever

[api-client.ts:31-50](frontend-customer/src/services/api-client.ts#L31-L50):

```ts
async function fetcher<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${endpoint}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
  });
  ...
}
```

No `signal: AbortSignal.timeout(N)`, no per-call timeout, no global circuit breaker. The browser's default `fetch` waits as long as the OS/proxy holds the TCP socket open — minutes, sometimes indefinitely on misbehaving captive portals.

Concrete UX consequences:

- `CartView.submit` ([CartView.vue:50-69](frontend-customer/src/components/screens/CartView.vue#L50-L69)) wraps `apiClient.submitOrder` in `isSubmitting.value = true`. If the backend is slow / partition'd, the button stays in "Submitting" forever and the customer has no recovery path except a page refresh — which loses the cart per [F1](#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution).
- `useMenu.load` ([useMenu.ts:11-66](frontend-customer/src/composables/useMenu.ts#L11-L66)) sets `isLoading.value = true` and waits indefinitely on a hung `getMenu` — the customer sees a permanent spinner with no way to retry.
- `syncCartWithBackend` ([F2](#f2--high--cart-sync-errors-are-silent-only-consoleerror-no-toastbanner-no-retry)) hangs silently — `lastSyncPromise` never resolves.

The fix is one line per call site or one line in `fetcher`:

```ts
const response = await fetch(`${BASE_URL}${endpoint}`, {
  signal: AbortSignal.timeout(15_000),
  ...options,
  headers: { 'Content-Type': 'application/json', ...options?.headers },
});
```

`AbortSignal.timeout` is supported in all modern browsers (since Chrome 103, Safari 16.4). The resulting `DOMException(name: "TimeoutError")` is caught by the existing `try/catch` and surfaced via `fetcher`'s thrown `Error`.

---

## F5 — MEDIUM — No global error handler, no toast system; per-feature error state is the only pattern

`grep` across [src/](frontend-customer/src/) for `app\.config\.errorHandler`, `toast`, `Toast`, `useNotification`, `Snackbar` returns one weak hit (a comment about a *cart success* state in `FlashDealBanner.vue:82`). There is no shared error-surface composable, no global notification queue, no `app.config.errorHandler` registered in [main.ts](frontend-customer/src/main.ts):

```ts
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import './style.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.mount('#app')
```

Bare. The pattern in the codebase is "each component or store holds its own error ref" — and only `CartView.submit` actually renders one. The other consumers swallow with `console.error`:

| Surface | Error handling |
|---|---|
| `CartView.submit` | ✓ Local `submitError.value` rendered as a banner. |
| `useCartStore.syncCartWithBackend` | ❌ `console.error` only (see [F2](#f2--high--cart-sync-errors-are-silent-only-consoleerror-no-toastbanner-no-retry)). |
| `useSessionStore.initializeSession` | ⚠ Throws to the caller; caller (`OrderingView`) sets `sessionError.value` and renders the error overlay. Works for the boot path. |
| `useMenu.load` | ⚠ Sets `error.value` ref — but no consumer in the existing screens reads it (`MenuView` reads `categories`/`products` and `isLoading` only — verify in Segment 18's UI-pass). |
| `useRecommendations` | ❌ `console.error` (line 81). |
| `useWebSocket` error path | ❌ `console.error` (line 99, 113) — the customer never sees that the realtime connection dropped. |

The roadmap's `↳` follow-up — *"When the backend returns a localized error message, does the customer SPA forward it or replace it with a generic message in their language?"* — is answered correctly by `fetcher` ([api-client.ts:42](frontend-customer/src/services/api-client.ts#L42)), which forwards `errorData.message` if present. But the forwarded message only reaches the user in the single `CartView.submit` site.

Fix shape (out of scope): introduce a `useNotifications()` composable + a `<NotificationToast />` mounted in `App.vue`. Wire `useCartStore.syncCartWithBackend`, `useMenu.load`, `useRecommendations` to call `notifications.error(...)` instead of `console.error`. The pattern is well-established in any Vue codebase; here it doesn't exist.

`app.config.errorHandler` separately catches *uncaught* errors in component render / lifecycle hooks; a one-liner in `main.ts` would route those to the same toast surface. Today an unhandled exception in a `setup()` block silently produces a blank screen.

---

## F6 — LOW — `localhost:5005` / `ws://localhost:5005` fallbacks mask misconfigured env vars

[api-client.ts:4](frontend-customer/src/services/api-client.ts#L4):

```ts
const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005';
```

[useWebSocket.ts:8](frontend-customer/src/composables/useWebSocket.ts#L8):

```ts
const rawWsUrl = import.meta.env.VITE_WS_URL || 'ws://localhost:5005'
```

The roadmap names *"no hardcoded localhost:5005"* — the literals are *fallback* values via `||`, not hardcoded targets, so the strictest reading passes. In docker-compose dev these are overridden ([docker-compose.yml:71-72](docker-compose.yml#L71-L72)).

The concern is the failure mode in production. If a future deploy forgets to set `VITE_API_URL` (or sets it to an empty string), the customer SPA silently targets `http://localhost:5005` — which from the diner's phone is *their phone*, so every request fails with a network error, but the failure looks like "backend is down" rather than "frontend is misconfigured." Vite's `import.meta.env.VITE_*` becomes the string `"undefined"` if you do `import.meta.env.VITE_API_URL` and it's missing — or empty string in some build configs — and `'' || 'http://localhost:5005'` evaluates to `'http://localhost:5005'`.

A guard at module-init time — `if (!import.meta.env.VITE_API_URL && import.meta.env.PROD) throw new Error('VITE_API_URL required')` — would fail-fast at first render in misconfigured prod, before any network call is attempted. Today, the same misconfiguration produces a soft failure that's harder to diagnose.

LOW because dev/prod parity is handled by docker-compose today; the failure mode is a deploy-time misconfig that the current shape doesn't catch.

---

## F7 — LOW — `recommendedItemIds` reset on mount; it's also a non-deeply-reactive `Set` in a `ref`

[useCartStore.ts:15, 64-67](frontend-customer/src/stores/useCartStore.ts#L15-L67):

```ts
const recommendedItemIds = ref<Set<string>>(new Set())

async function addRecommendedItem(product: Product) {
  recommendedItemIds.value.add(product.id)
  await addItem(product)
}
```

Two related concerns:

- **Reset on mount** — already covered in [F1](#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution): tab refresh empties this Set, so the `wasAiRecommended` flag on `syncCartWithBackend`'s payload is `false` for every item after a refresh.
- **Non-deeply-reactive Set in a `ref`** — `ref(new Set())` creates a `ShallowRef`-like wrapper around the Set. Mutating with `recommendedItemIds.value.add(id)` doesn't trigger a reactivity tick because `.value` (the Set reference) hasn't changed. Vue 3 *does* track `Set`/`Map` mutations *if* the Set is reactive (`reactive(new Set())`) — but `ref(new Set())` doesn't get the proxy treatment. Today this only matters in places that *read* `recommendedItemIds` directly; the most critical consumer (`wasRecommended()` in the `syncCartWithBackend` payload) is called inside a `.map()` that already depends on the reactive `items`, so the Set is read fresh at call time. The fragility is in *future* consumers — a template that does `{{ recommendedItemIds.size }}` won't re-render when `.add` is called.

The fix is `ref<ReadonlyMap<string, true>>(new Map())` or `reactive(new Set<string>())`. Or — better — store the flag inline on each `CartItem` (`wasRecommended: boolean`) and drop the parallel Set entirely; then `items` is the single source of truth and the reactivity question disappears. The current shape has the data split across `items` and `recommendedItemIds`, which is also why the refresh loss in [F1](#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution) costs two things instead of one.

---

## What's solid

| Item | Why it's good |
|---|---|
| `RouterView` shell is minimal ([App.vue](frontend-customer/src/App.vue)) | No middleware/guards/layouts to debug — a single named route ([router/index.ts](frontend-customer/src/router/index.ts)) keeps boot deterministic. |
| Session error UX ([OrderingView.vue:86-99](frontend-customer/src/view/OrderingView.vue#L86-L99)) | Distinguishes "missing token" from "invalid token", renders a localised, styled overlay rather than a thrown exception or blank screen, and yields to the language picker so the user can localise before reading the error. |
| `sessionStorage` for QR-derived state ([useSessionStore.ts](frontend-customer/src/stores/useSessionStore.ts)) | Correct storage class — wipes on tab close, scopes per-tab. Aligned with the roadmap's QR-session model. |
| `localStorage` for locale / theme ([useLocale.ts:223-225, 233-235](frontend-customer/src/composables/useLocale.ts#L223-L235), [useTheme.ts:8-22](frontend-customer/src/composables/useTheme.ts#L8-L22)) | Cross-session preferences survive tab close — the right inversion of the session-data class. |
| `apiClient` is the single ingress ([api-client.ts](frontend-customer/src/services/api-client.ts)) | One place to add timeout, headers, auth, retry. The seam exists; only the implementations behind it are partial. |
| Backend localised messages forwarded ([api-client.ts:42](frontend-customer/src/services/api-client.ts#L42)) | `errorData.message || errorData.error || ...` correctly preserves the API's i18n contract. The roadmap's `↳` follow-up is answered. |
| `useMenu.load` de-duplicates concurrent loads ([useMenu.ts:9, 12](frontend-customer/src/composables/useMenu.ts#L9-L12)) | `loadPromise` cache prevents N parallel fetches from N components mounting at once. |
| 500ms cart-sync debounce ([useCartStore.ts:49](frontend-customer/src/stores/useCartStore.ts#L49)) | Right UX shape — rapid add/remove collapses into one sync. The error handling around it is the issue, not the debounce itself. |
| Type imports from `@shared/types/*` ([useCartStore.ts:3-4](frontend-customer/src/stores/useCartStore.ts#L3-L4), [api-client.ts:1-2](frontend-customer/src/services/api-client.ts#L1-L2)) | The shared-types boundary the roadmap mandates for Segment 24 is honoured at the consumption layer. |

---

## Cross-segment follow-ups raised here

- **[Segment 02](CODE_REVIEW_ROADMAP.md#segment-02--ci-workflows--build-hygiene):** [F3](#f3--medium--consolelog-not-stripped-in-vite-build-no-no-console-lint-rule) — `npm run lint` exists ([package.json:13-15](frontend-customer/package.json#L13-L15)) but has no `no-console` rule. The CI workflow needs both (a) lint to actually run, and (b) the rule to actually exist. Segment 02 should verify the lint job catches console.log.
- **[Segment 09](CODE_REVIEW_ROADMAP.md#segment-09--controllers--customer-surface):** the cart-rehydrate path in [F1](#f1--high--cart-store-doesnt-persist-tab-refresh-loses-cart-contents-and-the-wasairecommended-attribution) wants backend support — `apiClient.initSession` would naturally return `activeOrder.items` if the C# `/api/tables/{qrToken}/session` endpoint included them. Today the segment-09 endpoint returns only `activeOrder.id`. One-field extension.
- **[Segment 12](CODE_REVIEW_ROADMAP.md#segment-12--signalr-hubs--realtime):** the [F5](#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern) toast system would naturally surface `useWebSocket`'s `onerror` / `onclose` signals to the user — today they `console.error` ([useWebSocket.ts:99, 113](frontend-customer/src/composables/useWebSocket.ts#L99-L113)) with no UI signal that realtime is broken. Segment 12 also names the QR-token query-parameter mismatch (`?token=` vs `?access_token=`) which the customer SPA produces from this segment's shell.
- **[Segment 19](CODE_REVIEW_ROADMAP.md#segment-19--customer-spa--realtime-client):** [F3](#f3--medium--consolelog-not-stripped-in-vite-build-no-no-console-lint-rule) — the WebSocket composable owns five of the nine `console.*` call sites; the segment-19 fixes (reconnect backoff, state reconciliation) need to land alongside the console-strip work or the prod log surface won't survive the redesign.
- **[Segment 20](CODE_REVIEW_ROADMAP.md#segment-20--customer-spa--i18n-enro):** the `session-error` overlay's `t('session.invalidTitle')` / `t('session.missingMessage')` / `t('session.invalidMessage')` keys ([OrderingView.vue:92-97](frontend-customer/src/view/OrderingView.vue#L92-L97)) are translation-dependent — the segment-20 audit needs to verify they exist in both RO and EN dictionaries.
- **[Segment 24](CODE_REVIEW_ROADMAP.md#segment-24--shared-contracts-shared):** [useMenu.ts:25-50](frontend-customer/src/composables/useMenu.ts#L25-L50) tolerates *both* `sortOrder` and `sort_order` (and `imageUrl` / `image_url`, `isAvailable` / `is_available`) shapes returned by the API. That tolerance is itself a Segment 24 finding — the backend's `JsonNamingPolicy.CamelCase` ([Program.cs:138-139](backend/RestaurantApi/Program.cs#L138-L139)) should make snake_case impossible, so the tolerance reflects either historical contract drift or genuine inconsistency. Worth a probe.

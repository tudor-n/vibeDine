# CLAUDE.md — frontend-admin

Staff/admin console ("Clarifi"). Vue 3 + TypeScript (strict) + Vite 5 + Pinia 3 + vue-router 5 + Tailwind 3 + `@microsoft/signalr`. Dev server: **port 3001**. **All UI copy is Romanian** — no i18n layer; write new strings in Romanian.

## Commands

| Task | Command |
|---|---|
| Dev server | `npm run dev` (vite on 0.0.0.0:3001) |
| Type check | `npm run type-check` (`vue-tsc --noEmit`) |
| Tests (all) | `npm test` (`vitest run`) |
| One test file | `npx vitest run src/__tests__/menuImportApi.spec.ts` |
| One test by name | `npx vitest run -t "test name"` |
| Build | `npm run build` |

No lint script exists (unlike frontend-customer). `tsconfig.json` **excludes `*.spec.ts`/`*.test.ts`** — test files are not type-checked by `type-check`.

## API layer

- `src/services/api-client.ts` — hand-rolled `fetch` wrapper (no axios). `fetcher` (anonymous) vs `authedFetcher` (adds `Bearer`; on 401 calls `auth.tryRefresh()` once and retries). `scoped(suffix)` prefixes `/api/restaurants/${activeRestaurantId}`. Exports grouped clients: `authApi`, `orgApi`, `waiterApi`, `profileApi`, `tablesApi`, and the main `apiClient` (orders, metrics, menu, inventory, help requests, AI directives/rules). Typed errors: `EmailNotConfirmedError`, `WaiterPendingApprovalError`.
- `src/services/menuImportApi.ts` — separate multipart/FormData client for the AI import endpoints. It normalizes the backend's **snake_case payloads to camelCase** via `normalize*` functions — when adding fields to import contracts, extend those functions or the fields are silently dropped.
- **Media URLs**: backend returns root-relative paths (`/uploads/...`). Always run them through `resolveMediaUrl` (`src/services/mediaUrl.ts`) — dev is split-origin, so raw relative paths 404.

## Realtime (SignalR)

`src/services/signalRClient.ts` → `${VITE_API_URL}/hubs/staff`, `accessTokenFactory` + `withAutomaticReconnect`. Client→server: `JoinRoleGroup(restaurantId, role)` / `LeaveRoleGroup`. Two stores own their connections (lifecycle driven from `App.vue`):

- `useWaiterCallsStore` — joins role `'WaitStaff'`; handles `HelpRequested` / `HelpAcknowledged` (+ audio chime); re-joins and re-seeds from REST on reconnect.
- `useMenuImportStore` — joins role `'admin'`; handles `MenuImportProgress` / `MenuImportCompleted` / `MenuImportFailed`, **plus a 5s REST polling fallback** in case events are missed.

## Stores (`src/stores/`, Pinia setup-style)

`useAuthStore` (tokens + user + active org/restaurant; persisted to `localStorage['sr_admin_auth']`; roles `OrgOwner` / `RestaurantAdmin` / `Waiter`), `useMenuManagementStore` (menu tree CRUD; `softDeleteProduct` only flips `isAvailable=false`), `useInventoryStore` (`lowStockCount` drives the nav badge), `useMenuImportStore` (~1200 lines: photos+PDF wizard state, job lifecycle, **resume via IndexedDB `sr_admin_import` + `localStorage['sr_admin_import_pdf']`**, auto-resumes on boot), `useWaiterCallsStore`, `useMetricsStore` (30s polling), `useDirectivesStore`, `useRecommendationDirectivesStore`, `useSimilarityRulesStore`.

## Routing & access

All routes in `src/router/index.ts`; one global `beforeEach` guard handles auth restore (`fetchMe` + org load), forced onboarding, org-picker, and **waiter restrictions** (waiters may only reach `orders-active`, `orders`, `waiter-calls`, `org-picker`, `settings-profile`). Views are `src/views/*View.vue`; Settings tabs under `src/views/Settings/`. Auth/onboarding routes render "bare" (no sidebar shell) — `App.vue` decides by route name.

## Testing conventions

Vitest + `@vue/test-utils` + MSW + jsdom; `globals: true`, no `setupFiles` (each spec is self-contained). Patterns used in `src/__tests__/`:
- HTTP-level tests: MSW `setupServer` with `// @vitest-environment node` at the top of the file; seed `useAuthStore` directly (`activeRestaurantId`, `accessToken`).
- Component/store tests: `setActivePinia(createPinia())` in `beforeEach`; `vi.mock` for stores/`signalRClient` (capture event handlers in a map, fire them manually); `vi.useFakeTimers()` + `advanceTimersByTimeAsync` for polling.

## Conventions & gotchas

- **Design system**: reuse component classes from `src/style.css` — `pill-btn`, `pill-btn-ink`, `btn-ghost`, `btn-danger`, `card-paper`, `card-white`, `field-input/-label/-error`, `page-head`, `section-title`, `eyebrow`, `alert-*`, `tag-pill`, `icon-btn` — and CSS vars `--paper`, `--ink`, `--accent`, `--lime`. No component library; custom dropdown = `BrandSelect.vue`. Dark mode: `data-theme` on `<html>` via `useTheme` (`localStorage['clarifi.admin.theme']`).
- **Brand paint-on-boot**: tenant colours only arrive from `GET .../branding`, so `main.ts` calls `hydrateBrandFromCache(auth.activeRestaurantId)` **after `app.use(pinia)` but before `app.mount()`** to repaint the last-known palette from `@shared/branding/cache` (`localStorage['clarifi.brand.<restaurantId>']`) — without it a branded tenant flashes the default palette for the length of that request. `App.vue`'s `refreshBranding()` revalidates and rewrites the cache; it stays authoritative.
- **`@shared` alias → `../shared`** (outside this package). It's configured in **three places that must stay in sync**: `vite.config.ts`, `vitest.config.ts`, `tsconfig.json`. Docker/CI builds need repo-root build context or `shared/` is missing.
- `shared/types/` has committed generated `.js`/`.d.ts` next to the `.ts` — edit only the `.ts` sources.
- Validation limits come from `@shared/types/menu-validation.ts` (`MENU_VALIDATION`, `IMPORT_MAX_FILES=10`, `IMPORT_MAX_FILE_BYTES=5MB`) and are hand-synced with the C# backend — change both sides together.
- No Vite dev proxy: the app calls `VITE_API_URL` cross-origin (default `http://localhost:5005`).
- `VITE_CUSTOMER_URL` (QR-code target in `TablesView`, default `http://localhost:3000`) is **not** in `.env.example` — easy to miss when QR codes point to the wrong host.
- vue-router is **v5** (not the usual v4 for Vue 3).

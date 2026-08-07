# Embedded QR-Phone Demo Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fully frontend-mocked demo of the diner QR experience at `http://localhost:3000/demo-vibey-x7k2`, embeddable in an iframe — hardcoded menu/branding/recs, always-on happy hour, post-order flash deal, simulated kitchen progression; no backend, no WebSocket, no bill merging, no session limits.

**Architecture:** A magic qrToken (`demo-vibey-x7k2`) recognized by the existing catch-all route. `api-client.ts` delegates every request to a fixture adapter when the session is the demo; `useWebSocket.ts` never connects; a scripter module replays the events the socket would have pushed by calling the same store actions. Spec: `docs/superpowers/specs/2026-08-07-embedded-demo-session-design.md`.

**Tech Stack:** Vue 3 + TS + Pinia (frontend-customer), vitest + jsdom (existing setup in `src/__tests__/`).

## Global Constraints

- **All work is in `frontend-customer/` (+ nothing else).** Zero backend changes.
- **NEVER run git state-changing commands** (`add`, `commit`, `push`, …) — the user manages git manually (repo policy, CLAUDE.md). Skip every "commit" step you'd normally do; just leave the working tree ready.
- `linkExpirySeconds` in fixtures MUST be `86_400` (24 h). Values > ~2.1 billion ms overflow `setTimeout`'s 32-bit delay in `useLinkExpiry.ts` and fire **immediately**, bricking the demo behind the link-expired overlay.
- Branding image URLs in fixtures MUST be `${window.location.origin}/demo/<file>.svg` (served from `public/demo/`). Data-URIs are rejected by `WelcomeView.vue`'s sanitizer (http(s)/relative only); `/`-relative paths get rewritten to the **API** origin by `resolveMediaUrl` — both silently break.
- Fixture menu content (dish names/descriptions) is Romanian-only — real menus are single-language tenant data, and the RO/EN chrome around it comes from `useLocale` untouched. Vibey fixture *reasons/summary* ARE locale-aware (the request carries `locale`).
- Money values: 2-decimal numbers; totals go through `roundMoney` (`src/utils/money.ts`) — never hand-compute float sums in fixtures/adapter.
- Demo modules must be imported into production code paths **only** via `src/demo/config.ts` (static, store-free) and **dynamic `import()`** for the adapter — the adapter imports Pinia stores, and a static import from `api-client.ts` would create a circular dependency (stores → api-client → adapter → stores).
- Run all commands from `frontend-customer/`: tests `npx vitest run <path>`, type-check `npm run type-check`, lint `npm run lint`.
- Timers in tests: use `vi.useFakeTimers()` and restore with `vi.useRealTimers()` in `afterEach`.

---

### Task 1: Demo config module

**Files:**
- Create: `frontend-customer/src/demo/config.ts`
- Test: `frontend-customer/src/__tests__/demoConfig.spec.ts`

**Interfaces:**
- Consumes: nothing (leaf module — no store imports, safe to static-import anywhere).
- Produces: `DEMO_TOKEN: string`; `isDemoToken(token: string | null | undefined): boolean`; `isDemoSession(): boolean` (true when the URL's first path segment OR `sessionStorage['qrToken']` equals `DEMO_TOKEN`).

- [ ] **Step 1: Write the failing test**

```ts
// frontend-customer/src/__tests__/demoConfig.spec.ts
import { describe, it, expect, beforeEach } from 'vitest'
import { DEMO_TOKEN, isDemoToken, isDemoSession } from '@/demo/config'

describe('demo config', () => {
  beforeEach(() => {
    sessionStorage.clear()
    window.history.replaceState(null, '', '/')
  })

  it('recognizes the demo token and nothing else', () => {
    expect(isDemoToken(DEMO_TOKEN)).toBe(true)
    expect(isDemoToken('a3f0c9d2-real-guid')).toBe(false)
    expect(isDemoToken(null)).toBe(false)
    expect(isDemoToken(undefined)).toBe(false)
    expect(isDemoToken('')).toBe(false)
  })

  it('isDemoSession is true when the URL path is the demo token', () => {
    window.history.replaceState(null, '', `/${DEMO_TOKEN}`)
    expect(isDemoSession()).toBe(true)
  })

  it('isDemoSession is true when sessionStorage qrToken is the demo token', () => {
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    expect(isDemoSession()).toBe(true)
  })

  it('isDemoSession is false for a normal session', () => {
    window.history.replaceState(null, '', '/some-real-token')
    sessionStorage.setItem('qrToken', 'some-real-token')
    expect(isDemoSession()).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/__tests__/demoConfig.spec.ts`
Expected: FAIL — cannot resolve `@/demo/config`.

- [ ] **Step 3: Write the implementation**

```ts
// frontend-customer/src/demo/config.ts
// The magic qrToken that switches the whole app into the embeddable demo
// session (see docs/superpowers/specs/2026-08-07-embedded-demo-session-design.md).
// Deliberately a token-like slug (not "/demo") so it isn't guessable on the
// production customer domain. Leaf module: NO store imports — production code
// (api-client, useWebSocket, OrderingView, main.ts) static-imports this file,
// and anything heavier would drag demo machinery into every session's bundle
// path or create circular imports.
export const DEMO_TOKEN = 'demo-vibey-x7k2'

export function isDemoToken(token: string | null | undefined): boolean {
  return token === DEMO_TOKEN
}

// True when THIS browsing context is the demo: the URL says so (fresh load,
// before any join) or the stored session does (later calls that only know the
// restaurant-scoped endpoint, e.g. menu/happy-hour fetches).
export function isDemoSession(): boolean {
  const urlToken = window.location.pathname.split('/')[1] || null
  return isDemoToken(urlToken) || isDemoToken(sessionStorage.getItem('qrToken'))
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/__tests__/demoConfig.spec.ts`
Expected: PASS (4 tests).

---

### Task 2: Demo assets + fixtures

**Files:**
- Create: `frontend-customer/public/demo/logo.svg`
- Create: `frontend-customer/public/demo/welcome.svg`
- Create: `frontend-customer/src/demo/fixtures.ts`
- Test: `frontend-customer/src/__tests__/demoFixtures.spec.ts`

**Interfaces:**
- Consumes: `DEMO_TOKEN` from `@/demo/config`; wire types from `@shared/types/*` and `@/services/api-client`.
- Produces (all consumed by Task 4's adapter and Task 3's scripter):
  - `DEMO_RESTAURANT_ID: string`, `DEMO_TABLE_ID: string`, `DEMO_GUEST_ID: string`
  - `buildDemoJoinResponse(): JoinTableResponse`
  - `demoPartyMembers(): PartyMember[]`
  - `DEMO_MENU: MenuResponse` (plain constant)
  - `buildDemoHappyHour(): ActiveHappyHour`
  - `buildDemoVibeyResponse(req: VibeyRecommendationRequest): VibeyRecommendationResponse`
  - `buildDemoFlashDealPayload(orderId: string): PendingFlashDealPayload`

- [ ] **Step 1: Create the two SVG assets**

```xml
<!-- frontend-customer/public/demo/logo.svg -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120">
  <rect width="120" height="120" rx="28" fill="#B4532A"/>
  <path d="M60 26c-14 0-25 11-25 25 0 18 25 43 25 43s25-25 25-43c0-14-11-25-25-25z" fill="#F6F1E7"/>
  <circle cx="60" cy="51" r="10" fill="#3E7C59"/>
</svg>
```

```xml
<!-- frontend-customer/public/demo/welcome.svg -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 390 844" preserveAspectRatio="xMidYMid slice">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#B4532A"/>
      <stop offset=".55" stop-color="#7A3A1E"/>
      <stop offset="1" stop-color="#2C1710"/>
    </linearGradient>
  </defs>
  <rect width="390" height="844" fill="url(#g)"/>
  <circle cx="90" cy="180" r="120" fill="#F6F1E7" opacity=".08"/>
  <circle cx="330" cy="420" r="160" fill="#3E7C59" opacity=".12"/>
  <circle cx="140" cy="660" r="140" fill="#F6F1E7" opacity=".06"/>
</svg>
```

- [ ] **Step 2: Write the failing test**

```ts
// frontend-customer/src/__tests__/demoFixtures.spec.ts
import { describe, it, expect, beforeEach } from 'vitest'
import {
  DEMO_RESTAURANT_ID,
  buildDemoJoinResponse,
  DEMO_MENU,
  buildDemoHappyHour,
  buildDemoVibeyResponse,
  buildDemoFlashDealPayload,
} from '@/demo/fixtures'

describe('demo fixtures', () => {
  beforeEach(() => sessionStorage.clear())

  it('join response has one party member, a 24h expiry, and enabled branding', () => {
    const res = buildDemoJoinResponse()
    expect(res.restaurantId).toBe(DEMO_RESTAURANT_ID)
    expect(res.partyMembers).toHaveLength(1)
    expect(res.partyMembers[0].guestId).toBe(res.guestId)
    expect(res.session?.linkExpirySeconds).toBe(86_400)
    expect(res.branding?.enabled).toBe(true)
    // Same-origin absolute URLs — pass WelcomeView's sanitizer, skip resolveMediaUrl rewriting.
    expect(res.branding?.photoUrl).toBe(`${window.location.origin}/demo/welcome.svg`)
    expect(res.branding?.logoUrl).toBe(`${window.location.origin}/demo/logo.svg`)
    expect(res.activeOrder.items).toEqual([])
  })

  it('join keeps the same draft orderId across a reload, but mints a new one after submit', () => {
    const first = buildDemoJoinResponse()
    sessionStorage.setItem('qrToken', 'demo-vibey-x7k2')
    sessionStorage.setItem('orderId', first.activeOrder.id)
    const reload = buildDemoJoinResponse()
    expect(reload.activeOrder.id).toBe(first.activeOrder.id)

    // Mark that draft as submitted (what registerSubmittedRound persists).
    sessionStorage.setItem(
      'sessionOrders',
      JSON.stringify([{ id: first.activeOrder.id, status: 'submitted', totalAmount: 50, items: [] }]),
    )
    const next = buildDemoJoinResponse()
    expect(next.activeOrder.id).not.toBe(first.activeOrder.id)
  })

  it('join restores the visit summary from sessionStorage', () => {
    sessionStorage.setItem('qrToken', 'demo-vibey-x7k2')
    sessionStorage.setItem('cumulativeTotal', '99.5')
    sessionStorage.setItem(
      'sessionOrders',
      JSON.stringify([{ id: 'x', status: 'submitted', totalAmount: 99.5, items: [] }]),
    )
    const res = buildDemoJoinResponse()
    expect(res.session?.cumulativeTotal).toBe(99.5)
    expect(res.session?.orders).toHaveLength(1)
  })

  it('menu has 4 categories, every product priced and available', () => {
    expect(DEMO_MENU.categories).toHaveLength(4)
    const products = DEMO_MENU.categories.flatMap((c) => c.products)
    expect(products.length).toBeGreaterThanOrEqual(12)
    for (const p of products) {
      expect(p.price).toBeGreaterThan(0)
      expect(p.isAvailable).toBe(true)
      expect(p.categoryId).toBeTruthy()
    }
  })

  it('happy hour is always live, priced off a real menu product, ending 1-2h out', () => {
    const hh = buildDemoHappyHour()
    const product = DEMO_MENU.categories
      .flatMap((c) => c.products)
      .find((p) => p.id === hh.productId)
    expect(product).toBeDefined()
    expect(hh.originalPrice).toBe(product!.price)
    expect(hh.discountedPrice).toBeLessThan(hh.originalPrice)
    expect(hh.endTime).toMatch(/^\d{2}:\d{2}$/)
  })

  it('vibey response excludes items already in the cart and localizes', () => {
    const pool = buildDemoVibeyResponse({ items: [], locale: 'ro' })
    expect(pool.recommendations.length).toBeGreaterThanOrEqual(2)
    const firstId = pool.recommendations[0].id
    const withCart = buildDemoVibeyResponse({
      items: [{ id: firstId, name: 'x', qty: 1 }],
      locale: 'en',
    })
    expect(withCart.recommendations.some((r) => r.id === firstId)).toBe(false)
    expect(withCart.summary).not.toBe(pool.summary) // en vs ro summary
  })

  it('flash deal payload targets the given order with a discounted real product', () => {
    const deal = buildDemoFlashDealPayload('order-123')
    expect(deal.orderId).toBe('order-123')
    expect(deal.discountPrice).toBeLessThan(deal.originalPrice)
    expect(Date.parse(deal.expiresAt)).toBeGreaterThan(Date.now())
    const product = DEMO_MENU.categories
      .flatMap((c) => c.products)
      .find((p) => p.id === deal.productId)
    expect(product).toBeDefined()
  })
})
```

- [ ] **Step 3: Run test to verify it fails**

Run: `npx vitest run src/__tests__/demoFixtures.spec.ts`
Expected: FAIL — cannot resolve `@/demo/fixtures`.

- [ ] **Step 4: Write the implementation**

```ts
// frontend-customer/src/demo/fixtures.ts
// Every piece of hardcoded data the demo session serves, typed against the
// REAL wire contracts so drift breaks `npm run type-check`. Content rules:
// dish names/descriptions are Romanian-only (menus are single-language tenant
// data); Vibey copy is locale-aware because the request carries `locale`.
import type { JoinTableResponse, PartyMember } from '@/services/api-client'
import type { MenuResponse } from '@shared/types/api-responses'
import type { VibeyRecommendationRequest } from '@shared/types/api-requests'
import type { VibeyRecommendationResponse, VibeyRec } from '@shared/types/api-responses'
import type {
  ActiveHappyHour,
  DeviceSessionOrder,
  PendingFlashDealPayload,
} from '@shared/types/models'
import { roundMoney } from '@/utils/money'

export const DEMO_RESTAURANT_ID = '00000000-0000-4000-8000-00000000d001'
export const DEMO_TABLE_ID = '00000000-0000-4000-8000-00000000d002'
export const DEMO_GUEST_ID = '00000000-0000-4000-8000-00000000d003'

// Stable product ids so Vibey/happy-hour/flash-deal fixtures can reference them.
const P = {
  bruschete: 'demo-p-01', hummus: 'demo-p-02', zacusca: 'demo-p-03',
  burger: 'demo-p-11', paste: 'demo-p-12', somon: 'demo-p-13', risotto: 'demo-p-14',
  limonada: 'demo-p-21', ipa: 'demo-p-22', cafea: 'demo-p-23', vin: 'demo-p-24',
  lava: 'demo-p-31', papanasi: 'demo-p-32', tiramisu: 'demo-p-33',
} as const

export const DEMO_MENU: MenuResponse = {
  categories: [
    {
      id: 'demo-c-1', name: 'Aperitive', sortOrder: 1, imageUrl: null,
      products: [
        { id: P.bruschete, categoryId: 'demo-c-1', name: 'Bruschete cu roșii', description: 'Pâine prăjită, roșii coapte, busuioc, ulei de măsline', price: 24.5, imageUrl: null, isAvailable: true },
        { id: P.hummus, categoryId: 'demo-c-1', name: 'Hummus cu lipie caldă', description: 'Năut cremos, tahini, boia afumată', price: 26, imageUrl: null, isAvailable: true },
        { id: P.zacusca, categoryId: 'demo-c-1', name: 'Zacuscă de casă', description: 'Rețeta bunicii, cu pâine de casă', price: 22, imageUrl: null, isAvailable: true },
      ],
    },
    {
      id: 'demo-c-2', name: 'Feluri principale', sortOrder: 2, imageUrl: null,
      products: [
        { id: P.burger, categoryId: 'demo-c-2', name: 'Burger Angus', description: 'Vită Angus, cheddar maturat, bacon crocant, cartofi wedges', price: 52, imageUrl: null, isAvailable: true },
        { id: P.paste, categoryId: 'demo-c-2', name: 'Paste carbonara', description: 'Guanciale, pecorino, gălbenuș de ou', price: 42.5, imageUrl: null, isAvailable: true },
        { id: P.somon, categoryId: 'demo-c-2', name: 'Somon la grătar', description: 'File de somon, legume de sezon, sos de lămâie', price: 64, imageUrl: null, isAvailable: true },
        { id: P.risotto, categoryId: 'demo-c-2', name: 'Risotto cu ciuperci', description: 'Orez arborio, ciuperci de pădure, parmezan', price: 46, imageUrl: null, isAvailable: true },
      ],
    },
    {
      id: 'demo-c-3', name: 'Băuturi', sortOrder: 3, imageUrl: null,
      products: [
        { id: P.limonada, categoryId: 'demo-c-3', name: 'Limonadă cu mentă', description: 'Lămâie proaspătă, mentă, miere', price: 18, imageUrl: null, isAvailable: true },
        { id: P.ipa, categoryId: 'demo-c-3', name: 'Bere artizanală IPA', description: 'IPA locală, note citrice, 5.9%', price: 22, imageUrl: null, isAvailable: true },
        { id: P.cafea, categoryId: 'demo-c-3', name: 'Cafea specialitate', description: 'Espresso dublu, origine Etiopia', price: 14, imageUrl: null, isAvailable: true },
        { id: P.vin, categoryId: 'demo-c-3', name: 'Vin alb (pahar)', description: 'Fetească regală, sec', price: 24, imageUrl: null, isAvailable: true },
      ],
    },
    {
      id: 'demo-c-4', name: 'Deserturi', sortOrder: 4, imageUrl: null,
      products: [
        { id: P.lava, categoryId: 'demo-c-4', name: 'Lava cake', description: 'Ciocolată belgiană, interior curgător, înghețată de vanilie', price: 28, imageUrl: null, isAvailable: true },
        { id: P.papanasi, categoryId: 'demo-c-4', name: 'Papanași cu smântână', description: 'Cu dulceață de afine și smântână', price: 26, imageUrl: null, isAvailable: true },
        { id: P.tiramisu, categoryId: 'demo-c-4', name: 'Tiramisu', description: 'Mascarpone, espresso, cacao', price: 25, imageUrl: null, isAvailable: true },
      ],
    },
  ],
}

function findProduct(id: string) {
  const p = DEMO_MENU.categories.flatMap((c) => c.products).find((x) => x.id === id)
  if (!p) throw new Error(`Demo fixture references unknown product ${id}`)
  return p
}

export function demoPartyMembers(): PartyMember[] {
  return [{ guestId: DEMO_GUEST_ID, displayName: 'Oaspete 1', itemCount: 0, total: 0, status: 'draft' }]
}

function readStoredOrders(): DeviceSessionOrder[] {
  try {
    const raw = sessionStorage.getItem('sessionOrders')
    return raw ? (JSON.parse(raw) as DeviceSessionOrder[]) : []
  } catch {
    return []
  }
}

// The draft orderId must survive a reload (the real backend restores the same
// draft) but must NOT be reused once that order was submitted — orderAgain()
// re-joins expecting a fresh draft. sessionOrders (persisted by
// registerSubmittedRound / the session store) records which ids are spent.
function resolveDraftOrderId(): string {
  const stored = sessionStorage.getItem('orderId')
  const spent = new Set(readStoredOrders().map((o) => o.id))
  if (stored && !spent.has(stored)) return stored
  return crypto.randomUUID()
}

export function buildDemoJoinResponse(): JoinTableResponse {
  const orders = readStoredOrders()
  const cumulativeTotal = Number(sessionStorage.getItem('cumulativeTotal')) || 0
  const origin = window.location.origin
  return {
    guestId: DEMO_GUEST_ID,
    displayName: 'Oaspete 1',
    restaurantId: DEMO_RESTAURANT_ID,
    restaurantName: 'Aroma Bistro',
    restaurantAddress: 'Str. Demo 1, București',
    organizationName: 'Aroma Group',
    tableId: DEMO_TABLE_ID,
    tableNumber: '7',
    activeOrder: { id: resolveDraftOrderId(), status: 'draft', totalAmount: 0, items: [] },
    partyMembers: demoPartyMembers(),
    branding: {
      enabled: true,
      primary: '#B4532A',
      secondary: '#3E7C59',
      background: '#F6F1E7',
      logoUrl: `${origin}/demo/logo.svg`,
      photoUrl: `${origin}/demo/welcome.svg`,
      menuBackgroundUrl: null,
      menuBackgroundStyle: null,
      presetKey: null,
      logoBackground: null,
      logoIsDark: false,
    },
    session: {
      id: '00000000-0000-4000-8000-00000000d004',
      // 24h — effectively unlimited for a demo. MUST stay < ~24.8 days:
      // useLinkExpiry uses setTimeout(seconds*1000) and a 32-bit overflow
      // fires the expiry overlay IMMEDIATELY.
      linkExpirySeconds: 86_400,
      cumulativeTotal,
      orders,
      pendingDeal: null,
    },
  }
}

// Always-on ("24/7") happy hour: −20% on the IPA. endTime is recomputed on
// every poll to ~2h from now (capped at 23:59 — secondsUntil() evaluates
// "HH:mm" against TODAY, so a wrapped past-midnight time would read as
// already over; near midnight the countdown just runs to 23:59 and the next
// poll after midnight restores the 2h window).
export function buildDemoHappyHour(): ActiveHappyHour {
  const product = findProduct(P.ipa)
  const end = new Date(Date.now() + 2 * 60 * 60 * 1000)
  const sameDay = end.getDate() === new Date().getDate()
  const hh = sameDay ? String(end.getHours()).padStart(2, '0') : '23'
  const mm = sameDay ? String(end.getMinutes()).padStart(2, '0') : '59'
  return {
    productId: product.id,
    productName: product.name,
    discountPercent: 20,
    originalPrice: product.price,
    discountedPrice: roundMoney(product.price * 0.8),
    startTime: '00:00',
    endTime: `${hh}:${mm}`,
  }
}

// Hardcoded Vibey picks with per-locale reasons. Served minus whatever is
// already in the cart, so Vibey never suggests something the guest has.
const VIBEY_POOL: Array<{ id: string; reason: { ro: string; en: string } }> = [
  { id: P.ipa, reason: { ro: 'Amăreala citrică taie perfect grăsimea burgerului.', en: 'Its citrus bitterness cuts right through a rich main.' } },
  { id: P.limonada, reason: { ro: 'Proaspătă și dulce-acrișoară — echilibrează felurile sărate.', en: 'Fresh and tangy — balances the savoury dishes.' } },
  { id: P.bruschete, reason: { ro: 'Un start ușor cât aștepți felul principal.', en: 'A light start while your main is on the way.' } },
  { id: P.lava, reason: { ro: 'Finalul clasic: ciocolată caldă după un fel consistent.', en: 'The classic finish: warm chocolate after a hearty main.' } },
]

export function buildDemoVibeyResponse(
  req: VibeyRecommendationRequest,
): VibeyRecommendationResponse {
  const inCart = new Set(req.items.map((i) => i.id))
  const recommendations: VibeyRec[] = VIBEY_POOL.filter((r) => !inCart.has(r.id))
    .slice(0, 3)
    .map((r) => {
      const p = findProduct(r.id)
      return {
        id: p.id,
        name: p.name,
        category: DEMO_MENU.categories.find((c) => c.id === p.categoryId)?.name ?? null,
        priceRon: p.price,
        reason: req.locale === 'en' ? r.reason.en : r.reason.ro,
      }
    })
  const summary =
    req.locale === 'en'
      ? 'I looked at your picks — here is what would round them off nicely.'
      : 'M-am uitat peste alegerile tale — uite ce le-ar completa de minune.'
  return { summary, recommendations }
}

// Post-order upsell: Lava cake at −25%, claimable for 2 minutes. Shape mirrors
// the FlashDealAvailable WS payload so buildFlashDealFromPayload maps it as-is.
export function buildDemoFlashDealPayload(orderId: string): PendingFlashDealPayload {
  const product = findProduct(P.lava)
  return {
    dealId: crypto.randomUUID(),
    orderId,
    productId: product.id,
    productName: product.name,
    imageUrl: null,
    originalPrice: product.price,
    discountPrice: roundMoney(product.price * 0.75),
    discountPercent: 25,
    expiresAt: new Date(Date.now() + 120_000).toISOString(),
    quantity: 1,
  }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npx vitest run src/__tests__/demoFixtures.spec.ts`
Expected: PASS (7 tests).

---

### Task 3: Demo event scripter

**Files:**
- Create: `frontend-customer/src/demo/demoScripter.ts`
- Test: `frontend-customer/src/__tests__/demoScripter.spec.ts`

**Interfaces:**
- Consumes: `buildDemoFlashDealPayload` (Task 2); stores `useOrderStatusStore`, `useFlashDealStore` (+ `buildFlashDealFromPayload`), `useHelpCallStore`.
- Produces (consumed by Task 4's adapter):
  - `startPostSubmitSequence(orderId: string): void` — schedules `preparing` @ 15 s, `ready` @ 45 s, flash deal @ 8 s.
  - `scheduleWaiterOnTheWay(): void` — clears the call-waiter cooldown @ 6 s (the `WaiterOnTheWay` equivalent).
  - `cancelDemoTimers(): void` — clears every pending timer (fresh reset / teardown).

- [ ] **Step 1: Write the failing test**

```ts
// frontend-customer/src/__tests__/demoScripter.spec.ts
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import {
  startPostSubmitSequence,
  scheduleWaiterOnTheWay,
  cancelDemoTimers,
} from '@/demo/demoScripter'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useHelpCallStore } from '@/stores/useHelpCallStore'

describe('demo scripter', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    sessionStorage.clear()
    setActivePinia(createPinia())
  })
  afterEach(() => {
    cancelDemoTimers()
    vi.useRealTimers()
  })

  it('plays out preparing → ready on this order', () => {
    const orderStatus = useOrderStatusStore()
    startPostSubmitSequence('order-1')
    expect(orderStatus.status).toBeNull()
    vi.advanceTimersByTime(15_000)
    expect(orderStatus.status).toBe('preparing')
    expect(orderStatus.orderId).toBe('order-1')
    vi.advanceTimersByTime(30_000)
    expect(orderStatus.status).toBe('ready')
  })

  it('injects the flash deal for this order after ~8s', () => {
    const flashDeal = useFlashDealStore()
    startPostSubmitSequence('order-2')
    expect(flashDeal.activeDeal).toBeNull()
    vi.advanceTimersByTime(8_000)
    expect(flashDeal.activeDeal).not.toBeNull()
    expect(flashDeal.activeDealOrderId).toBe('order-2')
    expect(flashDeal.activeDeal!.discountedPrice).toBeLessThan(flashDeal.activeDeal!.menuItem.price)
  })

  it('a new submit sequence cancels the previous one', () => {
    const orderStatus = useOrderStatusStore()
    startPostSubmitSequence('order-a')
    vi.advanceTimersByTime(5_000)
    startPostSubmitSequence('order-b')
    vi.advanceTimersByTime(15_000)
    expect(orderStatus.orderId).toBe('order-b')
  })

  it('waiter-on-the-way clears the help-call cooldown after ~6s', () => {
    const helpCall = useHelpCallStore()
    helpCall.applyCooldown(Date.now() + 120_000)
    scheduleWaiterOnTheWay()
    expect(helpCall.isCoolingDown).toBe(true)
    vi.advanceTimersByTime(6_000)
    expect(helpCall.cooldownEndsAt).toBeNull()
  })

  it('cancelDemoTimers stops everything pending', () => {
    const orderStatus = useOrderStatusStore()
    startPostSubmitSequence('order-3')
    cancelDemoTimers()
    vi.advanceTimersByTime(60_000)
    expect(orderStatus.status).toBeNull()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/__tests__/demoScripter.spec.ts`
Expected: FAIL — cannot resolve `@/demo/demoScripter`.

- [ ] **Step 3: Write the implementation**

```ts
// frontend-customer/src/demo/demoScripter.ts
// Replays, on timers, the SignalR pushes a real session would receive — by
// calling the SAME store actions the handlers in useWebSocket.ts call. Only
// ever invoked from the demo adapter (which only runs in a demo session), so
// no isDemoSession() guards here. Store imports are fine: this module is
// reached exclusively via dynamic import (api-client → adapter → here).
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import { useFlashDealStore, buildFlashDealFromPayload } from '@/stores/useFlashDealStore'
import { useHelpCallStore } from '@/stores/useHelpCallStore'
import { buildDemoFlashDealPayload } from '@/demo/fixtures'

const timers = new Set<ReturnType<typeof setTimeout>>()

function after(ms: number, fn: () => void) {
  const t = setTimeout(() => {
    timers.delete(t)
    fn()
  }, ms)
  timers.add(t)
}

export function cancelDemoTimers() {
  for (const t of timers) clearTimeout(t)
  timers.clear()
}

// The demo kitchen: flash-deal upsell at 8s (the moment you chose — right
// after the order went off), preparing at 15s, ready at 45s. A new submit
// (next round) cancels anything still pending from the previous one so an
// old round's "ready" can't stamp the new round's timeline.
export function startPostSubmitSequence(orderId: string) {
  cancelDemoTimers()
  after(8_000, () => {
    const flashDeal = useFlashDealStore()
    const payload = buildDemoFlashDealPayload(orderId)
    flashDeal.setActiveDeal(buildFlashDealFromPayload(payload), orderId, payload.quantity)
  })
  after(15_000, () => useOrderStatusStore().setStatus(orderId, 'preparing'))
  after(45_000, () => useOrderStatusStore().setStatus(orderId, 'ready'))
}

// The WaiterOnTheWay equivalent: the "waiter" acknowledges after a few seconds.
export function scheduleWaiterOnTheWay() {
  after(6_000, () => useHelpCallStore().clearCooldown())
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/__tests__/demoScripter.spec.ts`
Expected: PASS (5 tests).

Note: `useHelpCallStore.clearCooldown` and `useOrderStatusStore.setStatus` write to `sessionStorage` via jsdom — no mocking needed. If `crypto.randomUUID` is missing in the jsdom version, add `import { webcrypto } from 'node:crypto'` polyfill to the vitest setup file — but current jsdom/node 20 has it; verify before adding anything.

---

### Task 4: Demo adapter

**Files:**
- Create: `frontend-customer/src/demo/demoAdapter.ts`
- Test: `frontend-customer/src/__tests__/demoAdapter.spec.ts`

**Interfaces:**
- Consumes: fixtures (Task 2), scripter (Task 3), `useSessionStore`, `useFlashDealStore`.
- Produces (consumed by Task 5's api-client seam):
  - `handleDemoRequest(endpoint: string, options?: RequestInit): Promise<unknown>` — fixture response for a covered endpoint after 150–400 ms simulated latency; **throws** naming the endpoint for anything unmatched.
  - `handleDemoCallWaiter(): Promise<{ ok: true; cooldownEndsAt: string }>` — success + schedules the waiter ack.

- [ ] **Step 1: Write the failing test**

```ts
// frontend-customer/src/__tests__/demoAdapter.spec.ts
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { handleDemoRequest, handleDemoCallWaiter } from '@/demo/demoAdapter'
import { cancelDemoTimers } from '@/demo/demoScripter'
import { DEMO_RESTAURANT_ID, DEMO_GUEST_ID } from '@/demo/fixtures'
import { useSessionStore } from '@/stores/useSessionStore'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import type { JoinTableResponse } from '@/services/api-client'
import type { MenuResponse } from '@shared/types/api-responses'

const RID = DEMO_RESTAURANT_ID

// The adapter adds 150-400ms latency; auto-advance fake timers so awaits resolve.
describe('demo adapter', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    sessionStorage.clear()
    setActivePinia(createPinia())
  })
  afterEach(() => {
    cancelDemoTimers()
    vi.useRealTimers()
  })

  it('serves join', async () => {
    const res = (await handleDemoRequest('/api/tables/demo-vibey-x7k2/join', {
      method: 'POST',
      body: JSON.stringify({ guestId: null, deviceId: 'dev' }),
    })) as JoinTableResponse
    expect(res.guestId).toBe(DEMO_GUEST_ID)
    expect(res.partyMembers).toHaveLength(1)
  })

  it('serves party, menu, happy hour', async () => {
    const party = (await handleDemoRequest('/api/tables/demo-vibey-x7k2/party?guestId=x')) as {
      partyMembers: unknown[]
    }
    expect(party.partyMembers).toHaveLength(1)
    const menu = (await handleDemoRequest(`/api/restaurants/${RID}/menu`)) as MenuResponse
    expect(menu.categories).toHaveLength(4)
    const hh = (await handleDemoRequest(`/api/restaurants/${RID}/happy-hour`)) as {
      productId: string
    }
    expect(hh.productId).toBeTruthy()
  })

  it('serves recommendations filtered by the request cart', async () => {
    const res = (await handleDemoRequest(`/api/restaurants/${RID}/recommendations`, {
      method: 'POST',
      body: JSON.stringify({ items: [], locale: 'ro' }),
    })) as { recommendations: unknown[] }
    expect(res.recommendations.length).toBeGreaterThan(0)
  })

  it('acks cart PATCH and starts the kitchen sequence on submit', async () => {
    await handleDemoRequest(`/api/restaurants/${RID}/orders/o-1/items`, {
      method: 'PATCH',
      body: JSON.stringify({ items: [] }),
    })
    await handleDemoRequest(`/api/restaurants/${RID}/orders/o-1/submit`, { method: 'POST' })
    const orderStatus = useOrderStatusStore()
    await vi.advanceTimersByTimeAsync(15_000)
    expect(orderStatus.status).toBe('preparing')
    expect(orderStatus.orderId).toBe('o-1')
  })

  it('claim-deal bumps the visit total by the deal price', async () => {
    const session = useSessionStore()
    await handleDemoRequest(`/api/restaurants/${RID}/orders/o-2/submit`, { method: 'POST' })
    await vi.advanceTimersByTimeAsync(8_000) // deal arrives
    const before = session.cumulativeTotal
    await handleDemoRequest(`/api/restaurants/${RID}/orders/o-2/claim-deal/whatever`, {
      method: 'POST',
    })
    expect(session.cumulativeTotal).toBeGreaterThan(before)
  })

  it('call-waiter succeeds and later clears the cooldown', async () => {
    const res = await handleDemoCallWaiter()
    expect(res.ok).toBe(true)
    expect(Date.parse(res.cooldownEndsAt)).toBeGreaterThan(Date.now())
  })

  it('throws loudly on an uncovered endpoint', async () => {
    await expect(handleDemoRequest(`/api/restaurants/${RID}/uncovered-endpoint`)).rejects.toThrow(
      /uncovered-endpoint/,
    )
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/__tests__/demoAdapter.spec.ts`
Expected: FAIL — cannot resolve `@/demo/demoAdapter`.

- [ ] **Step 3: Write the implementation**

```ts
// frontend-customer/src/demo/demoAdapter.ts
// The demo session's "backend": pattern-matches the same endpoints
// api-client.ts would hit and serves fixtures with realistic latency.
// Reached ONLY via dynamic import from api-client (see Task 5) — a static
// import there would be circular (stores → api-client → adapter → stores).
// Anything unmatched throws naming the endpoint: a newly added API call must
// fail fast in the demo instead of silently half-working.
import {
  buildDemoJoinResponse,
  buildDemoHappyHour,
  buildDemoVibeyResponse,
  demoPartyMembers,
  DEMO_MENU,
} from '@/demo/fixtures'
import { startPostSubmitSequence, scheduleWaiterOnTheWay } from '@/demo/demoScripter'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { roundMoney } from '@/utils/money'
import type { VibeyRecommendationRequest } from '@shared/types/api-requests'

function latency(): Promise<void> {
  return new Promise((r) => setTimeout(r, 150 + Math.random() * 250))
}

function parseBody<T>(options?: RequestInit): T {
  return JSON.parse((options?.body as string) || '{}') as T
}

export async function handleDemoRequest(
  endpoint: string,
  options?: RequestInit,
): Promise<unknown> {
  await latency()
  const method = (options?.method || 'GET').toUpperCase()
  const path = endpoint.split('?')[0]

  if (method === 'POST' && /^\/api\/tables\/[^/]+\/join$/.test(path)) {
    return buildDemoJoinResponse()
  }
  if (method === 'GET' && /^\/api\/tables\/[^/]+\/party$/.test(path)) {
    return { partyMembers: demoPartyMembers() }
  }
  if (method === 'GET' && /^\/api\/restaurants\/[^/]+\/menu$/.test(path)) {
    return DEMO_MENU
  }
  if (method === 'GET' && /^\/api\/restaurants\/[^/]+\/happy-hour$/.test(path)) {
    return buildDemoHappyHour()
  }
  if (method === 'POST' && /^\/api\/restaurants\/[^/]+\/recommendations$/.test(path)) {
    return buildDemoVibeyResponse(parseBody<VibeyRecommendationRequest>(options))
  }
  if (method === 'PATCH' && /^\/api\/restaurants\/[^/]+\/orders\/[^/]+\/items$/.test(path)) {
    return {}
  }
  const submit = path.match(/^\/api\/restaurants\/[^/]+\/orders\/([^/]+)\/submit$/)
  if (method === 'POST' && submit) {
    // The "kitchen" takes over: status progression + the post-order flash deal.
    startPostSubmitSequence(submit[1])
    return {}
  }
  const claim = path.match(/^\/api\/restaurants\/[^/]+\/orders\/([^/]+)\/claim-deal\/[^/]+$/)
  if (method === 'POST' && claim) {
    // Mirror the server: the claimed dessert is appended to the sent order at
    // the deal price, so the visit's running total (and the sent screen, via
    // OrderingView's cumulativeTotal watcher) updates.
    const flashDeal = useFlashDealStore()
    const deal = flashDeal.activeDeal
    if (deal) {
      const price = roundMoney(deal.discountedPrice * Math.max(1, flashDeal.activeDealQuantity))
      useSessionStore().registerSubmittedRound(price, claim[1])
    }
    return {}
  }

  throw new Error(
    `[demo] Unhandled endpoint in demo session: ${method} ${endpoint} — add it to demoAdapter.ts`,
  )
}

export async function handleDemoCallWaiter(): Promise<{ ok: true; cooldownEndsAt: string }> {
  await latency()
  scheduleWaiterOnTheWay()
  return { ok: true, cooldownEndsAt: new Date(Date.now() + 120_000).toISOString() }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/__tests__/demoAdapter.spec.ts`
Expected: PASS (7 tests).

Note on the claim test: `registerSubmittedRound` appends a `sessionOrders` entry with the same order id as the submitted round — a harmless demo-only artifact (the id-reuse guard in `resolveDraftOrderId` treats it as spent either way).

---

### Task 5: api-client seam

**Files:**
- Modify: `frontend-customer/src/services/api-client.ts` (the `fetcher` function ~line 74 and `callWaiter` ~line 160)
- Test: `frontend-customer/src/__tests__/apiClientDemo.spec.ts`

**Interfaces:**
- Consumes: `isDemoSession` (Task 1, static import); `handleDemoRequest`/`handleDemoCallWaiter` (Task 4, **dynamic import only**).
- Produces: unchanged `apiClient` surface — every existing call site works in both modes.

- [ ] **Step 1: Write the failing test**

```ts
// frontend-customer/src/__tests__/apiClientDemo.spec.ts
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { apiClient, setRestaurantId } from '@/services/api-client'
import { DEMO_TOKEN } from '@/demo/config'
import { DEMO_RESTAURANT_ID, DEMO_TABLE_ID } from '@/demo/fixtures'
import { cancelDemoTimers } from '@/demo/demoScripter'

describe('api-client demo seam', () => {
  beforeEach(() => {
    sessionStorage.clear()
    setActivePinia(createPinia())
    vi.stubGlobal('fetch', vi.fn())
  })
  afterEach(() => {
    cancelDemoTimers()
    vi.unstubAllGlobals()
  })

  it('demo session: joinTable is served from fixtures, fetch is never called', async () => {
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    const res = await apiClient.joinTable(DEMO_TOKEN, null, 'device-1')
    expect(res.restaurantId).toBe(DEMO_RESTAURANT_ID)
    expect(fetch).not.toHaveBeenCalled()
  })

  it('demo session: menu and callWaiter bypass the network too', async () => {
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    setRestaurantId(DEMO_RESTAURANT_ID)
    const menu = await apiClient.getMenu()
    expect(menu.categories.length).toBeGreaterThan(0)
    const waiter = await apiClient.callWaiter(DEMO_TABLE_ID)
    expect(waiter.ok).toBe(true)
    expect(fetch).not.toHaveBeenCalled()
  })

  it('normal session: fetch is still used', async () => {
    ;(fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ categories: [] }),
    })
    setRestaurantId('real-restaurant')
    await apiClient.getMenu()
    expect(fetch).toHaveBeenCalledOnce()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/__tests__/apiClientDemo.spec.ts`
Expected: FAIL — the first two tests hit the stubbed `fetch` (mock returns undefined → TypeError) instead of fixtures.

- [ ] **Step 3: Add the seam**

In `frontend-customer/src/services/api-client.ts`, add to the imports:

```ts
import { isDemoSession } from '@/demo/config'
```

Change `fetcher` — add the demo branch as its first statement:

```ts
async function fetcher<T>(endpoint: string, options?: RequestInit): Promise<T> {
  // Demo session (magic qrToken): every request is served from local fixtures —
  // no backend exists. Dynamic import so (a) demo code stays out of the normal
  // session's path and (b) no circular import (the adapter imports Pinia
  // stores, which import this module).
  if (isDemoSession()) {
    const { handleDemoRequest } = await import('@/demo/demoAdapter')
    return (await handleDemoRequest(endpoint, options)) as T
  }
  const response = await fetch(`${BASE_URL}${endpoint}`, {
    // ... existing body unchanged
```

Change `callWaiter` — add the same branch as its first statement (it bypasses `fetcher` to preserve 429 payloads, so it needs its own seam):

```ts
  callWaiter: async (
    tableId: string,
  ): Promise<
    | { ok: true; cooldownEndsAt: string }
    | { ok: false; status: number; retryAfterSeconds?: number; message?: string }
  > => {
    if (isDemoSession()) {
      const { handleDemoCallWaiter } = await import('@/demo/demoAdapter')
      return handleDemoCallWaiter()
    }
    const res = await fetch(
      // ... existing body unchanged
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/__tests__/apiClientDemo.spec.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Run the whole suite to check nothing regressed**

Run: `npx vitest run`
Expected: all specs pass (including the 5 pre-existing ones).

---

### Task 6: useWebSocket seam

**Files:**
- Modify: `frontend-customer/src/composables/useWebSocket.ts` (the `connect()` function, ~line 40)
- Test: `frontend-customer/src/__tests__/useWebSocketDemo.spec.ts`

**Interfaces:**
- Consumes: `isDemoToken` (Task 1); `sessionStore.qrToken`.
- Produces: unchanged composable surface; in a demo session `connect()` is a no-op — no socket, no reconnect loop.

- [ ] **Step 1: Write the failing test**

```ts
// frontend-customer/src/__tests__/useWebSocketDemo.spec.ts
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick } from 'vue'
import { createPinia } from 'pinia'
import { useWebSocket } from '@/composables/useWebSocket'
import { useSessionStore } from '@/stores/useSessionStore'
import { DEMO_TOKEN } from '@/demo/config'

// useWebSocket registers onUnmounted, so it must run inside a component.
function mountWithWebSocket() {
  const app = createApp(
    defineComponent({
      setup() {
        useWebSocket()
        return () => h('div')
      },
    }),
  )
  app.use(createPinia())
  const el = document.createElement('div')
  app.mount(el)
  return app
}

describe('useWebSocket demo guard', () => {
  let apps: ReturnType<typeof mountWithWebSocket>[]
  beforeEach(() => {
    sessionStorage.clear()
    apps = []
    vi.stubGlobal('WebSocket', vi.fn(() => ({ close: vi.fn(), readyState: 3 })))
  })
  afterEach(() => {
    apps.forEach((a) => a.unmount())
    vi.unstubAllGlobals()
  })

  it('never opens a socket for the demo session', async () => {
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    sessionStorage.setItem('tableId', 'demo-table')
    apps.push(mountWithWebSocket())
    await nextTick()
    expect(WebSocket).not.toHaveBeenCalled()
  })

  it('still opens a socket for a real session', async () => {
    sessionStorage.setItem('qrToken', 'real-token')
    sessionStorage.setItem('tableId', 'real-table')
    apps.push(mountWithWebSocket())
    await nextTick()
    expect(WebSocket).toHaveBeenCalledOnce()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/__tests__/useWebSocketDemo.spec.ts`
Expected: the demo test FAILS (`WebSocket` was called); the real-session test passes.

- [ ] **Step 3: Add the guard**

In `frontend-customer/src/composables/useWebSocket.ts`, add to the imports:

```ts
import { isDemoToken } from '@/demo/config'
```

Add as the first line of `connect()`:

```ts
  function connect() {
    // Demo session: there is no backend and no hub — the demo scripter replays
    // the pushes instead (src/demo/demoScripter.ts). Bail before creating a
    // socket so there's also no 5s reconnect loop hammering a dead URL.
    if (isDemoToken(sessionStore.qrToken)) return
    if (socket) return  // already connected, don't check tableId here
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/__tests__/useWebSocketDemo.spec.ts`
Expected: PASS (2 tests).

---

### Task 7: OrderingView + main.ts seams, full verification

**Files:**
- Modify: `frontend-customer/src/view/OrderingView.vue` (party poll setup, ~line 232)
- Modify: `frontend-customer/src/main.ts` (`?fresh=1` reset, before `hydrateBrandFromCache`)
- No new unit test (template-orchestration changes; covered by type-check + the manual runbook below)

**Interfaces:**
- Consumes: `isDemoToken`, `DEMO_TOKEN` (Task 1).
- Produces: the finished demo — `http://localhost:3000/demo-vibey-x7k2` (+ `?fresh=1` for a clean run).

- [ ] **Step 1: Skip the party poll in OrderingView**

In `frontend-customer/src/view/OrderingView.vue`, add to the imports:

```ts
import { isDemoToken } from '@/demo/config'
```

Replace the poll + listener setup at the end of `onMounted` (currently `partyPoll = setInterval(...)` and the `addEventListener` line):

```ts
  // Demo session: no party exists and no server wants heartbeats — skip the
  // poll and the refocus beat entirely (the WebSocket is already skipped in
  // useWebSocket, and the scripter replays the kitchen's pushes).
  if (!isDemoToken(sessionStore.qrToken)) {
    partyPoll = setInterval(() => guestStore.refreshParty(sessionStore.qrToken), PARTY_POLL_MS)
    document.addEventListener('visibilitychange', onVisibilityChange)
  }
```

(The `onUnmounted`/link-expiry teardowns already null-check `partyPoll` and `removeEventListener` is a safe no-op — no changes needed there.)

- [ ] **Step 2: Add the `?fresh=1` reset in main.ts**

In `frontend-customer/src/main.ts`, after the `qrTokenFromUrl` definition and **before** the `hydrateBrandFromCache(...)` call:

```ts
import { isDemoToken } from './demo/config'

// Embedding pages can force a clean demo run with ?fresh=1: wipe everything
// the demo persisted (screen, cart, totals, guest identity, brand cache) so
// the next visitor starts from the language screen. Must run before
// hydrateBrandFromCache AND before any store module reads sessionStorage —
// i.e. right here, before the app is created.
const bootToken = qrTokenFromUrl()
if (isDemoToken(bootToken) && new URLSearchParams(window.location.search).has('fresh')) {
  sessionStorage.clear()
  try {
    localStorage.removeItem(`party.guest.${bootToken}`)
    localStorage.removeItem(`clarifi.brand.${bootToken}`)
  } catch {
    // private mode — nothing persisted anyway
  }
}

hydrateBrandFromCache(bootToken)
```

(Replace the existing bare `hydrateBrandFromCache(qrTokenFromUrl())` call with this block; keep everything else as is.)

- [ ] **Step 3: Type-check, lint, full test suite**

Run (from `frontend-customer/`): `npm run type-check`, then `npm run lint`, then `npx vitest run`
Expected: all three clean. Fix anything they flag before proceeding.

- [ ] **Step 4: Manual runbook — verify the demo end-to-end**

Run: `npm run dev` (port 3000; no backend, no docker needed) and open `http://localhost:3000/demo-vibey-x7k2?fresh=1`. Verify:

1. Terracotta/cream "Aroma Bistro" branding paints (logo + welcome photo on the welcome screen; no theme toggle — branded sessions hide it).
2. Welcome → categories (4) → a menu with priced items; add 2-3 items.
3. Happy-hour popup appears (−20% IPA) with a live countdown; the IPA shows the discounted price in the cart.
4. Cart: no "Merge our bills" UI anywhere; Vibey teaser present; tapping "Da, arată-mi" shows hardcoded recs (none already in cart); adding one badges it.
5. Send order → confirmation; ~8 s later the flash deal (Lava cake −25%) pops; claim it → total on the sent screen increases by 21.00.
6. Timeline advances: preparing ~15 s, ready ~45 s after submit.
7. Call-waiter button: tap → cooldown starts, clears itself after ~6 s (the "on the way" ack).
8. Reload without `?fresh=1` → resumes in place; with `?fresh=1` → starts over.
9. DevTools network tab: no requests to `localhost:5005`, no WebSocket connections.
10. Iframe check: create a scratch HTML file containing `<iframe src="http://localhost:3000/demo-vibey-x7k2" style="width:390px;height:844px;border:0"></iframe>`, open it, confirm the demo renders and is interactive inside the frame.

- [ ] **Step 5: Report**

Summarize verification results (including any deviations) to the user. Do NOT commit — the user manages git.

---

## Self-Review Notes

- Spec coverage: entry/URL (T1), fixtures/branding/menu/happy-hour/Vibey/flash-deal (T2), scripted kitchen + waiter ack (T3), adapter + fail-fast unmatched endpoint + claim bump (T4), api-client seam incl. `callWaiter` (T5), WebSocket skip (T6), party-poll skip + `?fresh=1` + iframe/manual verification (T7). Merging/limits absent by construction — no task needed (T2's single-member party + 24 h expiry).
- The spec's "cancel scripter timers on unmount" is covered by `cancelDemoTimers` + the guard that each new submit cancels prior timers; OrderingView teardown intentionally doesn't call it (a backgrounded iframe keeping its "kitchen" running matches real behavior, and page unload kills timers anyway).
- Type consistency checked: `startPostSubmitSequence`/`scheduleWaiterOnTheWay`/`cancelDemoTimers` (T3↔T4), `handleDemoRequest`/`handleDemoCallWaiter` (T4↔T5), `isDemoToken`/`isDemoSession`/`DEMO_TOKEN` (T1↔T5/T6/T7), fixture builders (T2↔T4).

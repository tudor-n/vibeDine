// @vitest-environment node
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createPinia, setActivePinia } from 'pinia'

import { useAuthStore } from '@/stores/useAuthStore'
import type { WaiterFloorResponse, WaiterOrderDto, WaiterSessionDto } from '@shared/types/waiter'

/**
 * HTTP contract for `services/waiterApi.ts` — the frozen functions of the
 * Waiter Mode REST surface, all under `/api/restaurants/{rid}/waiter`.
 *
 * The module path is still assembled at runtime: it predates the module landing
 * (a statically analysable import of a missing file failed the whole file at
 * collection) and keeping it costs nothing now that the suites run for real.
 */
const WAITER_API_MODULE = ['@/services', 'waiterApi'].join('/')

const loadWaiterApi = async () =>
  (await import(/* @vite-ignore */ WAITER_API_MODULE)) as unknown as Record<
    string,
    (...args: never[]) => Promise<unknown>
  >

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// api-client.ts freezes its BASE_URL when the module loads, and the static
// imports above pull it in transitively — so the env override must run before
// any import executes. vi.hoisted is the only pre-import hook vitest offers.
const baseUrl = vi.hoisted(() => {
  import.meta.env.VITE_API_URL = 'http://api.test'
  return 'http://api.test'
})
const restaurantId = 'rest-1'
const waiterPrefix = `${baseUrl}/api/restaurants/${restaurantId}/waiter`

beforeEach(() => {
  setActivePinia(createPinia())
  const auth = useAuthStore()
  auth.activeRestaurantId = restaurantId
  auth.accessToken = 'test-token'
})

const session = (over: Partial<WaiterSessionDto> = {}): WaiterSessionDto => ({
  id: 's-1',
  tableId: 't-1',
  tableNumber: '12',
  waiterUserId: 'u-1',
  waiterName: 'Ana Popescu',
  status: 'open',
  openedAt: '2026-08-05T18:00:00Z',
  closedAt: null,
  closedReason: null,
  paymentMethod: null,
  cashAmount: null,
  cardAmount: null,
  totalAmount: null,
  activeOrderCount: 0,
  cumulativeTotal: 0,
  ...over,
})

const order = (over: Partial<WaiterOrderDto> = {}): WaiterOrderDto => ({
  id: 'o-1',
  orderNumber: null,
  status: 'draft',
  source: 'waiter',
  placedByUserId: 'u-1',
  totalAmount: 0,
  createdAt: '2026-08-05T18:05:00Z',
  items: [],
  ...over,
})

describe('waiterApi — URL building (scoped to the active restaurant)', () => {
  it('getFloor GETs /waiter/floor', async () => {
    const floor: WaiterFloorResponse = { zones: [], tables: [] }
    server.use(http.get(`${waiterPrefix}/floor`, () => HttpResponse.json(floor)))

    const api = await loadWaiterApi()
    await expect(api.getFloor()).resolves.toEqual(floor)
  })

  it('sends the bearer token on every call', async () => {
    let seen: string | null = null
    server.use(
      http.get(`${waiterPrefix}/my-sessions`, ({ request }) => {
        seen = request.headers.get('Authorization')
        return HttpResponse.json([])
      }),
    )

    const api = await loadWaiterApi()
    await api.getMySessions()
    expect(seen).toBe('Bearer test-token')
  })

  it('getSession GETs /waiter/sessions/{id}', async () => {
    server.use(
      http.get(`${waiterPrefix}/sessions/s-1`, () =>
        HttpResponse.json({ session: session(), orders: [], cumulativeTotal: 0 }),
      ),
    )

    const api = await loadWaiterApi()
    const detail = (await api.getSession('s-1' as never)) as { cumulativeTotal: number }
    expect(detail.cumulativeTotal).toBe(0)
  })

  it('getUnclaimedOrders GETs /waiter/unclaimed-orders', async () => {
    server.use(
      http.get(`${waiterPrefix}/unclaimed-orders`, () =>
        HttpResponse.json([
          {
            id: 'o-9',
            orderNumber: 42,
            status: 'submitted',
            source: 'qr',
            tableId: 't-1',
            tableNumber: '12',
            totalAmount: 31,
            createdAt: '2026-08-05T18:10:00Z',
            items: [],
          },
        ]),
      ),
    )

    const api = await loadWaiterApi()
    const feed = (await api.getUnclaimedOrders()) as { tableNumber: string; source: string }[]
    expect(feed).toHaveLength(1)
    expect(feed[0].tableNumber).toBe('12')
    expect(feed[0].source).toBe('qr')
  })

  it('getStaff GETs /waiter/staff (transfer targets)', async () => {
    server.use(
      http.get(`${waiterPrefix}/staff`, () =>
        HttpResponse.json([{ userId: 'u-2', fullName: 'Bogdan Ionescu', role: 'Waiter' }]),
      ),
    )

    const api = await loadWaiterApi()
    const staff = (await api.getStaff()) as { userId: string }[]
    expect(staff[0].userId).toBe('u-2')
  })
})

describe('waiterApi — claim semantics', () => {
  it('claimTable POSTs and resolves the new session on 201', async () => {
    server.use(
      http.post(`${waiterPrefix}/tables/t-1/claim`, () =>
        HttpResponse.json(session(), { status: 201 }),
      ),
    )

    const api = await loadWaiterApi()
    const claimed = (await api.claimTable('t-1' as never)) as WaiterSessionDto
    expect(claimed.status).toBe('open')
    expect(claimed.waiterName).toBe('Ana Popescu')
  })

  it('surfaces the 409 already-claimed body (message names the owner)', async () => {
    server.use(
      http.post(`${waiterPrefix}/tables/t-1/claim`, () =>
        HttpResponse.json(
          {
            message: 'Masa este deja preluată de Bogdan Ionescu.',
            session: session({ waiterUserId: 'u-2', waiterName: 'Bogdan Ionescu' }),
          },
          { status: 409 },
        ),
      ),
    )

    const api = await loadWaiterApi()
    await expect(api.claimTable('t-1' as never)).rejects.toThrow(/deja preluată/)
  })
})

describe('waiterApi — session lifecycle', () => {
  it('transferSession POSTs { toUserId }', async () => {
    let body: unknown = null
    server.use(
      http.post(`${waiterPrefix}/sessions/s-1/transfer`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(session({ waiterUserId: 'u-2', waiterName: 'Bogdan Ionescu' }))
      }),
    )

    const api = await loadWaiterApi()
    await api.transferSession('s-1' as never, 'u-2' as never)
    expect(body).toEqual({ toUserId: 'u-2' })
  })

  it('closeSession POSTs the payment payload and never a restaurantId', async () => {
    let body: Record<string, unknown> = {}
    server.use(
      http.post(`${waiterPrefix}/sessions/s-1/close`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(
          session({
            status: 'closed',
            closedReason: 'paid',
            paymentMethod: 'mixed',
            cashAmount: 100,
            cardAmount: 87.5,
            totalAmount: 187.5,
          }),
        )
      }),
    )

    const api = await loadWaiterApi()
    const closed = (await api.closeSession('s-1' as never, {
      paymentMethod: 'mixed',
      cashAmount: 100,
      cardAmount: 87.5,
    } as never)) as WaiterSessionDto
    expect(body).toEqual({ paymentMethod: 'mixed', cashAmount: 100, cardAmount: 87.5 })
    expect(body.restaurantId).toBeUndefined()
    expect(closed.totalAmount).toBe(187.5)
  })

  it('surfaces the 409 raised while an order is still in flight to the kitchen', async () => {
    server.use(
      http.post(`${waiterPrefix}/sessions/s-1/close`, () =>
        HttpResponse.json(
          { message: 'Nu poți închide nota cât o comandă este pe drum spre bucătărie.' },
          { status: 409 },
        ),
      ),
    )

    const api = await loadWaiterApi()
    await expect(
      api.closeSession('s-1' as never, { paymentMethod: 'cash' } as never),
    ).rejects.toThrow(/bucătărie/)
  })

  it('forceReleaseSession POSTs /force-release (manager only)', async () => {
    server.use(
      http.post(`${waiterPrefix}/sessions/s-1/force-release`, () =>
        HttpResponse.json(
          session({ status: 'closed', closedReason: 'force_released', paymentMethod: null }),
        ),
      ),
    )

    const api = await loadWaiterApi()
    const released = (await api.forceReleaseSession('s-1' as never)) as WaiterSessionDto
    expect(released.closedReason).toBe('force_released')
    expect(released.paymentMethod).toBeNull()
  })
})

describe('waiterApi — orders', () => {
  it('createOrder POSTs /waiter/tables/{id}/orders and returns a draft', async () => {
    server.use(
      http.post(`${waiterPrefix}/tables/t-1/orders`, () =>
        HttpResponse.json(order(), { status: 201 }),
      ),
    )

    const api = await loadWaiterApi()
    const draft = (await api.createOrder('t-1' as never)) as WaiterOrderDto
    expect(draft.status).toBe('draft')
    expect(draft.source).toBe('waiter')
  })

  it('updateOrderItems PATCHes the full item list (omission removes)', async () => {
    let body: unknown = null
    server.use(
      http.patch(`${waiterPrefix}/orders/o-1/items`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(order({ totalAmount: 49 }))
      }),
    )

    const api = await loadWaiterApi()
    await api.updateOrderItems('o-1' as never, [
      { productId: 'p-1', quantity: 2, notes: 'fără smântână' },
    ] as never)
    expect(body).toEqual({ items: [{ productId: 'p-1', quantity: 2, notes: 'fără smântână' }] })
  })

  it('surfaces cap violations from the backend (items / quantity / notes)', async () => {
    server.use(
      http.patch(`${waiterPrefix}/orders/o-1/items`, () =>
        HttpResponse.json({ message: 'Comanda poate avea cel mult 100 de produse.' }, { status: 400 }),
      ),
    )

    const api = await loadWaiterApi()
    await expect(api.updateOrderItems('o-1' as never, [] as never)).rejects.toThrow(/cel mult 100/)
  })

  it('rejects editing a non-draft order with the backend 409', async () => {
    server.use(
      http.patch(`${waiterPrefix}/orders/o-1/items`, () =>
        HttpResponse.json({ message: 'Comanda a fost deja trimisă.' }, { status: 409 }),
      ),
    )

    const api = await loadWaiterApi()
    await expect(api.updateOrderItems('o-1' as never, [] as never)).rejects.toThrow(/deja trimisă/)
  })

  it('submitOrder returns the dispatched order summary', async () => {
    server.use(
      http.post(`${waiterPrefix}/orders/o-1/submit`, () =>
        HttpResponse.json({
          orderId: 'o-1',
          orderNumber: 41,
          status: 'submitted',
          totalAmount: 49,
        }),
      ),
    )

    const api = await loadWaiterApi()
    const res = (await api.submitOrder('o-1' as never)) as { orderNumber: number; status: string }
    // Waiter submit dispatches straight through — it never rests in pending_dispatch.
    expect(res.status).toBe('submitted')
    expect(res.orderNumber).toBe(41)
  })

  it('cancelOrder returns { id, status }', async () => {
    server.use(
      http.post(`${waiterPrefix}/orders/o-1/cancel`, () =>
        HttpResponse.json({ id: 'o-1', status: 'cancelled' }),
      ),
    )

    const api = await loadWaiterApi()
    const res = (await api.cancelOrder('o-1' as never)) as { status: string }
    expect(res.status).toBe('cancelled')
  })
})

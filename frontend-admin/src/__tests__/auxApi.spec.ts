// @vitest-environment node
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createPinia, setActivePinia } from 'pinia'

import { auxApi } from '@/services/api-client'
import { useAuthStore } from '@/stores/useAuthStore'

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// api-client captures its BASE_URL at module scope, which runs (via import
// hoisting) before this spec body could override VITE_API_URL — so mirror the
// exact same fallback computation instead of reassigning the env var.
const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005'
const baseUrl = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl
const restaurantId = 'rest-1'

beforeEach(() => {
  setActivePinia(createPinia())
  const auth = useAuthStore()
  auth.activeRestaurantId = restaurantId
  auth.accessToken = 'test-token'
})

describe('auxApi.list', () => {
  it('GETs the aux-tables endpoint with the bearer token and returns the rows', async () => {
    let authHeader: string | null = null
    server.use(
      http.get(`${baseUrl}/api/restaurants/${restaurantId}/admin/aux-tables`, ({ request }) => {
        authHeader = request.headers.get('authorization')
        return HttpResponse.json([
          {
            waiterId: 'w-1',
            waiterName: 'Ana Ospătar',
            avatarUrl: null,
            isActiveStaff: true,
            auxTableId: 'aux-1',
            auxTableNumber: 'AUX · Ana Ospătar',
            orders: [
              {
                id: 'o-1',
                orderNumber: 12,
                status: 'submitted',
                totalAmount: 24,
                createdAt: '2026-07-21T10:00:00Z',
                movedToAuxAt: '2026-07-21T10:05:00Z',
                movedFromTableId: 't-1',
                movedFromTableNumber: 'M1',
                claimedByWaiterId: 'w-1',
                claimedByName: 'Ana Ospătar',
                items: [
                  { id: 'i-1', productName: 'Ciorbă de burtă', quantity: 2, unitPrice: 12, subtotal: 24 },
                ],
              },
            ],
          },
        ])
      }),
    )

    const rows = await auxApi.list()

    expect(authHeader).toBe('Bearer test-token')
    expect(rows).toHaveLength(1)
    expect(rows[0].waiterId).toBe('w-1')
    expect(rows[0].auxTableNumber).toBe('AUX · Ana Ospătar')
    expect(rows[0].orders[0].movedFromTableNumber).toBe('M1')
    expect(rows[0].orders[0].items[0].productName).toBe('Ciorbă de burtă')
  })
})

describe('auxApi.moveOrderToAux', () => {
  it('POSTs to move-to-aux and serializes the payload exactly', async () => {
    let body: unknown = null
    server.use(
      http.post(
        `${baseUrl}/api/restaurants/${restaurantId}/admin/orders/o-1/move-to-aux`,
        async ({ request }) => {
          body = await request.json()
          return HttpResponse.json({
            movedOrderId: 'o-1',
            movedOrderNumber: 12,
            auxTableId: 'aux-1',
            auxTableNumber: 'AUX · Ana Ospătar',
            auxWaiterId: 'w-1',
            auxWaiterName: 'Ana Ospătar',
            sourceOrderId: 'o-1',
            wholeOrderMoved: true,
          })
        },
      ),
    )

    const res = await auxApi.moveOrderToAux('o-1', {
      targetWaiterId: 'w-1',
      items: [{ orderItemId: 'i-1', quantity: 2 }],
    })

    expect(body).toEqual({
      targetWaiterId: 'w-1',
      items: [{ orderItemId: 'i-1', quantity: 2 }],
    })
    expect(res.wholeOrderMoved).toBe(true)
    expect(res.auxTableId).toBe('aux-1')
  })
})

describe('auxApi.moveToTable', () => {
  it('posts { tableId } to the move-to-table endpoint', async () => {
    let body: unknown = null
    server.use(
      http.post(
        `${baseUrl}/api/restaurants/${restaurantId}/admin/aux-tables/orders/o-1/move-to-table`,
        async ({ request }) => {
          body = await request.json()
          return HttpResponse.json({
            orderId: 'o-1',
            orderNumber: 12,
            tableId: 't-9',
            tableNumber: 'M9',
          })
        },
      ),
    )

    const res = await auxApi.moveToTable('o-1', 't-9')

    expect(body).toEqual({ tableId: 't-9' })
    expect(res.tableId).toBe('t-9')
  })
})

describe('auxApi.reassign', () => {
  it('posts { waiterId } to the reassign endpoint', async () => {
    let body: unknown = null
    server.use(
      http.post(
        `${baseUrl}/api/restaurants/${restaurantId}/admin/aux-tables/orders/o-1/reassign`,
        async ({ request }) => {
          body = await request.json()
          return HttpResponse.json({
            orderId: 'o-1',
            orderNumber: 12,
            auxTableId: 'aux-2',
            auxTableNumber: 'AUX · Bogdan Ospătar',
            auxWaiterId: 'w-2',
            auxWaiterName: 'Bogdan Ospătar',
          })
        },
      ),
    )

    const res = await auxApi.reassign('o-1', 'w-2')

    expect(body).toEqual({ waiterId: 'w-2' })
    expect(res.auxWaiterId).toBe('w-2')
  })
})

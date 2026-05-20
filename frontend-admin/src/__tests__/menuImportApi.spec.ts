// @vitest-environment node
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createPinia, setActivePinia } from 'pinia'

import { menuImportApi } from '@/services/menuImportApi'
import { useAuthStore } from '@/stores/useAuthStore'
import type { BulkCreateProductsRequest, MenuScanResponse } from '@shared/types/menu-import'

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const baseUrl = 'http://api.test'
const restaurantId = 'rest-1'
import.meta.env.VITE_API_URL = baseUrl

beforeEach(() => {
  setActivePinia(createPinia())
  const auth = useAuthStore()
  auth.activeRestaurantId = restaurantId
  auth.accessToken = 'test-token'
})

describe('menuImportApi.scan', () => {
  it('POSTs photos as multipart and parses the response', async () => {
    server.use(
      http.post(`${baseUrl}/api/restaurants/${restaurantId}/admin/menu/scan`, async ({ request }) => {
        const form = await request.formData()
        const files = form.getAll('photos')
        expect(files).toHaveLength(2)
        return HttpResponse.json<MenuScanResponse>({
          items: [{ name: 'Ciorbă', price: 18, sourcePage: 0, sourceIndex: 0, categoryHint: 'Ciorbe' }],
          failedPages: [],
          model: 'fake',
          schemaVersion: 1,
        })
      }),
    )

    const result = await menuImportApi.scan([
      new File([new Uint8Array([1])], 'a.png', { type: 'image/png' }),
      new File([new Uint8Array([2])], 'b.png', { type: 'image/png' }),
    ])
    expect(result.items[0].name).toBe('Ciorbă')
    expect(result.items[0].categoryHint).toBe('Ciorbe')
  })

  it('throws a typed error on 5xx', async () => {
    server.use(
      http.post(`${baseUrl}/api/restaurants/${restaurantId}/admin/menu/scan`, () =>
        HttpResponse.json({ message: 'down' }, { status: 502 }),
      ),
    )
    await expect(
      menuImportApi.scan([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })]),
    ).rejects.toThrowError(/scan/i)
  })
})

describe('menuImportApi.bulkCreate', () => {
  it('POSTs JSON and parses response', async () => {
    server.use(
      http.post(`${baseUrl}/api/restaurants/${restaurantId}/admin/menu/products/bulk`, async ({ request }) => {
        const body = (await request.json()) as BulkCreateProductsRequest
        expect(body.items).toHaveLength(1)
        return HttpResponse.json([
          { id: 'p1', categoryId: 'c1', categoryName: 'Aperitive', name: 'Ciorbă', description: '', price: 18, imageUrl: null, isAvailable: true, sortOrder: 0 },
        ])
      }),
    )
    const created = await menuImportApi.bulkCreate({
      items: [{ name: 'Ciorbă', price: 18, categoryId: 'c1' }],
      newCategories: [],
    })
    expect(created).toHaveLength(1)
  })

  it('throws with parsed missingCategoryIds on 409', async () => {
    server.use(
      http.post(`${baseUrl}/api/restaurants/${restaurantId}/admin/menu/products/bulk`, () =>
        HttpResponse.json({ missingCategoryIds: ['c-gone'] }, { status: 409 }),
      ),
    )
    await expect(
      menuImportApi.bulkCreate({
        items: [{ name: 'X', price: 1, categoryId: 'c-gone' }],
        newCategories: [],
      }),
    ).rejects.toMatchObject({ status: 409, missingCategoryIds: ['c-gone'] })
  })
})

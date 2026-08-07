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

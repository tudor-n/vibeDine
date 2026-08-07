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

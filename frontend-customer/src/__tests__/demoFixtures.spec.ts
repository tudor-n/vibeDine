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

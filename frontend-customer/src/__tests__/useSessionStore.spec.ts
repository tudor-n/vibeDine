// Regression: branding images (welcome photo, logo, menu background) must
// survive a mid-session reload. A reload with a stored screen skips the boot
// splash and renders immediately, so `session.branding` has to be restored from
// sessionStorage on store init — otherwise the diner sees a default-looking
// screen until /join answers, then the design pops in (the "closed Chrome,
// reopened, bare starter then design" bug).
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { RestaurantBranding, PendingFlashDealPayload, DeviceSessionSummary } from '@shared/types/models'

const { joinMock } = vi.hoisted(() => ({ joinMock: vi.fn() }))

// The session store only uses the guest store for join(); mock it so tests
// exercise the persistence logic without the network.
vi.mock('@/stores/useGuestStore', () => ({
  useGuestStore: () => ({ join: joinMock }),
}))

import { useSessionStore } from '@/stores/useSessionStore'

const BRANDING: RestaurantBranding = {
  enabled: true,
  primary: '#8C4A2F',
  secondary: '#2F4A3C',
  background: '#1B1814',
  logoUrl: 'http://localhost:5005/uploads/branding/rid/logo.png',
  photoUrl: 'http://localhost:5005/uploads/branding/rid/photo.jpg',
  menuBackgroundUrl: 'http://localhost:5005/uploads/branding/rid/menu-bg.jpg',
  menuBackgroundStyle: 'medium',
  presetKey: null,
  logoBackground: 'auto',
  logoIsDark: false,
}

function joinResponse(branding: RestaurantBranding | null, session: DeviceSessionSummary | null = null) {
  return {
    guestId: 'g-1',
    displayName: null,
    restaurantId: 'r-1',
    restaurantName: 'Demo',
    restaurantAddress: null,
    organizationName: 'Demo Chain',
    tableId: 't-1',
    tableNumber: '1',
    activeOrder: { id: 'o-1', status: 'draft', totalAmount: 0, items: [] },
    partyMembers: [],
    branding,
    session,
  }
}

const PENDING_DEAL: PendingFlashDealPayload = {
  dealId: 'deal-1',
  orderId: 'o-1',
  productId: 'p-1',
  productName: 'Limonadă',
  imageUrl: 'http://localhost:5005/uploads/products/lemonade.jpg',
  originalPrice: 12,
  discountPrice: 9.6,
  expiresAt: '2026-07-21T12:02:00Z',
  quantity: 1,
}

function deviceSession(overrides: Partial<DeviceSessionSummary> = {}): DeviceSessionSummary {
  return {
    id: 'ds-1',
    linkExpirySeconds: 3600,
    cumulativeTotal: 42.5,
    orders: [
      {
        id: 'o-0',
        status: 'completed',
        totalAmount: 42.5,
        items: [
          {
            productId: 'p-1',
            productName: 'Limonadă',
            quantity: 1,
            unitPrice: 12,
            isFlashDealItem: false,
            imageUrl: null,
          },
        ],
      },
    ],
    pendingDeal: PENDING_DEAL,
    ...overrides,
  }
}

function atUrl(qrToken: string) {
  window.history.replaceState(null, '', `/${qrToken}`)
}

beforeEach(() => {
  sessionStorage.clear()
  localStorage.clear()
  joinMock.mockReset()
  setActivePinia(createPinia())
})

describe('useSessionStore branding persistence', () => {
  it('restores branding from sessionStorage on init, before any join', () => {
    atUrl('tok-1')
    sessionStorage.setItem('qrToken', 'tok-1')
    sessionStorage.setItem('branding', JSON.stringify(BRANDING))

    const store = useSessionStore()

    expect(store.branding).toEqual(BRANDING)
    expect(joinMock).not.toHaveBeenCalled()
  })

  it('does not restore another table\'s branding when the URL token differs', () => {
    atUrl('tok-OTHER')
    sessionStorage.setItem('qrToken', 'tok-1')
    sessionStorage.setItem('branding', JSON.stringify(BRANDING))

    const store = useSessionStore()

    expect(store.branding).toBeNull()
  })

  it('starts with null branding when nothing is stored or the entry is corrupt', () => {
    atUrl('tok-1')
    sessionStorage.setItem('qrToken', 'tok-1')
    expect(useSessionStore().branding).toBeNull()

    sessionStorage.setItem('branding', '{not json')
    setActivePinia(createPinia())
    expect(useSessionStore().branding).toBeNull()
  })

  it('persists branding on join so the next load can restore it', async () => {
    atUrl('tok-1')
    joinMock.mockResolvedValue(joinResponse(BRANDING))

    const store = useSessionStore()
    await store.initializeSession('tok-1')

    expect(JSON.parse(sessionStorage.getItem('branding')!)).toEqual(BRANDING)

    // Simulate the reload: a fresh pinia re-runs the store setup from storage.
    setActivePinia(createPinia())
    expect(useSessionStore().branding).toEqual(BRANDING)
  })

  it('removes stored branding when join says the restaurant is unbranded', async () => {
    atUrl('tok-1')
    sessionStorage.setItem('qrToken', 'tok-1')
    sessionStorage.setItem('branding', JSON.stringify(BRANDING))
    joinMock.mockResolvedValue(joinResponse(null))

    const store = useSessionStore()
    expect(store.branding).toEqual(BRANDING) // stale restore…

    await store.initializeSession('tok-1')

    expect(store.branding).toBeNull() // …join corrects it
    expect(sessionStorage.getItem('branding')).toBeNull()
  })
})

describe('useSessionStore device session summary', () => {
  it('stores session fields including cumulativeTotal and pendingDeal on join', async () => {
    atUrl('tok-1')
    const session = deviceSession()
    joinMock.mockResolvedValue(joinResponse(null, session))

    const store = useSessionStore()
    await store.initializeSession('tok-1')

    expect(store.deviceSessionId).toBe('ds-1')
    expect(store.linkExpirySeconds).toBe(3600)
    expect(store.cumulativeTotal).toBe(42.5)
    expect(store.sessionOrders).toEqual(session.orders)
    expect(store.pendingDeal).toEqual(PENDING_DEAL)
  })

  it('leaves the device-session defaults when the join response has session: null', async () => {
    atUrl('tok-1')
    joinMock.mockResolvedValue(joinResponse(null, null))

    const store = useSessionStore()
    await store.initializeSession('tok-1')

    expect(store.deviceSessionId).toBeNull()
    expect(store.linkExpirySeconds).toBeNull()
    expect(store.cumulativeTotal).toBe(0)
    expect(store.sessionOrders).toEqual([])
    expect(store.pendingDeal).toBeNull()
  })

  it('registerSubmittedRound accumulates cumulativeTotal with money rounding', async () => {
    atUrl('tok-1')
    joinMock.mockResolvedValue(joinResponse(null, null))
    const store = useSessionStore()
    await store.initializeSession('tok-1')

    const afterFirst = store.registerSubmittedRound(12.5)
    expect(afterFirst).toBe(12.5)

    const afterSecond = store.registerSubmittedRound(12.5)
    expect(afterSecond).toBe(25)
    expect(store.cumulativeTotal).toBe(25)
  })

  it('registerSubmittedRound rounds away floating point noise', async () => {
    atUrl('tok-1')
    joinMock.mockResolvedValue(joinResponse(null, null))
    const store = useSessionStore()
    await store.initializeSession('tok-1')

    store.registerSubmittedRound(0.1)
    const total = store.registerSubmittedRound(0.2)
    expect(total).toBe(0.3)
  })

  it('persists sessionOrders/cumulativeTotal/deviceSessionId to sessionStorage and restores them on reload, but NOT pendingDeal', async () => {
    atUrl('tok-1')
    const session = deviceSession()
    joinMock.mockResolvedValue(joinResponse(null, session))

    const store = useSessionStore()
    await store.initializeSession('tok-1')

    expect(JSON.parse(sessionStorage.getItem('sessionOrders')!)).toEqual(session.orders)
    expect(sessionStorage.getItem('cumulativeTotal')).toBe('42.5')
    expect(sessionStorage.getItem('deviceSessionId')).toBe('ds-1')

    // Simulate a same-tab reload: fresh pinia re-runs the store setup from storage.
    setActivePinia(createPinia())
    const restored = useSessionStore()

    expect(restored.deviceSessionId).toBe('ds-1')
    expect(restored.linkExpirySeconds).toBe(3600)
    expect(restored.cumulativeTotal).toBe(42.5)
    expect(restored.sessionOrders).toEqual(session.orders)
    // pendingDeal deliberately does not survive a reload — it's restored fresh
    // from the next join rather than trusted from storage.
    expect(restored.pendingDeal).toBeNull()
  })

  it('clearSession resets the device-session fields', async () => {
    atUrl('tok-1')
    joinMock.mockResolvedValue(joinResponse(null, deviceSession()))

    const store = useSessionStore()
    await store.initializeSession('tok-1')
    store.clearSession()

    expect(store.deviceSessionId).toBeNull()
    expect(store.linkExpirySeconds).toBeNull()
    expect(store.cumulativeTotal).toBe(0)
    expect(store.sessionOrders).toEqual([])
    expect(store.pendingDeal).toBeNull()
  })
})

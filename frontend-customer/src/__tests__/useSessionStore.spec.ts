// Regression: branding images (welcome photo, logo, menu background) must
// survive a mid-session reload. A reload with a stored screen skips the boot
// splash and renders immediately, so `session.branding` has to be restored from
// sessionStorage on store init — otherwise the diner sees a default-looking
// screen until /join answers, then the design pops in (the "closed Chrome,
// reopened, bare starter then design" bug).
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { RestaurantBranding } from '@shared/types/models'

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

function joinResponse(branding: RestaurantBranding | null) {
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

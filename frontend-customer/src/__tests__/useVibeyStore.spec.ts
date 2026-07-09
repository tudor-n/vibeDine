import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useVibeyStore } from '@/stores/useVibeyStore'
import { useCartStore } from '@/stores/useCartStore'
import { apiClient } from '@/services/api-client'
import { pickTeaser, followUpLine, nothingToSuggest } from '@/components/vibey/vibeyCopy'
import type { VibeyRecommendationResponse } from '@shared/types/api-responses'

// This suite is the automated guard for the Vibey cost invariant (see
// docs/superpowers/specs/2026-07-09-vibey-recommendations-design.md): the
// model may be called only from openSheet(), and only once per cart identity.
// Teaser + follow-up copy must never trigger a network call.

const mockResponse: VibeyRecommendationResponse = {
  summary: 'Astea merg perfect cu ce ai ales.',
  recommendations: [
    {
      id: 'rec-1',
      name: 'Cartofi wedges',
      category: 'Extra',
      priceRon: 18,
      reason: 'Se potrivesc grozav cu burgerul.',
    },
    {
      id: 'rec-2',
      name: 'IPA Zăganu',
      category: 'Băutură',
      priceRon: 22,
      reason: 'Completează masa cu o notă răcoritoare.',
    },
  ],
}

let vibeyRecsSpy: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  // useCartStore.syncCartWithBackend warns (expected, no orderId in these
  // tests) and useVibeyStore.openSheet's error path both write to the
  // console; keep test output clean.
  vi.spyOn(console, 'warn').mockImplementation(() => {})
  vi.spyOn(console, 'error').mockImplementation(() => {})
  vibeyRecsSpy = vi.spyOn(apiClient, 'getVibeyRecommendations').mockResolvedValue(mockResponse)
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('useVibeyStore — teaser (deterministic, no network)', () => {
  it('picks a deterministic teaser on load and never calls the model', () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')

    const first = store.teaser
    expect(first).toBe(pickTeaser('order-abc', 'ro'))
    expect(store.botMsg).toBe(first)
    expect(vibeyRecsSpy).not.toHaveBeenCalled()
  })

  it('is idempotent for the same cart identity (no re-teaser mid-session)', () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')
    const first = store.teaser

    store.ensureTeaser('order-abc', 'ro')
    expect(store.teaser).toBe(first)
    expect(vibeyRecsSpy).not.toHaveBeenCalled()
  })

  it('re-picks the teaser and resets stale state when the cart identity changes', () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')
    store.sheetOpen = true
    store.recsLoaded = true

    store.ensureTeaser('order-def', 'ro')

    expect(store.teaser).toBe(pickTeaser('order-def', 'ro'))
    expect(store.sheetOpen).toBe(false)
    expect(store.recsLoaded).toBe(false)
    expect(vibeyRecsSpy).not.toHaveBeenCalled()
  })
})

describe('useVibeyStore — openSheet (the ONLY fetch trigger)', () => {
  it('fires exactly one fetch across openSheet -> closeSheet -> openSheet', async () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')

    await store.openSheet()
    expect(store.sheetOpen).toBe(true)
    expect(store.recsLoaded).toBe(true)
    expect(store.thinking).toBe(false)
    expect(store.recs).toHaveLength(2)
    expect(store.summary).toBe(mockResponse.summary)
    expect(store.botMsg).toBe(mockResponse.summary)

    store.closeSheet()
    expect(store.sheetOpen).toBe(false)

    await store.openSheet()
    expect(store.sheetOpen).toBe(true)
    expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)
  })

  it('does not fire a second concurrent fetch on a double-tap before the first resolves', async () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')

    // Two taps of "Da, arată-mi" before the first request resolves: the second
    // still opens the sheet but must be blocked by the in-flight (thinking)
    // guard so only one model call is ever made.
    const p1 = store.openSheet()
    const p2 = store.openSheet()
    await Promise.all([p1, p2])

    expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)
    expect(store.sheetOpen).toBe(true)
    expect(store.recsLoaded).toBe(true)
  })

  it('fails gracefully to an empty, calm state without throwing', async () => {
    vibeyRecsSpy.mockRejectedValueOnce(new Error('boom'))
    const store = useVibeyStore()
    store.ensureTeaser('order-xyz', 'ro')

    await expect(store.openSheet()).resolves.toBeUndefined()

    expect(store.recs).toHaveLength(0)
    expect(store.recsLoaded).toBe(true)
    expect(store.thinking).toBe(false)
    // A failure is treated exactly like an empty response: noRecs, NOT allAdded,
    // and the calm bubble line — never a false "all added" claim.
    expect(store.noRecs).toBe(true)
    expect(store.allAdded).toBe(false)
    expect(store.botMsg).toBe(nothingToSuggest('ro'))
  })

  it('distinguishes a genuinely-empty AI response from all-added (calm state, no false claim)', async () => {
    vibeyRecsSpy.mockResolvedValueOnce({ summary: '', recommendations: [] })
    const store = useVibeyStore()
    store.ensureTeaser('order-empty', 'ro')

    await store.openSheet()

    expect(store.recs).toHaveLength(0)
    expect(store.recsLoaded).toBe(true)
    expect(store.noRecs).toBe(true)
    expect(store.allAdded).toBe(false) // nothing was added — must NOT claim it was
    expect(store.botMsg).toBe(nothingToSuggest('ro'))
  })

  it('closes the sheet on a genuinely-empty AI response instead of leaving a blank sheet open', async () => {
    vibeyRecsSpy.mockResolvedValueOnce({ summary: '', recommendations: [] })
    const store = useVibeyStore()
    store.ensureTeaser('order-empty-sheet', 'ro')

    await store.openSheet()

    // The scrim-covered blank sheet (header only, no recs) must not linger —
    // the calm nothingToSuggest bubble line behind it is what the guest sees.
    expect(store.sheetOpen).toBe(false)
    expect(store.noRecs).toBe(true)
    expect(store.botMsg).toBe(nothingToSuggest('ro'))

    // Re-opening must not re-fetch (recsLoaded guard) and must not pop a
    // blank sheet back open.
    await store.openSheet()
    expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)
    expect(store.sheetOpen).toBe(false)
  })

  it('closes the sheet on a failed AI fetch instead of leaving a blank sheet open', async () => {
    vibeyRecsSpy.mockRejectedValueOnce(new Error('boom'))
    const store = useVibeyStore()
    store.ensureTeaser('order-fail-sheet', 'ro')

    await store.openSheet()

    expect(store.sheetOpen).toBe(false)
    expect(store.noRecs).toBe(true)
    expect(store.botMsg).toBe(nothingToSuggest('ro'))

    await store.openSheet()
    expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)
    expect(store.sheetOpen).toBe(false)
  })
})

describe('useVibeyStore — addRec (no fetch, deterministic follow-up)', () => {
  it('adds the item to cart, removes it from recs, and sets a deterministic follow-up with no extra fetch', async () => {
    const store = useVibeyStore()
    const cartStore = useCartStore()
    store.ensureTeaser('order-abc', 'ro')
    await store.openSheet()

    await store.addRec('rec-1')

    expect(cartStore.items.some((i) => i.id === 'rec-1')).toBe(true)
    expect(cartStore.wasRecommended('rec-1')).toBe(true)
    expect(store.recs.some((r) => r.id === 'rec-1')).toBe(false)
    expect(store.botMsg).toBe(followUpLine('Cartofi wedges', 1, 'ro'))
    expect(store.sheetOpen).toBe(true) // one recommendation still remains
    expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)

    await store.addRec('rec-2')

    expect(store.recs).toHaveLength(0)
    expect(store.sheetOpen).toBe(false) // all added -> sheet closes
    expect(store.botMsg).toBe(followUpLine('IPA Zăganu', 0, 'ro'))
    expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)
    // Every offered rec is now in the cart -> the honest all-added state.
    expect(store.allAdded).toBe(true)
    expect(store.noRecs).toBe(false)
  })

  it('is a no-op for an unknown id and still makes no fetch', async () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')
    await store.openSheet()
    const before = store.botMsg

    await store.addRec('does-not-exist')

    expect(store.botMsg).toBe(before)
    expect(store.recs).toHaveLength(2)
    expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)
  })
})

describe('useVibeyStore — celebration (cosmetic, no fetch)', () => {
  it('pulses `celebrating` on add and clears it after ~1.2s, re-triggering across a spree', async () => {
    vi.useFakeTimers()
    try {
      const store = useVibeyStore()
      store.ensureTeaser('order-abc', 'ro')
      await store.openSheet()
      expect(store.celebrating).toBe(false)

      await store.addRec('rec-1')
      expect(store.celebrating).toBe(true)

      // A second add mid-celebration restarts the timer (stays happy).
      vi.advanceTimersByTime(800)
      await store.addRec('rec-2')
      expect(store.celebrating).toBe(true)
      vi.advanceTimersByTime(800) // 800 < 1200 since the last add reset it
      expect(store.celebrating).toBe(true)

      vi.advanceTimersByTime(1200)
      expect(store.celebrating).toBe(false)

      // Every rec was added -> Vibey stays lastingly happy after the transient
      // hop ends (the avatar keeps smiling; `allAdded` is what drives it).
      expect(store.allAdded).toBe(true)

      // Still exactly the one fetch — the celebration is purely cosmetic.
      expect(vibeyRecsSpy).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('reset() clears an in-flight celebration', async () => {
    vi.useFakeTimers()
    try {
      const store = useVibeyStore()
      store.ensureTeaser('order-abc', 'ro')
      await store.openSheet()
      await store.addRec('rec-1')
      expect(store.celebrating).toBe(true)

      store.reset()
      expect(store.celebrating).toBe(false)
      // The pending timer must not resurrect it after reset.
      vi.advanceTimersByTime(1500)
      expect(store.celebrating).toBe(false)
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('useVibeyStore — reset', () => {
  it('clears all state', async () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')
    await store.openSheet()

    store.reset()

    expect(store.teaser).toBe('')
    expect(store.recs).toHaveLength(0)
    expect(store.recsLoaded).toBe(false)
    expect(store.thinking).toBe(false)
    expect(store.summary).toBe('')
    expect(store.sheetOpen).toBe(false)
    expect(store.botMsg).toBe('')
  })

  it('after reset, ensureTeaser re-picks (does not silently stay empty)', () => {
    const store = useVibeyStore()
    store.ensureTeaser('order-abc', 'ro')
    store.reset()

    store.ensureTeaser('order-abc', 'ro')
    expect(store.teaser).toBe(pickTeaser('order-abc', 'ro'))
  })
})

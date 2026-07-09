import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Product } from '@shared/types/models'
import type { VibeyCartItem } from '@shared/types/api-requests'
import type { VibeyRec } from '@shared/types/api-responses'
import { apiClient } from '@/services/api-client'
import { useCartStore } from '@/stores/useCartStore'
import { useLocale, type Locale } from '@/composables/useLocale'
import { pickTeaser, followUpLine } from '@/components/vibey/vibeyCopy'

// Vibey: the in-cart AI recommendation assistant. The cost invariant (see
// docs/superpowers/specs/2026-07-09-vibey-recommendations-design.md) is that
// `openSheet()` is the ONLY place that may call the model, and only once per
// cart identity (guarded by `recsLoaded`). Teaser + post-add follow-up are
// deterministic client copy (`vibeyCopy.ts`) — never a network call.
export const useVibeyStore = defineStore('vibey', () => {
  const cartStore = useCartStore()

  // Current cart identity (the draft orderId) the teaser/recs were computed
  // for. Internal — drives `ensureTeaser`'s per-cart stability, not meant to
  // be read/written directly by consumers.
  const cartKey = ref('')

  const teaser = ref('')
  const recs = ref<VibeyRec[]>([])
  const recsLoaded = ref(false)
  const thinking = ref(false)
  const summary = ref('')
  const sheetOpen = ref(false)
  const botMsg = ref('')

  const hasRecs = computed(() => recs.value.length > 0)
  const allAdded = computed(() => recsLoaded.value && recs.value.length === 0)

  /** Clears all Vibey state — call on cart clear / order-id change. */
  function reset() {
    cartKey.value = ''
    teaser.value = ''
    recs.value = []
    recsLoaded.value = false
    thinking.value = false
    summary.value = ''
    sheetOpen.value = false
    botMsg.value = ''
  }

  // Picks the deterministic teaser once per cart identity (`seed`, normally
  // sessionStore.orderId). Idempotent: calling it repeatedly for the same
  // seed is a no-op, so it's safe to call on every render/mount. A different
  // seed means a new cart, so state resets before picking the fresh teaser —
  // no stale recs leak across orders. Never touches the network.
  function ensureTeaser(seed: string, locale: Locale) {
    if (cartKey.value === seed) {
      if (!teaser.value) {
        teaser.value = pickTeaser(seed, locale)
        botMsg.value = teaser.value
      }
      return
    }
    reset()
    cartKey.value = seed
    teaser.value = pickTeaser(seed, locale)
    botMsg.value = teaser.value
  }

  // The ONLY place a Vibey network call may originate. Guarded by
  // `recsLoaded` so re-opening the sheet (or calling openSheet again after a
  // prior success or graceful failure) never re-hits the model.
  async function openSheet() {
    sheetOpen.value = true
    if (recsLoaded.value) return

    thinking.value = true
    try {
      const items: VibeyCartItem[] = cartStore.items.map((it) => ({
        id: it.id,
        name: it.name,
        qty: it.quantity,
      }))
      const { locale } = useLocale()
      const res = await apiClient.getVibeyRecommendations({ items, locale: locale.value })
      recs.value = res.recommendations ?? []
      summary.value = res.summary ?? ''
      botMsg.value = summary.value || botMsg.value
    } catch {
      // Graceful empty state — an AI-service failure must never surface as an
      // error to the guest; Vibey simply doesn't push anything.
      recs.value = []
      summary.value = ''
    } finally {
      recsLoaded.value = true
      thinking.value = false
    }
  }

  function closeSheet() {
    sheetOpen.value = false
  }

  // Adds a recommended item to the cart and acknowledges it with a
  // deterministic follow-up line — no model call. Closes the sheet once every
  // recommendation has been added.
  async function addRec(id: string) {
    const rec = recs.value.find((r) => r.id === id)
    if (!rec) return

    const product: Product = {
      id: rec.id,
      categoryId: '',
      categoryName: rec.category ?? undefined,
      name: rec.name,
      description: '',
      price: rec.priceRon,
      imageUrl: null,
      isAvailable: true,
    }
    await cartStore.addRecommendedItem(product)

    recs.value = recs.value.filter((r) => r.id !== id)
    const { locale } = useLocale()
    botMsg.value = followUpLine(rec.name, recs.value.length, locale.value)
    if (recs.value.length === 0) sheetOpen.value = false
  }

  return {
    teaser,
    recs,
    recsLoaded,
    thinking,
    summary,
    sheetOpen,
    botMsg,
    hasRecs,
    allAdded,
    ensureTeaser,
    openSheet,
    closeSheet,
    addRec,
    reset,
  }
})

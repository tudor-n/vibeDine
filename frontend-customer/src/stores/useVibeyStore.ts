import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Product } from '@shared/types/models'
import type { VibeyCartItem } from '@shared/types/api-requests'
import type { VibeyRec } from '@shared/types/api-responses'
import { apiClient } from '@/services/api-client'
import { useCartStore } from '@/stores/useCartStore'
import { useLocale, type Locale } from '@/composables/useLocale'
import { pickTeaser, followUpLine, nothingToSuggest } from '@/components/vibey/vibeyCopy'

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
  // The full set Vibey fetched, kept intact for the session. The list the guest
  // actually sees (`recs`) is derived from this minus whatever is already in the
  // cart, so adding a rec hides it and REMOVING that item from the cart brings
  // the recommendation back — you can never lose a suggestion by adding then
  // removing it.
  const fetchedRecs = ref<VibeyRec[]>([])
  const recsLoaded = ref(false)
  // Whether the ONE fetch ever returned any recommendations. Captured once, at
  // fetch time — it's what lets us tell the "guest added every rec" state
  // (hadRecs true) apart from the "AI genuinely returned nothing" state
  // (hadRecs false), which must NOT show the all-added confirmation (that would
  // be a false claim).
  const hadRecs = ref(false)
  const thinking = ref(false)
  const summary = ref('')
  const sheetOpen = ref(false)
  const botMsg = ref('')
  // Brief happy state: true for ~1.2s right after a recommendation is added, so
  // the avatar can smile and hop. Purely cosmetic — never gates any logic.
  const celebrating = ref(false)
  let celebrationTimer: ReturnType<typeof setTimeout> | null = null

  // The suggestions still worth showing: everything Vibey fetched that isn't
  // already in the cart. Reactive to cart changes, so removing an added item
  // restores its recommendation automatically.
  const recs = computed(() =>
    fetchedRecs.value.filter((r) => !cartStore.items.some((i) => i.id === r.id)),
  )

  const hasRecs = computed(() => recs.value.length > 0)
  // True only once the guest has added every recommendation Vibey actually
  // offered — never when the fetch came back empty.
  const allAdded = computed(() => recsLoaded.value && hadRecs.value && recs.value.length === 0)
  // The AI genuinely had nothing to push (exhausted pool or graceful failure).
  // The calm bubble line carries this; the reveal pill / all-added row hide.
  const noRecs = computed(() => recsLoaded.value && !hadRecs.value)

  /** Clears all Vibey state — call on cart clear / order-id change. */
  function reset() {
    cartKey.value = ''
    teaser.value = ''
    fetchedRecs.value = []
    recsLoaded.value = false
    hadRecs.value = false
    thinking.value = false
    summary.value = ''
    sheetOpen.value = false
    botMsg.value = ''
    if (celebrationTimer) clearTimeout(celebrationTimer)
    celebrationTimer = null
    celebrating.value = false
  }

  // Fire the brief happy animation. Re-triggerable: adding several recs in a
  // row restarts the timer so Vibey stays cheerful across the whole spree.
  function celebrate() {
    celebrating.value = true
    if (celebrationTimer) clearTimeout(celebrationTimer)
    celebrationTimer = setTimeout(() => {
      celebrating.value = false
      celebrationTimer = null
    }, 1200)
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
  // prior success or graceful failure) never re-hits the model. Opening the
  // sheet always works; the *fetch* is additionally gated by `thinking` so a
  // double-tap before the first request resolves can't fire a second
  // concurrent call — the cost invariant is "exactly one".
  async function openSheet() {
    // The AI already came back with nothing (or failed) for this cart — the
    // reveal pill that triggers openSheet() is hidden in this state, but
    // guard defensively against a stray re-open call ever popping a blank
    // sheet back open.
    if (recsLoaded.value && !hadRecs.value) return

    sheetOpen.value = true
    if (recsLoaded.value || thinking.value) return

    thinking.value = true
    try {
      const items: VibeyCartItem[] = cartStore.items.map((it) => ({
        id: it.id,
        name: it.name,
        qty: it.quantity,
      }))
      const { locale } = useLocale()
      const res = await apiClient.getVibeyRecommendations({ items, locale: locale.value })
      fetchedRecs.value = res.recommendations ?? []
      // Did the ONE fetch return anything? Drives allAdded vs noRecs.
      hadRecs.value = fetchedRecs.value.length > 0
      summary.value = res.summary ?? ''
      botMsg.value = hadRecs.value
        ? summary.value || botMsg.value
        : nothingToSuggest(locale.value)
      // A genuinely-empty AI response must not leave a blank sheet open (header
      // only, no recs) behind the scrim — close it so the calm nothingToSuggest
      // bubble line in VibeyBlock is what the guest actually sees.
      if (!hadRecs.value) sheetOpen.value = false
    } catch {
      // Graceful empty state — an AI-service failure must never surface as an
      // error to the guest; Vibey simply doesn't push anything. Same calm line
      // as a legitimately-empty response so the guest never sees a false
      // "all added" claim. Also must not leave a blank sheet open.
      const { locale } = useLocale()
      fetchedRecs.value = []
      hadRecs.value = false
      summary.value = ''
      botMsg.value = nothingToSuggest(locale.value)
      sheetOpen.value = false
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
    // Adds to the cart; `recs` (derived) drops it automatically because it's now
    // in the cart — and would bring it back if the guest later removes it.
    await cartStore.addRecommendedItem(product)

    const { locale } = useLocale()
    botMsg.value = followUpLine(rec.name, recs.value.length, locale.value)
    celebrate()
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
    celebrating,
    hasRecs,
    allAdded,
    noRecs,
    ensureTeaser,
    openSheet,
    closeSheet,
    addRec,
    reset,
  }
})

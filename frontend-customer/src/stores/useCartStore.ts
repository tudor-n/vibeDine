import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import type { Product } from '@shared/types/models'
import type { UpdateOrderItemsRequest } from '@shared/types/api-requests'
import { apiClient, type PartyOrderItem } from '@/services/api-client'
import { roundMoney } from '@/utils/money'
import { useSessionStore } from './useSessionStore'
import { useHappyHourStore } from './useHappyHourStore'

export interface CartItem extends Product {
  quantity: number
}

// Persist the in-progress cart in sessionStorage (same scope as the session
// info in useSessionStore) so an accidental tab refresh while ordering keeps
// the cart intact. It's wiped when the order is sent to the kitchen (clearCart)
// and when the tab closes — exactly the lifetime of one ordering session.
const CART_STORAGE_KEY = 'cart'

interface PersistedCart {
  items: CartItem[]
  recommendedItemIds: string[]
}

function loadPersistedCart(): PersistedCart {
  try {
    const raw = sessionStorage.getItem(CART_STORAGE_KEY)
    if (!raw) return { items: [], recommendedItemIds: [] }
    const parsed = JSON.parse(raw) as Partial<PersistedCart>
    return {
      items: Array.isArray(parsed?.items) ? parsed.items : [],
      recommendedItemIds: Array.isArray(parsed?.recommendedItemIds) ? parsed.recommendedItemIds : [],
    }
  } catch {
    return { items: [], recommendedItemIds: [] }
  }
}

export const useCartStore = defineStore('cart', () => {

  const persisted = loadPersistedCart()
  const items = ref<CartItem[]>(persisted.items)
  const recommendedItemIds = ref<Set<string>>(new Set(persisted.recommendedItemIds))
  const sessionStore = useSessionStore()
  const happyHour = useHappyHourStore()

  // Mirror every cart change back to sessionStorage. clearCart()/abandonForMerge()
  // empty `items`, so a submitted or merged-away cart is persisted as empty too.
  watch(
    [items, recommendedItemIds],
    () => {
      try {
        sessionStorage.setItem(
          CART_STORAGE_KEY,
          JSON.stringify({
            items: items.value,
            recommendedItemIds: Array.from(recommendedItemIds.value),
          }),
        )
      } catch (error) {
        console.error('Nu s-a putut salva coșul în sessionStorage:', error)
      }
    },
    { deep: true },
  )

  // When this phone's draft has been merged into someone else's order it must
  // stop syncing — its draft is now "merged" and PATCHes would 404.
  const frozen = ref(false)

  // Round to the cent: a raw float sum (e.g. 73.9 * 2 + 36.95) would surface
  // IEEE-754 noise like 226.85000000000002 in every total readout.
  // The price a line actually charges right now: the live happy-hour price when
  // this item is the happy-hour product, otherwise its stored (full) price. The
  // bill is applied authoritatively server-side; this keeps the display in step,
  // and tracks the window edges because it reads the happy-hour store reactively.
  function effectivePrice(item: CartItem): number {
    const discounted = happyHour.discountedPriceFor(item.id)
    return discounted != null ? discounted : item.price
  }

  const total = computed(() => {
    return roundMoney(items.value.reduce((sum, item) => sum + effectivePrice(item) * item.quantity, 0))
  })

  let syncTimeout: ReturnType<typeof setTimeout> | null = null;
  const lastSyncPromise = ref<Promise<void> | null>(null)

  // Build the wire payload for the backend draft from the current cart state.
  // Shared by the debounced sync and the immediate flush() so both send an
  // identical shape.
  function buildPayload(): UpdateOrderItemsRequest {
    return {
      items: items.value.map((item) => ({
        productId: item.id,
        quantity: item.quantity,
        wasAiRecommended: recommendedItemIds.value.has(item.id)
      }))
    }
  }

  async function syncCartWithBackend() {
    if (frozen.value) return
    if(!sessionStore.orderId){
      console.warn('Nu există o comandă activă (orderId lipsește).')
      return
    }
    const payload: UpdateOrderItemsRequest = buildPayload()

    if (syncTimeout) clearTimeout(syncTimeout)

    lastSyncPromise.value = new Promise<void>((resolve) => {
      syncTimeout = setTimeout(async () => {
        try {
          await apiClient.updateCartItems(sessionStore.orderId!, payload)
        } catch (error) {
          console.error('Eroare la sincronizarea coșului pe backend:', error)
        } finally {
          resolve()
        }
      }, 500)
    })
  }

  // Immediately push the current cart to the backend draft, cancelling any
  // pending debounced sync. Called right before submit so the order the kitchen
  // receives always includes the last edit — awaiting lastSyncPromise is not
  // enough (it only resolves after the 500 ms timer, and rapid edits orphan
  // earlier promises so they never resolve). Rejects on failure so submit aborts
  // rather than dispatch a stale draft.
  async function flush() {
    if (syncTimeout) {
      clearTimeout(syncTimeout)
      syncTimeout = null
    }
    if (frozen.value) return
    if (!sessionStore.orderId) return
    await apiClient.updateCartItems(sessionStore.orderId, buildPayload())
  }

  async function addItem(product: Product){
    const existingItem = items.value.find((i) => i.id === product.id)

    if(existingItem){
      existingItem.quantity++
    }else {
      items.value.push({ ...product, quantity: 1 })
    }
    await syncCartWithBackend()
  }

  async function addRecommendedItem(product: Product) {
    recommendedItemIds.value.add(product.id)
    await addItem(product)
  }

  async function removeItem(productId: string) {
    const item = items.value.find((i) => i.id === productId)
    if (item) {
      if (item.quantity > 1) {
        item.quantity--
      } else {
        items.value = items.value.filter((i) => i.id !== productId)
        recommendedItemIds.value.delete(productId)
      }

      await syncCartWithBackend()
    }
  }

  function addLocalItemOnly(product: Product) {
    const existingItem = items.value.find((i) => i.id === product.id)
    if (existingItem) {
      existingItem.quantity++
    } else {
      items.value.push({ ...product, quantity: 1 })
    }
  }

  function clearCart() {
    items.value = []
    recommendedItemIds.value.clear()
  }

  function mapServerItems(serverItems: PartyOrderItem[]): CartItem[] {
    return serverItems.map((i) => ({
      id: i.productId,
      categoryId: '',
      name: i.productName,
      description: '',
      // Store the FULL price so the reactive happy-hour discount isn't applied on
      // top of an already-discounted server price. For the happy-hour product the
      // server's unitPrice may be discounted, so recover the original; every other
      // product's unitPrice is already its full price.
      price: happyHour.active?.productId === i.productId ? happyHour.active.originalPrice : i.unitPrice,
      imageUrl: i.imageUrl,
      isAvailable: true,
      quantity: i.quantity,
    }))
  }

  // Replace the local cart with the authoritative server order (used by the
  // merge initiator: after merging, the backend order already holds everyone's
  // items, so we mirror them in WITHOUT re-syncing).
  function replaceFromServer(serverItems: PartyOrderItem[]) {
    items.value = mapServerItems(serverItems)
    recommendedItemIds.value.clear()
  }

  // Re-hydrate the cart from the backend's persisted draft after a reload. The
  // backend keeps every cart change (updateCartItems), so its draft order is the
  // durable source of truth and survives the phone losing sessionStorage — e.g.
  // re-scanning the QR opens a fresh browser tab/context with empty storage.
  // Only fills an EMPTY cart, so it never clobbers items already restored from
  // sessionStorage (a plain refresh) or added earlier this session.
  function restoreFromServerIfEmpty(serverItems: PartyOrderItem[] | undefined | null) {
    if (frozen.value || items.value.length > 0) return
    if (!serverItems || serverItems.length === 0) return
    items.value = mapServerItems(serverItems)
    recommendedItemIds.value.clear()
  }

  // This phone's draft was merged away: drop any pending sync, empty the cart,
  // and freeze so nothing tries to write to the now-merged order.
  function abandonForMerge() {
    if (syncTimeout) {
      clearTimeout(syncTimeout)
      syncTimeout = null
    }
    items.value = []
    recommendedItemIds.value.clear()
    frozen.value = true
  }

  // Start a brand-new order (e.g. "order again" after a merge): clear and unfreeze.
  function resetForNewOrder() {
    if (syncTimeout) {
      clearTimeout(syncTimeout)
      syncTimeout = null
    }
    items.value = []
    recommendedItemIds.value.clear()
    frozen.value = false
  }

  function wasRecommended(productId: string): boolean {
    return recommendedItemIds.value.has(productId)
  }

  // Tear down any pending debounced sync without touching the cart itself —
  // used on link expiry (see OrderingView's `watch(linkExpired…)` teardown),
  // where an abandoned tab must stop talking to the backend but the cart
  // stays intact for a re-scan.
  function cancelPendingSync() {
    if (syncTimeout) {
      clearTimeout(syncTimeout)
      syncTimeout = null
    }
  }

  return {
    items,
    total,
    effectivePrice,
    frozen,
    lastSyncPromise,
    flush,
    addItem,
    addRecommendedItem,
    removeItem,
    clearCart,
    addLocalItemOnly,
    replaceFromServer,
    restoreFromServerIfEmpty,
    abandonForMerge,
    resetForNewOrder,
    wasRecommended,
    cancelPendingSync,
  }
})

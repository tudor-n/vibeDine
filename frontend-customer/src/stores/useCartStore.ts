import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Product } from '@shared/types/models'
import type { UpdateOrderItemsRequest } from '@shared/types/api-requests'
import { apiClient, type PartyOrderItem } from '@/services/api-client'
import { useSessionStore } from './useSessionStore'

export interface CartItem extends Product {
  quantity: number
}

export const useCartStore = defineStore('cart', () => {

  const items = ref<CartItem[]>([])
  const recommendedItemIds = ref<Set<string>>(new Set())
  const sessionStore = useSessionStore()

  // When this phone's draft has been merged into someone else's order it must
  // stop syncing — its draft is now "merged" and PATCHes would 404.
  const frozen = ref(false)

  const total = computed(() => {
    return items.value.reduce((sum, item) => sum + item.price * item.quantity, 0)
  })

  let syncTimeout: ReturnType<typeof setTimeout> | null = null;
  const lastSyncPromise = ref<Promise<void> | null>(null)

  async function syncCartWithBackend() {
    if (frozen.value) return
    if(!sessionStore.orderId){
      console.warn('Nu există o comandă activă (orderId lipsește).')
      return
    }
    const payload: UpdateOrderItemsRequest = {
      items: items.value.map((item) => ({
        productId: item.id,
        quantity: item.quantity,
        wasAiRecommended: recommendedItemIds.value.has(item.id)
      }))
    }

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

  // Replace the local cart with the authoritative server order (used by the
  // merge initiator: after merging, the backend order already holds everyone's
  // items, so we mirror them in WITHOUT re-syncing).
  function replaceFromServer(serverItems: PartyOrderItem[]) {
    items.value = serverItems.map((i) => ({
      id: i.productId,
      categoryId: '',
      name: i.productName,
      description: '',
      price: i.unitPrice,
      imageUrl: i.imageUrl,
      isAvailable: true,
      quantity: i.quantity,
    }))
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

  return {
    items,
    total,
    frozen,
    lastSyncPromise,
    addItem,
    addRecommendedItem,
    removeItem,
    clearCart,
    addLocalItemOnly,
    replaceFromServer,
    abandonForMerge,
    resetForNewOrder,
    wasRecommended,
  }
})

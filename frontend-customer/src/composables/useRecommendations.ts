import { ref, watch } from 'vue'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { apiClient } from '@/services/api-client'
import type { Recommendation, Product } from '@shared/types/models'

export function useRecommendations() {
  const cartStore = useCartStore()
  const sessionStore = useSessionStore()

  const pairings = ref<Recommendation[]>([])
  const upsells = ref<Recommendation[]>([])
  const isLoading = ref(false)

  let debounceTimeout: ReturnType<typeof setTimeout>

  watch(
    () => cartStore.items,
    (newItems) => {
      clearTimeout(debounceTimeout)

      if (newItems.length === 0 || !sessionStore.orderId) {
        pairings.value = []
        upsells.value = []
        return
      }

      debounceTimeout = setTimeout(async () => {
        try {
          isLoading.value = true

          // Wait for any in-flight cart PATCH to finish, otherwise the backend
          // reads a stale order and rule-driven recommendations don't match.
          if (cartStore.lastSyncPromise) {
            await cartStore.lastSyncPromise
          }

          const itemIds = newItems.map((item) => item.id)

          const response = await apiClient.getRecommendations(sessionStore.orderId!, itemIds)

          if (Array.isArray(response)) {
            const menuResponse = await apiClient.getMenu()
            const allProducts = menuResponse.categories.flatMap(c => c.products || [])

            const mappedRecs: Recommendation[] = response.map((rec: any) => {
              const productInfo = allProducts.find((p: Product) => p.id === rec.productId) || {
                id: rec.productId,
                name: 'Produs Recomandat',
                description: '',
                price: 0,
                imageUrl: ''
              }

              return {
                id: rec.productId,
                type: 'pairing',
                reason: rec.reason,
                confidence: rec.confidence || 1,
                menuItem: {
                  id: productInfo.id,
                  name: productInfo.name,
                  description: productInfo.description,
                  price: productInfo.price,
                  imageUrl: productInfo.imageUrl,
                  categoryName: ''
                },
                reasonType: 'pairsWithOrder',
                discount: { percent: null, originalPrice: null, discountedPrice: null },
                source: 'ai'
              } as Recommendation
            })

            pairings.value = mappedRecs
            upsells.value = []
          } else {
            pairings.value = response.pairings || []
            upsells.value = response.upsells || []
          }
        } catch (error) {
          console.error('Eroare la încărcarea recomandărilor:', error)
        } finally {
          isLoading.value = false
        }
      }, 500)
    },
    { deep: true }
  )

  watch(
    () => sessionStore.orderId,
    () => {
      pairings.value = []
      upsells.value = []
    }
  )

  return { pairings, upsells, isLoading }
}

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
                image_url: ''
              }

              return {
                id: rec.productId,
                type: 'pairing',
                reason: rec.reason,
                confidence: rec.confidence || 1,
                menu_item: {
                  id: productInfo.id,
                  name: productInfo.name,
                  description: productInfo.description,
                  price: productInfo.price,
                  image_url: productInfo.image_url,
                  category_name: ''
                },
                reason_type: 'pairs_with_order',
                discount: { percent: null, original_price: null, discounted_price: null },
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

  return { pairings, upsells, isLoading }
}

import { ref, watch } from 'vue'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { apiClient } from '@/services/api-client'
import type { Recommendation } from '@shared/types/models'

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

          pairings.value = response.pairings?.items || []
          upsells.value = response.upsells?.items || []
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

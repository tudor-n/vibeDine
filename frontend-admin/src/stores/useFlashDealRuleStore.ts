import { defineStore } from 'pinia'
import { ref } from 'vue'
import { apiClient } from '@/services/api-client'
import type { FlashDealRule } from '@shared/types/models'

export interface FlashDealPayload {
  productId: string
  discountPercent: number
  minOrderTotal: number | null
  isEnabled: boolean
}

// One deterministic flash-deal rule per restaurant: saving replaces it, clearing
// removes it. Drives the post-order claimable popup (separate from happy hour).
export const useFlashDealRuleStore = defineStore('flashDealRule', () => {
  const config = ref<FlashDealRule | null>(null)
  const isLoading = ref(false)

  async function fetchConfig() {
    try {
      isLoading.value = true
      const data = await apiClient.getFlashDeal()
      config.value = data && (data as FlashDealRule).id ? (data as FlashDealRule) : null
    } catch (error) {
      console.error('Eroare la aducerea flash deal:', error)
    } finally {
      isLoading.value = false
    }
  }

  async function save(payload: FlashDealPayload) {
    try {
      isLoading.value = true
      config.value = await apiClient.saveFlashDeal(payload)
    } catch (error) {
      console.error('Eroare la salvarea flash deal:', error)
      throw error
    } finally {
      isLoading.value = false
    }
  }

  async function clear() {
    try {
      isLoading.value = true
      await apiClient.clearFlashDeal()
      config.value = null
    } catch (error) {
      console.error('Eroare la ștergerea flash deal:', error)
      throw error
    } finally {
      isLoading.value = false
    }
  }

  return { config, isLoading, fetchConfig, save, clear }
})

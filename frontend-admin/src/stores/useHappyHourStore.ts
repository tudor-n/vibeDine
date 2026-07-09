import { defineStore } from 'pinia'
import { ref } from 'vue'
import { apiClient } from '@/services/api-client'
import type { HappyHourDeal } from '@shared/types/models'

export interface HappyHourPayload {
  productId: string
  discountPercent: number
  startTime: string // "HH:mm"
  endTime: string // "HH:mm"
  isEnabled: boolean
}

// A restaurant has at most one happy hour at a time: saving replaces it,
// clearing removes it. There is no list — this mirrors the single-config model.
export const useHappyHourStore = defineStore('happyHour', () => {
  const config = ref<HappyHourDeal | null>(null)
  const isLoading = ref(false)

  async function fetchConfig() {
    try {
      isLoading.value = true
      const data = await apiClient.getHappyHour()
      // The fetcher returns {} for an empty/204 body — treat anything without an
      // id as "no happy hour configured".
      config.value = data && (data as HappyHourDeal).id ? (data as HappyHourDeal) : null
    } catch (error) {
      console.error('Eroare la aducerea happy hour:', error)
    } finally {
      isLoading.value = false
    }
  }

  async function save(payload: HappyHourPayload) {
    try {
      isLoading.value = true
      config.value = await apiClient.saveHappyHour(payload)
    } catch (error) {
      console.error('Eroare la salvarea happy hour:', error)
      throw error
    } finally {
      isLoading.value = false
    }
  }

  async function clear() {
    try {
      isLoading.value = true
      await apiClient.clearHappyHour()
      config.value = null
    } catch (error) {
      console.error('Eroare la ștergerea happy hour:', error)
      throw error
    } finally {
      isLoading.value = false
    }
  }

  return { config, isLoading, fetchConfig, save, clear }
})

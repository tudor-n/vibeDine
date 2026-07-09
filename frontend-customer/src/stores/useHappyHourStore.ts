import { defineStore } from 'pinia'
import { ref } from 'vue'
import { apiClient } from '@/services/api-client'
import type { ActiveHappyHour } from '@shared/types/models'

// The customer-facing happy hour: the restaurant's live deal (or null) used to
// show the menu banner and the discounted price. Polled so the banner/pricing
// appear and disappear at the window edges without a manual refresh. Pricing is
// applied authoritatively server-side at order time; this is the matching display.
export const useHappyHourStore = defineStore('happyHour', () => {
  const active = ref<ActiveHappyHour | null>(null)
  let pollTimer: ReturnType<typeof setInterval> | null = null

  async function fetchActive() {
    try {
      const data = await apiClient.getActiveHappyHour()
      // 204/empty → {} (no productId) → no active happy hour.
      active.value = data && (data as ActiveHappyHour).productId ? (data as ActiveHappyHour) : null
    } catch {
      active.value = null
    }
  }

  // The discounted price for a product if it is the live happy-hour product, else
  // null. Returns the server-computed value so the client and the bill agree.
  function discountedPriceFor(productId: string): number | null {
    return active.value && active.value.productId === productId
      ? active.value.discountedPrice
      : null
  }

  function startPolling() {
    fetchActive()
    if (pollTimer) return
    pollTimer = setInterval(fetchActive, 60_000)
  }

  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  return { active, fetchActive, discountedPriceFor, startPolling, stopPolling }
})

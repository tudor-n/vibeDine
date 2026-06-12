import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { FlashDeal } from '@shared/types/models'

export const useFlashDealStore = defineStore('flashDeal', () => {
  const activeDeal = ref<FlashDeal | null>(null)
  const activeDealOrderId = ref<string | null>(null)
  // How many discounted portions the offer is for: one per guest whose cart is
  // on this bill. 1 for a solo / pay-separately order, N for a bill that merged
  // N guests. Kept alongside the deal (like orderId) rather than on the shared
  // FlashDeal type so it stays a frontend-only display concern.
  const activeDealQuantity = ref(1)

  function setActiveDeal(deal: FlashDeal, orderId: string, quantity = 1) {
    activeDeal.value = deal
    activeDealOrderId.value = orderId
    activeDealQuantity.value = quantity > 0 ? quantity : 1
  }

  function clearDeal() {
    activeDeal.value = null
    activeDealOrderId.value = null
    activeDealQuantity.value = 1
  }

  return { activeDeal, activeDealOrderId, activeDealQuantity, setActiveDeal, clearDeal }
})

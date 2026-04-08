import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { FlashDeal } from '@shared/types/models'

export const useFlashDealStore = defineStore('flashDeal', () => {
  const activeDeal = ref<FlashDeal | null>(null)
  const activeDealOrderId = ref<string | null>(null)

  function setActiveDeal(deal: FlashDeal, orderId: string) {
    activeDeal.value = deal
    activeDealOrderId.value = orderId
  }

  function clearDeal() {
    activeDeal.value = null
    activeDealOrderId.value = null
  }

  return { activeDeal, activeDealOrderId, setActiveDeal, clearDeal }
})

import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { OrderStatus } from '@shared/types/models'

// Single source of truth for the customer's "what stage is my order at?" UI.
// Updated by the SignalR client when the admin moves the order across the
// kanban; OrderSentView reads from here to render the timeline.
export const useOrderStatusStore = defineStore('orderStatus', () => {
  const status = ref<OrderStatus | null>(null)
  const orderId = ref<string | null>(null)

  function setStatus(id: string, next: OrderStatus) {
    orderId.value = id
    status.value = next
  }

  function clear() {
    status.value = null
    orderId.value = null
  }

  return { status, orderId, setStatus, clear }
})

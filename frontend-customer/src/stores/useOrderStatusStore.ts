import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { OrderStatus } from '@shared/types/models'

// Single source of truth for the customer's "what stage is my order at?" UI.
// Updated by the SignalR client when the admin moves the order across the
// kanban; OrderSentView reads from here to render the timeline.
export const useOrderStatusStore = defineStore('orderStatus', () => {
  const status = ref<OrderStatus | null>(null)
  const orderId = ref<string | null>(null)

  // Timeline timestamps live here (not in OrderSentView's local state) so they
  // survive the diner leaving the progress screen and re-opening it later from
  // the "view progress" button — otherwise "submitted at" would reset to the
  // moment they re-opened it.
  const submittedAt = ref<Date | null>(null)
  const preparingAt = ref<Date | null>(null)
  const servedAt = ref<Date | null>(null)

  // Stamp the "submitted" time once, when the order-sent screen first appears.
  function markSubmitted() {
    if (!submittedAt.value) submittedAt.value = new Date()
  }

  function setStatus(id: string, next: OrderStatus) {
    orderId.value = id
    status.value = next
    // Stamp each stage on the first transition into it so the displayed time
    // matches when the kitchen moved the order, not when the UI re-rendered.
    if (
      (next === 'preparing' || next === 'ready' || next === 'completed') &&
      !preparingAt.value
    ) {
      preparingAt.value = new Date()
    }
    if (next === 'completed' && !servedAt.value) {
      servedAt.value = new Date()
    }
  }

  function clear() {
    status.value = null
    orderId.value = null
    submittedAt.value = null
    preparingAt.value = null
    servedAt.value = null
  }

  return {
    status,
    orderId,
    submittedAt,
    preparingAt,
    servedAt,
    markSubmitted,
    setStatus,
    clear,
  }
})

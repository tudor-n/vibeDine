import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import type { OrderStatus } from '@shared/types/models'

// Persisted in sessionStorage so the live order survives an accidental tab
// refresh. SignalR never replays past events, so without this the timeline —
// and the "view order status" button that's driven off it — would reset to
// empty on reload even though the order is still in progress. Cleared when the
// next order is submitted (clear()) and when the tab closes.
const ORDER_STATUS_STORAGE_KEY = 'orderStatus'

interface PersistedOrderStatus {
  status: OrderStatus | null
  orderId: string | null
  submittedAt: string | null
  preparingAt: string | null
  servedAt: string | null
}

function parseDate(value: string | null | undefined): Date | null {
  if (!value) return null
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? null : d
}

function loadPersisted(): PersistedOrderStatus {
  const empty: PersistedOrderStatus = {
    status: null, orderId: null, submittedAt: null, preparingAt: null, servedAt: null,
  }
  try {
    const raw = sessionStorage.getItem(ORDER_STATUS_STORAGE_KEY)
    if (!raw) return empty
    const parsed = JSON.parse(raw) as Partial<PersistedOrderStatus>
    return {
      status: parsed?.status ?? null,
      orderId: parsed?.orderId ?? null,
      submittedAt: parsed?.submittedAt ?? null,
      preparingAt: parsed?.preparingAt ?? null,
      servedAt: parsed?.servedAt ?? null,
    }
  } catch {
    return empty
  }
}

// Single source of truth for the customer's "what stage is my order at?" UI.
// Updated by the SignalR client when the admin moves the order across the
// kanban; OrderSentView reads from here to render the timeline.
export const useOrderStatusStore = defineStore('orderStatus', () => {
  const persisted = loadPersisted()
  const status = ref<OrderStatus | null>(persisted.status)
  const orderId = ref<string | null>(persisted.orderId)

  // Timeline timestamps live here (not in OrderSentView's local state) so they
  // survive the diner leaving the progress screen and re-opening it later from
  // the "view progress" button — otherwise "submitted at" would reset to the
  // moment they re-opened it.
  const submittedAt = ref<Date | null>(parseDate(persisted.submittedAt))
  const preparingAt = ref<Date | null>(parseDate(persisted.preparingAt))
  const servedAt = ref<Date | null>(parseDate(persisted.servedAt))

  // Mirror every change back to sessionStorage. clear() nulls everything, so a
  // finished/abandoned order is persisted as empty too. Dates serialize to ISO
  // strings and are revived via parseDate() on the next load.
  watch(
    [status, orderId, submittedAt, preparingAt, servedAt],
    () => {
      try {
        sessionStorage.setItem(
          ORDER_STATUS_STORAGE_KEY,
          JSON.stringify({
            status: status.value,
            orderId: orderId.value,
            submittedAt: submittedAt.value,
            preparingAt: preparingAt.value,
            servedAt: servedAt.value,
          }),
        )
      } catch (error) {
        console.error('Nu s-a putut salva statusul comenzii în sessionStorage:', error)
      }
    },
  )

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

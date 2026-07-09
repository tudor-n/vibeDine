import { defineStore } from 'pinia'
import { ref } from 'vue'
import { apiClient } from '@/services/api-client'
import type { ActiveHappyHour } from '@shared/types/models'

// Whole seconds remaining until an "HH:mm" wall-clock end time, evaluated against
// the phone's local clock for today. Floors at 0; the 60s poll clears the deal
// shortly after the window lapses, so a stale 00:00 is only shown briefly.
function secondsUntil(endTime: string): number {
  const parts = endTime.split(':')
  const h = Number(parts[0])
  const m = Number(parts[1])
  if (!Number.isFinite(h) || !Number.isFinite(m)) return 0
  const now = new Date()
  const end = new Date(now)
  end.setHours(h, m, 0, 0)
  const diff = Math.floor((end.getTime() - now.getTime()) / 1000)
  return diff > 0 ? diff : 0
}

// The customer-facing happy hour: the restaurant's live deal (or null) used to
// drive the offer popup, the reopen banner and the discounted menu price. Polled
// so the offer appears and disappears at the window edges without a manual
// refresh. Pricing is applied authoritatively server-side at order time; this is
// the matching display.
export const useHappyHourStore = defineStore('happyHour', () => {
  const active = ref<ActiveHappyHour | null>(null)
  // Offer popup (state A) vs. return banner (state B). The popup opens once per
  // distinct deal; once the guest dismisses it we keep it collapsed to the
  // banner for the rest of the window (reopening is manual, via the banner).
  const open = ref(false)
  // Success toast (state C), shown briefly after adding at the happy-hour price.
  const added = ref(false)
  // Live countdown to the window's end, in whole seconds (floored at 0).
  const secs = ref(0)

  let pollTimer: ReturnType<typeof setInterval> | null = null
  let tickTimer: ReturnType<typeof setInterval> | null = null
  let addedTimer: ReturnType<typeof setTimeout> | null = null
  // The deal we've already auto-opened, so re-fetching the same live deal every
  // minute doesn't re-pop the popup after the guest has dismissed it.
  let autoOpenedProductId: string | null = null

  function refreshCountdown() {
    secs.value = active.value ? secondsUntil(active.value.endTime) : 0
  }

  async function fetchActive() {
    try {
      const data = await apiClient.getActiveHappyHour()
      // 204/empty → {} (no productId) → no active happy hour.
      const next =
        data && (data as ActiveHappyHour).productId ? (data as ActiveHappyHour) : null
      active.value = next
      if (!next) {
        open.value = false
        autoOpenedProductId = null
      } else if (next.productId !== autoOpenedProductId) {
        // First sight of this deal → present the popup once.
        autoOpenedProductId = next.productId
        open.value = true
      }
      refreshCountdown()
    } catch {
      active.value = null
      open.value = false
      autoOpenedProductId = null
    }
  }

  // The discounted price for a product if it is the live happy-hour product, else
  // null. Returns the server-computed value so the client and the bill agree.
  function discountedPriceFor(productId: string): number | null {
    return active.value && active.value.productId === productId
      ? active.value.discountedPrice
      : null
  }

  function dismiss() {
    open.value = false
  }

  function reopen() {
    if (active.value) open.value = true
  }

  // Flash the "added at happy-hour price" toast, replacing any pending timer so
  // rapid adds keep it on screen a full ~2.6s from the last add.
  function notifyAdded() {
    added.value = true
    if (addedTimer) clearTimeout(addedTimer)
    addedTimer = setTimeout(() => {
      added.value = false
    }, 2600)
  }

  function startPolling() {
    fetchActive()
    if (!pollTimer) pollTimer = setInterval(fetchActive, 60_000)
    // Tick the countdown independently of the (slow) poll so MM:SS stays live.
    if (!tickTimer) tickTimer = setInterval(refreshCountdown, 1000)
  }

  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
    if (tickTimer) {
      clearInterval(tickTimer)
      tickTimer = null
    }
    if (addedTimer) {
      clearTimeout(addedTimer)
      addedTimer = null
    }
  }

  return {
    active,
    open,
    added,
    secs,
    fetchActive,
    discountedPriceFor,
    dismiss,
    reopen,
    notifyAdded,
    startPolling,
    stopPolling,
  }
})

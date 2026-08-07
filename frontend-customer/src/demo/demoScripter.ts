// Replays, on timers, the SignalR pushes a real session would receive — by
// calling the SAME store actions the handlers in useWebSocket.ts call. Only
// ever invoked from the demo adapter (which only runs in a demo session), so
// no isDemoSession() guards here. Store imports are fine: this module is
// reached exclusively via dynamic import (api-client → adapter → here).
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import { useFlashDealStore, buildFlashDealFromPayload } from '@/stores/useFlashDealStore'
import { useHelpCallStore } from '@/stores/useHelpCallStore'
import { buildDemoFlashDealPayload } from '@/demo/fixtures'

const timers = new Set<ReturnType<typeof setTimeout>>()

function after(ms: number, fn: () => void) {
  const t = setTimeout(() => {
    timers.delete(t)
    fn()
  }, ms)
  timers.add(t)
}

export function cancelDemoTimers() {
  for (const t of timers) clearTimeout(t)
  timers.clear()
}

// The demo kitchen: flash-deal upsell at 8s (the moment you chose — right
// after the order went off), preparing at 15s, ready at 45s. A new submit
// (next round) cancels anything still pending from the previous one so an
// old round's "ready" can't stamp the new round's timeline.
export function startPostSubmitSequence(orderId: string) {
  cancelDemoTimers()
  after(8_000, () => {
    const flashDeal = useFlashDealStore()
    const payload = buildDemoFlashDealPayload(orderId)
    flashDeal.setActiveDeal(buildFlashDealFromPayload(payload), orderId, payload.quantity)
  })
  after(15_000, () => useOrderStatusStore().setStatus(orderId, 'preparing'))
  after(45_000, () => useOrderStatusStore().setStatus(orderId, 'ready'))
}

// The WaiterOnTheWay equivalent: the "waiter" acknowledges after a few seconds.
export function scheduleWaiterOnTheWay() {
  after(6_000, () => useHelpCallStore().clearCooldown())
}

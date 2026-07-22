import { ref } from 'vue'

// Module-level singleton (mirrors useMenu.ts): OrderingView and any other
// consumer share one `expired` flag and one timer, so restarting the
// countdown from one place is visible everywhere without prop drilling.
const expired = ref(false)
let timer: ReturnType<typeof setTimeout> | null = null

function clearTimer() {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
}

/**
 * (Re)starts the link-expiry countdown. Clears any prior timer first, so
 * calling `start` again — e.g. after `orderAgain` re-joins — restarts the
 * window from zero instead of stacking timers.
 */
function start(seconds: number) {
  clearTimer()
  expired.value = false
  timer = setTimeout(() => {
    expired.value = true
    timer = null
  }, seconds * 1000)
}

/** Cancels the countdown without flipping `expired`. */
function stop() {
  clearTimer()
}

export function useLinkExpiry() {
  return { expired, start, stop }
}

import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { apiClient } from '@/services/api-client'
import { useSessionStore } from '@/stores/useSessionStore'

export const COOLDOWN_TOTAL_SECONDS = 120

const STORAGE_KEY = 'helpCall.cooldownEndsAt'

function readStoredCooldown(): number | null {
  const raw = sessionStorage.getItem(STORAGE_KEY)
  if (!raw) return null
  const ms = Number(raw)
  if (!Number.isFinite(ms) || ms <= Date.now()) {
    sessionStorage.removeItem(STORAGE_KEY)
    return null
  }
  return ms
}

export const useHelpCallStore = defineStore('helpCall', () => {
  const cooldownEndsAt = ref<number | null>(readStoredCooldown())
  const isPending = ref(false)
  const lastError = ref<string | null>(null)
  const now = ref(Date.now())

  let ticker: ReturnType<typeof setInterval> | null = null

  function startTicker() {
    if (ticker) return
    now.value = Date.now()
    ticker = setInterval(() => {
      now.value = Date.now()
      if (cooldownEndsAt.value !== null && now.value >= cooldownEndsAt.value) {
        clearCooldown()
      }
    }, 1000)
  }

  function stopTicker() {
    if (ticker) {
      clearInterval(ticker)
      ticker = null
    }
  }

  // Auto start/stop the 1s ticker so we don't burn CPU when no cooldown is
  // active — same approach FlashDealIcon uses for its countdown.
  watch(
    cooldownEndsAt,
    (value) => {
      if (value !== null && value > Date.now()) {
        startTicker()
      } else {
        stopTicker()
      }
    },
    { immediate: true },
  )

  const cooldownRemaining = computed(() => {
    if (cooldownEndsAt.value === null) return 0
    return Math.max(0, Math.ceil((cooldownEndsAt.value - now.value) / 1000))
  })

  const isCoolingDown = computed(() => cooldownRemaining.value > 0)

  function applyCooldown(endsAtMs: number) {
    cooldownEndsAt.value = endsAtMs
    try {
      sessionStorage.setItem(STORAGE_KEY, String(endsAtMs))
    } catch {
      // private mode / disabled storage — fine, in-memory cooldown still works
    }
    now.value = Date.now()
  }

  function clearCooldown() {
    cooldownEndsAt.value = null
    try {
      sessionStorage.removeItem(STORAGE_KEY)
    } catch {
      // ignore
    }
    stopTicker()
  }

  async function callWaiter() {
    if (isPending.value || isCoolingDown.value) return

    const session = useSessionStore()
    const tableId = session.tableId
    if (!tableId) {
      lastError.value = 'no-session'
      return
    }

    isPending.value = true
    lastError.value = null
    try {
      const res = await apiClient.callWaiter(tableId)
      if (res.ok) {
        const endsAt = Date.parse(res.cooldownEndsAt)
        if (Number.isFinite(endsAt)) {
          applyCooldown(endsAt)
        } else {
          applyCooldown(Date.now() + COOLDOWN_TOTAL_SECONDS * 1000)
        }
      } else if (res.status === 429) {
        const retry = res.retryAfterSeconds ?? COOLDOWN_TOTAL_SECONDS
        applyCooldown(Date.now() + retry * 1000)
      } else {
        lastError.value = res.message ?? 'error'
      }
    } catch (err) {
      console.error('callWaiter failed', err)
      lastError.value = 'network'
    } finally {
      isPending.value = false
    }
  }

  return {
    cooldownEndsAt,
    cooldownRemaining,
    isCoolingDown,
    isPending,
    lastError,
    callWaiter,
    applyCooldown,
    clearCooldown,
  }
})

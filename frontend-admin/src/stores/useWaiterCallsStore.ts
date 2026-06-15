import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { apiClient, type WaiterCall } from '@/services/api-client'
import { createStaffHubClient, type StaffHubClient } from '@/services/signalRClient'
import { useToast } from '@/composables/useToast'

/**
 * Global waiter-call queue. Keeps a single StaffHub connection alive for any
 * authenticated staffer (admin or waiter) with an active restaurant, so a new
 * call is surfaced anywhere in the dashboard — via the nav badge, the floating
 * pill in App.vue, a toast, and a chime. Lifecycle is driven from App.vue.
 *
 * The backend broadcasts help events to the `WaitStaff` group; the hub authorizes
 * the join by restaurant access (not role), so admins may join it too — no
 * backend change needed (see HelpController / StaffHub).
 */
export const useWaiterCallsStore = defineStore('waiterCalls', () => {
  const toast = useToast()

  const calls = ref<WaiterCall[]>([])
  const isConnected = ref(false)
  // Track ids separately so duplicate live/seed events never double-insert.
  const seen = new Set<string>()

  let hub: StaffHubClient | null = null
  let currentRestaurantId: string | null = null
  let starting: Promise<void> | null = null

  const count = computed(() => calls.value.length)

  function upsert(c: WaiterCall): boolean {
    if (!c?.requestId || seen.has(c.requestId)) return false
    seen.add(c.requestId)
    calls.value.push(c)
    return true
  }

  function remove(requestId: string) {
    seen.delete(requestId)
    const i = calls.value.findIndex((c) => c.requestId === requestId)
    if (i !== -1) calls.value.splice(i, 1)
  }

  function reset() {
    calls.value = []
    seen.clear()
  }

  async function seed() {
    try {
      const open = await apiClient.getHelpRequests()
      reset()
      for (const c of open) upsert(c)
    } catch {
      // Keep whatever we have; live SignalR events will still arrive.
    }
  }

  async function start(restaurantId: string) {
    // Already connected (or connecting) to this restaurant → nothing to do.
    if (currentRestaurantId === restaurantId && (hub || starting)) return
    // Switching restaurants → tear the old connection down first.
    if (hub || currentRestaurantId) await stop()

    currentRestaurantId = restaurantId
    starting = (async () => {
      await seed()
      const client = createStaffHubClient()

      client.on<WaiterCall>('HelpRequested', (payload) => {
        if (upsert(payload)) {
          const where = payload.tableNumber ? `Masa ${payload.tableNumber}` : 'O masă'
          toast.info(`${where} cheamă un ospătar.`, { title: 'chemare nouă' })
          playChime()
        }
      })

      client.on<{ requestId: string }>('HelpAcknowledged', (payload) => {
        remove(payload.requestId)
      })

      // After a dropped-then-restored connection, re-join the group and re-seed
      // so any calls created (or accepted) while offline are reconciled.
      client.onReconnected(async () => {
        try {
          await client.joinRoleGroup(restaurantId, 'WaitStaff')
          await seed()
        } catch {
          /* will retry on the next reconnect */
        }
      })

      await client.start()
      await client.joinRoleGroup(restaurantId, 'WaitStaff')
      hub = client
      isConnected.value = true
    })()

    try {
      await starting
    } catch {
      // Initial connect failed — reset so a later lifecycle tick can retry cleanly.
      // The REST seed already populated the list, so the UI is still useful.
      currentRestaurantId = null
      isConnected.value = false
      hub = null
    } finally {
      starting = null
    }
  }

  async function stop() {
    if (starting) {
      try {
        await starting
      } catch {
        /* ignore */
      }
    }
    const client = hub
    const rid = currentRestaurantId
    hub = null
    currentRestaurantId = null
    isConnected.value = false
    reset()
    if (!client) return
    try {
      if (rid) await client.leaveRoleGroup(rid, 'WaitStaff')
    } catch {
      /* ignore */
    }
    try {
      await client.stop()
    } catch {
      /* ignore */
    }
  }

  async function acknowledge(requestId: string) {
    const idx = calls.value.findIndex((c) => c.requestId === requestId)
    const snapshot = idx !== -1 ? calls.value[idx] : null
    // Optimistic: drop it immediately; the server also broadcasts HelpAcknowledged
    // so every other staff session clears it too.
    remove(requestId)
    try {
      await apiClient.acknowledgeHelpRequest(requestId)
    } catch (err) {
      if (snapshot) upsert(snapshot)
      toast.error(err instanceof Error ? err.message : 'Nu s-a putut accepta chemarea.')
    }
  }

  return { calls, count, isConnected, start, stop, acknowledge }
})

// ── Chime ────────────────────────────────────────────────────────────────────
// A short, unobtrusive two-tone chime via WebAudio — no audio asset to bundle.
// Best-effort: browsers gate audio until the page has been interacted with, but
// a staffer working the dashboard always has.
let audioCtx: AudioContext | null = null
function playChime() {
  try {
    const Ctx =
      window.AudioContext ||
      (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!Ctx) return
    audioCtx = audioCtx ?? new Ctx()
    const ctx = audioCtx
    if (ctx.state === 'suspended') void ctx.resume()
    const start = ctx.currentTime
    const notes = [880, 1174.7] // A5 → D6
    notes.forEach((freq, i) => {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.value = freq
      const t0 = start + i * 0.16
      gain.gain.setValueAtTime(0.0001, t0)
      gain.gain.exponentialRampToValueAtTime(0.22, t0 + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.22)
      osc.connect(gain).connect(ctx.destination)
      osc.start(t0)
      osc.stop(t0 + 0.24)
    })
  } catch {
    /* audio is strictly best-effort */
  }
}

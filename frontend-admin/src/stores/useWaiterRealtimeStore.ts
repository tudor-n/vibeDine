import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { useAuthStore } from '@/stores/useAuthStore'
import { createStaffHubClient, type StaffHubClient } from '@/services/signalRClient'
import type {
  WaiterFloorResponse,
  WaiterFloorTableDto,
} from '@shared/types/waiter'

/**
 * Waiter Mode realtime store. Owns the live floor model behind the `/waiter/*`
 * views: which tables are free, which carry an open service session, whose
 * session it is, and which products just went unavailable.
 *
 * It follows the useFloorStore / useWaiterCallsStore pattern exactly — **events
 * are triggers, REST is truth**:
 *   REST seed → subscribe → join role group → 250 ms-debounced REST refresh on
 *   every event → rejoin + re-seed on reconnect → 15 s polling fallback.
 *
 * The one exception is `MenuItemAvailabilityChanged`: its payload is complete
 * and authoritative, so it is folded straight into `unavailableProductIds`
 * without a refetch.
 *
 * Lifecycle is driven globally from App.vue (`start(rid)` / `stop()`), like the
 * other two hub-owning stores.
 */

/** Coalescing window for event-triggered refreshes (matches useFloorStore). */
const REFRESH_DEBOUNCE_MS = 250
/** Safety net for missed events (same idea as useMenuImportStore's fallback). */
const POLL_INTERVAL_MS = 15_000

/** Events that only mean "the floor may have changed" — they trigger a refetch. */
const TRIGGER_EVENTS = [
  'TableSessionOpened',
  'TableSessionTransferred',
  'TableSessionClosed',
  'OrderSubmitted',
  'OrderStatusUpdated',
  'OrderFinalized',
  'HelpRequested',
  'HelpAcknowledged',
] as const

/** Availability broadcast — self-contained payload, folded in without a refetch. */
const AVAILABILITY_EVENT = 'MenuItemAvailabilityChanged'

interface AvailabilityPayload {
  items: { productId: string; isAvailable: boolean; outOfStock?: boolean }[]
}

const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005'
const BASE_URL = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl

/**
 * Thin authenticated GET for the floor endpoint.
 *
 * `api-client.ts` keeps `authedFetcher`/`scoped` module-private and is owned by
 * the waiter-mode frontend stream, so this store cannot import them yet. It
 * replicates their two behaviours that matter here: the bearer header and the
 * single refresh-and-retry on 401.
 * TODO(W2 landing): swap for `waiterApi.getFloor()` once that service exists.
 */
async function fetchFloor(restaurantId: string): Promise<WaiterFloorResponse> {
  const auth = useAuthStore()
  const url = `${BASE_URL}/api/restaurants/${restaurantId}/waiter/floor`

  const doFetch = () =>
    fetch(url, {
      headers: {
        'Content-Type': 'application/json',
        ...(auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
      },
    })

  let response = await doFetch()
  if (response.status === 401 && (await auth.tryRefresh())) {
    response = await doFetch()
  }
  if (!response.ok) {
    const body = await response.json().catch(() => ({}))
    throw new Error(body?.message || body?.error || `API Error: ${response.status}`)
  }
  const text = await response.text()
  return (text ? JSON.parse(text) : { zones: [], tables: [] }) as WaiterFloorResponse
}

export const useWaiterRealtimeStore = defineStore('waiterRealtime', () => {
  const auth = useAuthStore()

  const floor = ref<WaiterFloorResponse | null>(null)
  const isConnected = ref(false)
  const isLoading = ref(false)
  // Kept as a plain ref of a Set so a fold replaces it wholesale — Vue does not
  // track Set mutations through a `computed` otherwise.
  const unavailableIds = ref<Set<string>>(new Set())

  let hub: StaffHubClient | null = null
  let currentRestaurantId: string | null = null
  let currentRole: string | null = null
  let starting: Promise<void> | null = null
  let refreshTimer: ReturnType<typeof setTimeout> | null = null
  let pollTimer: ReturnType<typeof setInterval> | null = null

  // ── Derived floor state ─────────────────────────────────────────────────────
  const tables = computed<WaiterFloorTableDto[]>(() => floor.value?.tables ?? [])
  const zones = computed(() => floor.value?.zones ?? [])
  /** Tables whose open session belongs to the signed-in staffer. */
  const myTables = computed(() =>
    tables.value.filter((t) => t.session !== null && t.session.waiterUserId === auth.user?.id),
  )
  const freeTables = computed(() => tables.value.filter((t) => t.session === null))
  /** Live rounds attached to no session, floor-wide — the "Nepreluate" tab badge. */
  const unclaimedOrderCount = computed(() =>
    tables.value.reduce((n, t) => n + (t.unclaimedOrderCount ?? 0), 0),
  )
  const unavailableProductIds = computed<Set<string>>(() => unavailableIds.value)

  function tableById(id: string): WaiterFloorTableDto | undefined {
    return tables.value.find((t) => t.id === id)
  }

  // ── REST (the source of truth) ──────────────────────────────────────────────
  async function refreshFloor() {
    const rid = currentRestaurantId
    if (!rid) return
    isLoading.value = true
    try {
      floor.value = await fetchFloor(rid)
    } catch {
      // Keep the last good floor; the next event or poll reconciles it.
    } finally {
      isLoading.value = false
    }
  }

  /** Coalesce bursts of live events into a single refetch. */
  function scheduleRefresh() {
    if (refreshTimer) return
    refreshTimer = setTimeout(() => {
      refreshTimer = null
      void refreshFloor()
    }, REFRESH_DEBOUNCE_MS)
  }

  /** The availability payload is authoritative — fold it in, don't refetch. */
  function applyAvailability(payload: AvailabilityPayload) {
    const items = payload?.items
    if (!Array.isArray(items)) return
    const next = new Set(unavailableIds.value)
    for (const item of items) {
      if (!item?.productId) continue
      if (item.isAvailable === false || item.outOfStock === true) next.add(item.productId)
      else next.delete(item.productId)
    }
    unavailableIds.value = next
  }

  // ── Realtime lifecycle ──────────────────────────────────────────────────────
  async function start(restaurantId: string) {
    // Already connected (or connecting) to this restaurant → nothing to do.
    if (currentRestaurantId === restaurantId && (hub || starting)) return
    // Switching restaurants → tear the old connection down first.
    if (hub || currentRestaurantId) await stop()

    currentRestaurantId = restaurantId
    // Waiters live in the WaitStaff group; managers/owners watch from admin.
    // Both groups receive the session + availability broadcasts (see StaffHub).
    const role = auth.isWaiter ? 'WaitStaff' : 'admin'
    currentRole = role

    starting = (async () => {
      await refreshFloor()

      const client = createStaffHubClient()
      for (const event of TRIGGER_EVENTS) {
        client.on(event, () => scheduleRefresh())
      }
      client.on<AvailabilityPayload>(AVAILABILITY_EVENT, (payload) => applyAvailability(payload))

      // After a dropped-then-restored connection the group membership is gone and
      // anything that happened offline was missed — rejoin and re-seed from REST.
      client.onReconnected(async () => {
        try {
          await client.joinRoleGroup(restaurantId, role)
          await refreshFloor()
        } catch {
          /* will retry on the next reconnect */
        }
      })

      await client.start()
      await client.joinRoleGroup(restaurantId, role)
      hub = client
      isConnected.value = true
    })()

    try {
      await starting
    } catch {
      // Connect failed — reset so a later lifecycle tick can retry cleanly. The
      // REST seed already populated the floor, so the views are still usable.
      currentRestaurantId = null
      currentRole = null
      isConnected.value = false
      hub = null
    } finally {
      starting = null
    }

    // Poll regardless of whether the hub came up: it is the fallback for both a
    // dead connection and a dropped event.
    if (currentRestaurantId && !pollTimer) {
      pollTimer = setInterval(() => void refreshFloor(), POLL_INTERVAL_MS)
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
    const role = currentRole
    hub = null
    currentRestaurantId = null
    currentRole = null
    isConnected.value = false
    if (refreshTimer) {
      clearTimeout(refreshTimer)
      refreshTimer = null
    }
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
    floor.value = null
    unavailableIds.value = new Set()
    if (!client) return
    try {
      for (const event of TRIGGER_EVENTS) client.off(event)
      client.off(AVAILABILITY_EVENT)
    } catch {
      /* ignore */
    }
    try {
      if (rid && role) await client.leaveRoleGroup(rid, role)
    } catch {
      /* ignore */
    }
    try {
      await client.stop()
    } catch {
      /* ignore */
    }
  }

  return {
    // state
    floor,
    isConnected,
    isLoading,
    // getters
    tables,
    zones,
    myTables,
    freeTables,
    unclaimedOrderCount,
    unavailableProductIds,
    tableById,
    // actions
    start,
    stop,
    refreshFloor,
  }
})

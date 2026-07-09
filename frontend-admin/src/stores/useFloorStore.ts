import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  tablesApi,
  zonesApi,
  apiClient,
  type ActiveOrder,
  type TableSummary,
  type ZoneSummary,
} from '@/services/api-client'
import { createStaffHubClient, type StaffHubClient } from '@/services/signalRClient'
import { useToast } from '@/composables/useToast'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'

/**
 * Floor-plan store. Owns the zones + tables model shown on the "Sală" tab AND the
 * live list of active orders (the single source of truth for the Comenzi board,
 * the floor red dots, and the "Preia comanda" modal). It keeps a StaffHub
 * connection alive — driven globally from App.vue — so order state reacts in real
 * time to OrderSubmitted / OrderClaimed / OrderStatusUpdated / OrderFinalized.
 *
 * Waiter-call overlays are derived from the global useWaiterCallsStore so help
 * requests are tracked in exactly one place.
 */

// Live SignalR payload shapes (camelCase JSON from the StaffHub).
interface OrderSubmittedPayload {
  orderId: string
  orderNumber: string
  tableId: string
  status: string
}
interface OrderEventPayload {
  orderId: string
  tableId?: string
}

export const useFloorStore = defineStore('floor', () => {
  const toast = useToast()
  const waiterCalls = useWaiterCallsStore()

  const zones = ref<ZoneSummary[]>([])
  const tables = ref<TableSummary[]>([])
  // Single source of truth for everything order-related on the floor + board.
  const activeOrders = ref<ActiveOrder[]>([])
  const selectedZoneId = ref<string | null>(null)
  const isConnected = ref(false)

  let hub: StaffHubClient | null = null
  let currentRestaurantId: string | null = null
  let refreshTimer: ReturnType<typeof setTimeout> | null = null

  // ── Derived order state ──────────────────────────────────────────────────────
  const activeOrderTableIds = computed(
    () => new Set(activeOrders.value.map((o) => o.tableId)),
  )
  const unclaimedOrders = computed(() => activeOrders.value.filter((o) => !o.claimedByWaiterId))
  const unclaimedCount = computed(() => unclaimedOrders.value.length)
  const unclaimedTableIds = computed(
    () => new Set(unclaimedOrders.value.map((o) => o.tableId)),
  )
  // Zones (categories) that have at least one table with an unclaimed order —
  // powers the red dot on off-screen zone tabs and the sidebar "Sala" entry.
  const zoneIdsWithUnclaimed = computed(() => {
    const tableZone = new Map(tables.value.map((t) => [t.id, t.zoneId]))
    const set = new Set<string>()
    for (const tableId of unclaimedTableIds.value) {
      const zoneId = tableZone.get(tableId)
      if (zoneId) set.add(zoneId)
    }
    return set
  })

  // Table bells come straight from the global waiter-call queue.
  const waiterCallTableIds = computed(
    () => new Set(waiterCalls.calls.map((c) => c.tableId)),
  )

  function ordersForTable(tableId: string): ActiveOrder[] {
    return activeOrders.value.filter((o) => o.tableId === tableId)
  }

  function tableNumberOf(tableId: string): string | null {
    return tables.value.find((t) => t.id === tableId)?.tableNumber ?? null
  }

  // ── Zones ───────────────────────────────────────────────────────────────────
  async function fetchZones() {
    const list = await zonesApi.list()
    list.sort((a, b) => a.sortOrder - b.sortOrder)
    zones.value = list
    if (selectedZoneId.value === null && list.length > 0) {
      selectedZoneId.value = list[0].id
    }
  }

  async function fetchTables() {
    tables.value = await tablesApi.list()
  }

  async function createZone(name: string): Promise<ZoneSummary> {
    const created = await zonesApi.create(name)
    zones.value = [...zones.value, created].sort((a, b) => a.sortOrder - b.sortOrder)
    if (selectedZoneId.value === null) selectedZoneId.value = created.id
    return created
  }

  async function renameZone(id: string, name: string) {
    const updated = await zonesApi.update(id, { name })
    const idx = zones.value.findIndex((z) => z.id === id)
    if (idx !== -1) zones.value[idx] = updated
  }

  async function reorderZones(orderedIds: string[]) {
    // Persist each zone's new index as its sortOrder, then reflect locally.
    await Promise.all(orderedIds.map((id, sortOrder) => zonesApi.update(id, { sortOrder })))
    const byId = new Map(zones.value.map((z) => [z.id, z]))
    zones.value = orderedIds
      .map((id, sortOrder) => {
        const z = byId.get(id)
        return z ? { ...z, sortOrder } : null
      })
      .filter((z): z is ZoneSummary => z !== null)
  }

  async function deleteZone(id: string) {
    await zonesApi.remove(id)
    if (selectedZoneId.value === id) selectedZoneId.value = null
    // Tables in the deleted zone get unassigned server-side — refetch both.
    await Promise.all([fetchZones(), fetchTables()])
  }

  // ── Tables ──────────────────────────────────────────────────────────────────
  async function createTable(payload: {
    tableNumber: string
    zoneId?: string | null
    positionX?: number
    positionY?: number
    capacity?: number
    shape?: string
  }): Promise<TableSummary> {
    const created = await tablesApi.create(payload)
    tables.value = [...tables.value, created]
    return created
  }

  async function updateTablePosition(id: string, x: number, y: number) {
    const updated = await tablesApi.update(id, { positionX: x, positionY: y })
    replaceTable(id, updated)
  }

  async function moveTableToZone(id: string, zoneId: string | null) {
    const updated = await tablesApi.update(id, { zoneId })
    replaceTable(id, updated)
  }

  async function renameTable(id: string, tableNumber: string) {
    const updated = await tablesApi.update(id, { tableNumber })
    replaceTable(id, updated)
  }

  async function setTableCapacity(id: string, capacity: number) {
    const updated = await tablesApi.update(id, { capacity })
    replaceTable(id, updated)
  }

  async function setTableShape(id: string, shape: string) {
    const updated = await tablesApi.update(id, { shape })
    replaceTable(id, updated)
  }

  async function deleteTable(id: string) {
    await tablesApi.remove(id)
    tables.value = tables.value.filter((t) => t.id !== id)
  }

  function replaceTable(id: string, updated: TableSummary) {
    const idx = tables.value.findIndex((t) => t.id === id)
    if (idx !== -1) tables.value[idx] = updated
  }

  // ── Active orders ─────────────────────────────────────────────────────────────
  async function refreshActiveOrders() {
    try {
      activeOrders.value = await apiClient.getActiveOrders()
    } catch {
      // Keep whatever we have; the next live event or poll will reconcile.
    }
  }

  // Coalesce bursts of live events into a single refetch.
  function scheduleRefresh() {
    if (refreshTimer) return
    refreshTimer = setTimeout(() => {
      refreshTimer = null
      void refreshActiveOrders()
    }, 250)
  }

  function patchOrder(orderId: string, patch: Partial<ActiveOrder>) {
    const idx = activeOrders.value.findIndex((o) => o.id === orderId)
    if (idx !== -1) activeOrders.value[idx] = { ...activeOrders.value[idx], ...patch }
  }

  async function claim(orderId: string) {
    const res = await apiClient.claimOrder(orderId)
    patchOrder(orderId, {
      claimedByWaiterId: res.claimedByWaiterId,
      claimedByName: res.claimedByName ?? null,
      claimedAt: res.claimedAt ?? null,
    })
    return res
  }

  async function release(orderId: string) {
    const res = await apiClient.releaseOrder(orderId)
    patchOrder(orderId, { claimedByWaiterId: null, claimedByName: null, claimedAt: null })
    return res
  }

  // ── Realtime lifecycle ──────────────────────────────────────────────────────
  async function startRealtime(restaurantId: string) {
    if (currentRestaurantId === restaurantId && hub) return
    if (hub || currentRestaurantId) await stopRealtime()

    currentRestaurantId = restaurantId
    await refreshActiveOrders()

    const client = createStaffHubClient()

    client.on<OrderSubmittedPayload>('OrderSubmitted', (payload) => {
      announceOrder(payload.tableId)
      scheduleRefresh()
    })
    client.on<OrderEventPayload>('OrderClaimed', () => scheduleRefresh())
    client.on<OrderEventPayload>('OrderStatusUpdated', () => scheduleRefresh())
    client.on<OrderEventPayload>('OrderFinalized', () => scheduleRefresh())

    client.onReconnected(async () => {
      try {
        await client.joinAdminGroup(restaurantId)
        await refreshActiveOrders()
      } catch {
        /* will retry on the next reconnect */
      }
    })

    try {
      await client.start()
      // The StaffHub authorizes the admin group by restaurant access (not role),
      // so waiters may join it too — they need OrderSubmitted/Claimed/Finalized.
      await client.joinAdminGroup(restaurantId)
      hub = client
      isConnected.value = true
    } catch {
      // Connect failed — reset so a later tick can retry. The REST refresh already
      // populated the orders, so the floor/board are still useful.
      currentRestaurantId = null
      isConnected.value = false
      hub = null
    }
  }

  async function stopRealtime() {
    const client = hub
    const rid = currentRestaurantId
    hub = null
    currentRestaurantId = null
    isConnected.value = false
    if (refreshTimer) {
      clearTimeout(refreshTimer)
      refreshTimer = null
    }
    if (!client) return
    try {
      client.off('OrderSubmitted')
      client.off('OrderClaimed')
      client.off('OrderStatusUpdated')
      client.off('OrderFinalized')
    } catch {
      /* ignore */
    }
    try {
      if (rid) await client.leaveAdminGroup(rid)
    } catch {
      /* ignore */
    }
    try {
      await client.stop()
    } catch {
      /* ignore */
    }
  }

  // ── Announcements (toast + chime) ────────────────────────────────────────────
  function announceOrder(tableId: string) {
    const num = tableNumberOf(tableId)
    const where = num ? `Masa ${num}` : 'O masă'
    toast.info(`${where} a trimis o comandă.`, { title: 'comandă nouă' })
    playChime()
  }

  return {
    // state
    zones,
    tables,
    activeOrders,
    selectedZoneId,
    isConnected,
    // derived
    activeOrderTableIds,
    unclaimedOrders,
    unclaimedCount,
    unclaimedTableIds,
    zoneIdsWithUnclaimed,
    waiterCallTableIds,
    ordersForTable,
    // zones
    fetchZones,
    createZone,
    renameZone,
    reorderZones,
    deleteZone,
    // tables
    fetchTables,
    createTable,
    updateTablePosition,
    moveTableToZone,
    renameTable,
    setTableCapacity,
    setTableShape,
    deleteTable,
    // orders
    refreshActiveOrders,
    claim,
    release,
    // realtime
    startRealtime,
    stopRealtime,
  }
})

// ── Chime ──────────────────────────────────────────────────────────────────────
// A short, unobtrusive beep via WebAudio — no audio asset to bundle. Best-effort;
// guarded so headless/test environments without AudioContext stay quiet.
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
    const t0 = ctx.currentTime
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.type = 'sine'
    osc.frequency.value = 988 // ~B5
    gain.gain.setValueAtTime(0.0001, t0)
    gain.gain.exponentialRampToValueAtTime(0.2, t0 + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.12)
    osc.connect(gain).connect(ctx.destination)
    osc.start(t0)
    osc.stop(t0 + 0.14)
    osc.onended = () => {
      try {
        void ctx.close()
      } catch {
        /* ignore */
      }
      audioCtx = null
    }
  } catch {
    /* audio is strictly best-effort */
  }
}

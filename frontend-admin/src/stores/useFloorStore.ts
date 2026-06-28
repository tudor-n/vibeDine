import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
  tablesApi,
  zonesApi,
  apiClient,
  type TableSummary,
  type ZoneSummary,
} from '@/services/api-client'
import { createStaffHubClient, type StaffHubClient } from '@/services/signalRClient'
import { useToast } from '@/composables/useToast'

/**
 * Floor-plan store. Owns the zones + tables model shown on the "Sală" tab and
 * overlays live state on top of it: which tables currently have an active order
 * and which are calling a waiter. It keeps a single StaffHub connection alive
 * (admin + WaitStaff groups) so the floor reacts in real time to OrderSubmitted /
 * OrderFinalized / HelpRequested / HelpAcknowledged events. Lifecycle mirrors
 * useWaiterCallsStore.
 */

// Live SignalR payload shapes (camelCase JSON from the StaffHub).
interface OrderSubmittedPayload {
  orderId: string
  orderNumber: string
  tableId: string
  status: string
}
interface OrderFinalizedPayload {
  orderId: string
  orderNumber: string
  tableId: string
}
interface HelpRequestedPayload {
  requestId: string
  tableId: string
  tableNumber: string | null
  createdAt: string
}
interface HelpAcknowledgedPayload {
  requestId: string
}

export const useFloorStore = defineStore('floor', () => {
  const toast = useToast()

  const zones = ref<ZoneSummary[]>([])
  const tables = ref<TableSummary[]>([])
  // Live overlays — reassigned (not mutated) on every change so refs stay reactive.
  const activeOrderTableIds = ref<Set<string>>(new Set<string>())
  const waiterCallTableIds = ref<Set<string>>(new Set<string>())
  // requestId → tableId, so a HelpAcknowledged (which only carries requestId)
  // can clear the right table.
  const helpReqToTable = ref<Map<string, string>>(new Map<string, string>())
  const selectedZoneId = ref<string | null>(null)
  const isConnected = ref(false)

  let hub: StaffHubClient | null = null
  let currentRestaurantId: string | null = null

  // ── Set helpers (reassign for reactivity) ──────────────────────────────────
  function addActiveOrder(tableId: string) {
    if (!tableId) return
    const next = new Set(activeOrderTableIds.value)
    next.add(tableId)
    activeOrderTableIds.value = next
  }
  function removeActiveOrder(tableId: string) {
    if (!activeOrderTableIds.value.has(tableId)) return
    const next = new Set(activeOrderTableIds.value)
    next.delete(tableId)
    activeOrderTableIds.value = next
  }
  function addWaiterCall(requestId: string, tableId: string) {
    if (!tableId) return
    const next = new Set(waiterCallTableIds.value)
    next.add(tableId)
    waiterCallTableIds.value = next
    if (requestId) helpReqToTable.value.set(requestId, tableId)
  }
  function removeWaiterCallByRequest(requestId: string) {
    const tableId = helpReqToTable.value.get(requestId)
    helpReqToTable.value.delete(requestId)
    if (!tableId) return
    // Only clear the table flag if no other open request still points at it.
    const stillCalled = [...helpReqToTable.value.values()].includes(tableId)
    if (stillCalled) return
    const next = new Set(waiterCallTableIds.value)
    next.delete(tableId)
    waiterCallTableIds.value = next
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

  async function deleteTable(id: string) {
    await tablesApi.remove(id)
    tables.value = tables.value.filter((t) => t.id !== id)
  }

  function replaceTable(id: string, updated: TableSummary) {
    const idx = tables.value.findIndex((t) => t.id === id)
    if (idx !== -1) tables.value[idx] = updated
  }

  // ── Live state seeding ──────────────────────────────────────────────────────
  async function seedLiveState() {
    try {
      const active = await apiClient.getActiveOrders()
      const orderIds = new Set<string>()
      for (const o of active) {
        if (o?.tableId) orderIds.add(o.tableId as string)
      }
      activeOrderTableIds.value = orderIds
    } catch {
      // Keep whatever we have; live events will reconcile.
    }
    try {
      const open = await apiClient.getHelpRequests()
      const callIds = new Set<string>()
      const map = new Map<string, string>()
      for (const c of open) {
        if (c?.tableId) {
          callIds.add(c.tableId)
          if (c.requestId) map.set(c.requestId, c.tableId)
        }
      }
      waiterCallTableIds.value = callIds
      helpReqToTable.value = map
    } catch {
      // Keep whatever we have; live events will reconcile.
    }
  }

  // ── Realtime lifecycle ──────────────────────────────────────────────────────
  async function startRealtime(restaurantId: string) {
    if (currentRestaurantId === restaurantId && hub) return
    if (hub || currentRestaurantId) await stopRealtime()

    currentRestaurantId = restaurantId
    await seedLiveState()

    const client = createStaffHubClient()

    client.on<OrderSubmittedPayload>('OrderSubmitted', (payload) => {
      addActiveOrder(payload.tableId)
      announceOrder(payload.tableId)
    })

    client.on<OrderFinalizedPayload>('OrderFinalized', (payload) => {
      removeActiveOrder(payload.tableId)
    })

    client.on<HelpRequestedPayload>('HelpRequested', (payload) => {
      addWaiterCall(payload.requestId, payload.tableId)
      announceCall(payload.tableId)
    })

    client.on<HelpAcknowledgedPayload>('HelpAcknowledged', (payload) => {
      removeWaiterCallByRequest(payload.requestId)
    })

    client.onReconnected(async () => {
      try {
        await client.joinAdminGroup(restaurantId)
        await client.joinRoleGroup(restaurantId, 'WaitStaff')
        await seedLiveState()
      } catch {
        /* will retry on the next reconnect */
      }
    })

    try {
      await client.start()
      await client.joinAdminGroup(restaurantId)
      await client.joinRoleGroup(restaurantId, 'WaitStaff')
      hub = client
      isConnected.value = true
    } catch {
      // Connect failed — reset so a later tick can retry. REST seed already
      // populated the overlays, so the floor is still useful.
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
    if (!client) return
    try {
      client.off('OrderSubmitted')
      client.off('OrderFinalized')
      client.off('HelpRequested')
      client.off('HelpAcknowledged')
    } catch {
      /* ignore */
    }
    try {
      if (rid) {
        await client.leaveAdminGroup(rid)
        await client.leaveRoleGroup(rid, 'WaitStaff')
      }
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

  function announceCall(tableId: string) {
    const num = tableNumberOf(tableId)
    const where = num ? `Masa ${num}` : 'O masă'
    toast.success(`${where} cheamă un ospătar.`, { title: 'chemare nouă' })
    playChime()
  }

  return {
    // state
    zones,
    tables,
    activeOrderTableIds,
    waiterCallTableIds,
    helpReqToTable,
    selectedZoneId,
    isConnected,
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
    deleteTable,
    // realtime
    seedLiveState,
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

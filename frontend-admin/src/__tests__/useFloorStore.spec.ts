// @vitest-environment jsdom
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// ── Mock the auth store before importing the store under test ───────────────
vi.mock('@/stores/useAuthStore', () => ({
  useAuthStore: () => ({
    activeRestaurantId: 'rest-123',
    accessToken: 'tok-abc',
  }),
}))

// ── Mock the SignalR client ─────────────────────────────────────────────────
// Capture the registered event + reconnect handlers so tests invoke them directly.
const mockHandlers: Record<string, (payload: unknown) => void> = {}
const mockReconnectHandlers: Array<() => void | Promise<void>> = []
const mockHub = {
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  joinAdminGroup: vi.fn().mockResolvedValue(undefined),
  leaveAdminGroup: vi.fn().mockResolvedValue(undefined),
  joinRoleGroup: vi.fn().mockResolvedValue(undefined),
  leaveRoleGroup: vi.fn().mockResolvedValue(undefined),
  on: vi.fn().mockImplementation((event: string, handler: (p: unknown) => void) => {
    mockHandlers[event] = handler
  }),
  off: vi.fn(),
  onReconnected: vi.fn().mockImplementation((handler: () => void | Promise<void>) => {
    mockReconnectHandlers.push(handler)
  }),
}

vi.mock('@/services/signalRClient', () => ({
  createStaffHubClient: vi.fn(() => mockHub),
}))

// ── Mock the API client ──────────────────────────────────────────────────────
vi.mock('@/services/api-client', () => ({
  tablesApi: {
    list: vi.fn().mockResolvedValue([]),
    create: vi.fn(),
    update: vi.fn(),
    regenerate: vi.fn(),
    remove: vi.fn().mockResolvedValue(undefined),
  },
  zonesApi: {
    list: vi.fn().mockResolvedValue([]),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn().mockResolvedValue(undefined),
  },
  apiClient: {
    getActiveOrders: vi.fn().mockResolvedValue([]),
    getHelpRequests: vi.fn().mockResolvedValue([]),
    claimOrder: vi.fn(),
    releaseOrder: vi.fn(),
  },
}))

import { tablesApi, zonesApi, apiClient } from '@/services/api-client'
import { useFloorStore } from '@/stores/useFloorStore'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'

const order = (over: Record<string, unknown> = {}) => ({
  id: 'o1',
  orderNumber: 1,
  status: 'submitted',
  totalAmount: 0,
  createdAt: '',
  readyAt: null,
  tableId: 't-1',
  tableNumber: '1',
  claimedByWaiterId: null,
  claimedByName: null,
  claimedAt: null,
  items: [],
  ...over,
})

const mockTablesApi = tablesApi as unknown as {
  list: ReturnType<typeof vi.fn>
  create: ReturnType<typeof vi.fn>
  update: ReturnType<typeof vi.fn>
  remove: ReturnType<typeof vi.fn>
}
const mockZonesApi = zonesApi as unknown as {
  list: ReturnType<typeof vi.fn>
  create: ReturnType<typeof vi.fn>
  update: ReturnType<typeof vi.fn>
  remove: ReturnType<typeof vi.fn>
}
const mockApiClient = apiClient as unknown as {
  getActiveOrders: ReturnType<typeof vi.fn>
  getHelpRequests: ReturnType<typeof vi.fn>
  claimOrder: ReturnType<typeof vi.fn>
  releaseOrder: ReturnType<typeof vi.fn>
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  for (const k of Object.keys(mockHandlers)) delete mockHandlers[k]
  mockReconnectHandlers.length = 0
  // Re-establish handler-capturing implementations (a previous restore/reset
  // may have stripped them).
  mockHub.on.mockImplementation((event: string, handler: (p: unknown) => void) => {
    mockHandlers[event] = handler
  })
  mockHub.onReconnected.mockImplementation((handler: () => void | Promise<void>) => {
    mockReconnectHandlers.push(handler)
  })
  // Re-arm default resolved values.
  mockHub.start.mockResolvedValue(undefined)
  mockHub.stop.mockResolvedValue(undefined)
  mockHub.joinAdminGroup.mockResolvedValue(undefined)
  mockHub.leaveAdminGroup.mockResolvedValue(undefined)
  mockHub.joinRoleGroup.mockResolvedValue(undefined)
  mockHub.leaveRoleGroup.mockResolvedValue(undefined)
  mockTablesApi.list.mockResolvedValue([])
  mockTablesApi.remove.mockResolvedValue(undefined)
  mockZonesApi.list.mockResolvedValue([])
  mockZonesApi.remove.mockResolvedValue(undefined)
  mockApiClient.getActiveOrders.mockResolvedValue([])
  mockApiClient.getHelpRequests.mockResolvedValue([])
  mockApiClient.claimOrder.mockReset()
  mockApiClient.releaseOrder.mockReset()
  // playChime must be a no-op in tests.
  ;(window as unknown as { AudioContext?: unknown }).AudioContext = undefined
  ;(globalThis as unknown as { AudioContext?: unknown }).AudioContext = undefined
})

describe('useFloorStore — active orders', () => {
  it('seeds active orders on start and derives claimed/unclaimed sets', async () => {
    mockApiClient.getActiveOrders.mockResolvedValueOnce([
      order({ id: 'o1', tableId: 't-1', claimedByWaiterId: null }),
      order({ id: 'o2', tableId: 't-2', claimedByWaiterId: 'u9', claimedByName: 'Ana' }),
    ])

    const store = useFloorStore()
    await store.startRealtime('rest-123')

    expect(store.activeOrderTableIds.has('t-1')).toBe(true)
    expect(store.activeOrderTableIds.has('t-2')).toBe(true)
    expect(store.unclaimedTableIds.has('t-1')).toBe(true)
    expect(store.unclaimedTableIds.has('t-2')).toBe(false)
    expect(store.unclaimedCount).toBe(1)
  })

  it('registers the order event handlers and joins only the admin group', async () => {
    const store = useFloorStore()
    await store.startRealtime('rest-123')

    expect(mockHub.joinAdminGroup).toHaveBeenCalledWith('rest-123')
    expect(mockHub.joinRoleGroup).not.toHaveBeenCalled()
    expect(store.isConnected).toBe(true)
    for (const ev of ['OrderSubmitted', 'OrderClaimed', 'OrderStatusUpdated', 'OrderFinalized']) {
      expect(typeof mockHandlers[ev]).toBe('function')
    }
  })

  it('a live order event triggers a debounced refetch', async () => {
    vi.useFakeTimers()
    try {
      const store = useFloorStore()
      await store.startRealtime('rest-123')

      mockApiClient.getActiveOrders.mockResolvedValueOnce([order({ id: 'o1', tableId: 't-7' })])
      mockHandlers['OrderSubmitted']({ orderId: 'o1', orderNumber: '1', tableId: 't-7', status: 'submitted' })
      await vi.advanceTimersByTimeAsync(300)
      expect(store.activeOrderTableIds.has('t-7')).toBe(true)

      mockApiClient.getActiveOrders.mockResolvedValueOnce([])
      mockHandlers['OrderFinalized']({ orderId: 'o1', tableId: 't-7' })
      await vi.advanceTimersByTimeAsync(300)
      expect(store.activeOrderTableIds.has('t-7')).toBe(false)
    } finally {
      vi.useRealTimers()
    }
  })

  it('claim and release update the order claim state', async () => {
    mockApiClient.getActiveOrders.mockResolvedValueOnce([order({ id: 'o1', tableId: 't-1' })])
    mockApiClient.claimOrder.mockResolvedValueOnce({
      id: 'o1',
      claimedByWaiterId: 'me',
      claimedByName: 'Eu',
      claimedAt: '2026-06-29T10:00:00Z',
    })
    mockApiClient.releaseOrder.mockResolvedValueOnce({ id: 'o1', claimedByWaiterId: null })

    const store = useFloorStore()
    await store.startRealtime('rest-123')
    expect(store.unclaimedCount).toBe(1)

    await store.claim('o1')
    expect(mockApiClient.claimOrder).toHaveBeenCalledWith('o1')
    expect(store.unclaimedCount).toBe(0)

    await store.release('o1')
    expect(mockApiClient.releaseOrder).toHaveBeenCalledWith('o1')
    expect(store.unclaimedCount).toBe(1)
  })

  it('waiterCallTableIds derives from the waiter-calls store', () => {
    const store = useFloorStore()
    const calls = useWaiterCallsStore()
    calls.calls.push({ requestId: 'r1', tableId: 't-2', tableNumber: '5', createdAt: '' })

    expect(store.waiterCallTableIds.has('t-2')).toBe(true)
  })

  it('onReconnected re-seeds active orders', async () => {
    const store = useFloorStore()
    await store.startRealtime('rest-123')

    mockApiClient.getActiveOrders.mockResolvedValueOnce([order({ id: 'o1', tableId: 't-x' })])
    expect(mockReconnectHandlers).toHaveLength(1)
    await mockReconnectHandlers[0]()

    expect(store.activeOrderTableIds.has('t-x')).toBe(true)
  })
})

describe('useFloorStore — CRUD actions call the api-client', () => {
  it('createZone calls zonesApi.create and stores the result', async () => {
    mockZonesApi.create.mockResolvedValueOnce({ id: 'z1', name: 'Terasă', sortOrder: 0 })
    const store = useFloorStore()
    await store.createZone('Terasă')

    expect(mockZonesApi.create).toHaveBeenCalledWith('Terasă')
    expect(store.zones).toEqual([{ id: 'z1', name: 'Terasă', sortOrder: 0 }])
    expect(store.selectedZoneId).toBe('z1')
  })

  it('createTable calls tablesApi.create with the payload and pushes the result', async () => {
    const created = {
      id: 't1',
      tableNumber: '7',
      qrToken: 'q',
      zoneId: 'z1',
      positionX: 10,
      positionY: 20,
    }
    mockTablesApi.create.mockResolvedValueOnce(created)
    const store = useFloorStore()
    const payload = { tableNumber: '7', zoneId: 'z1', positionX: 10, positionY: 20 }
    await store.createTable(payload)

    expect(mockTablesApi.create).toHaveBeenCalledWith(payload)
    expect(store.tables).toContainEqual(created)
  })

  it('updateTablePosition calls tablesApi.update with positionX/positionY', async () => {
    const updated = {
      id: 't1',
      tableNumber: '7',
      qrToken: 'q',
      zoneId: 'z1',
      positionX: 99,
      positionY: 42,
    }
    mockTablesApi.update.mockResolvedValueOnce(updated)
    const store = useFloorStore()
    store.tables = [
      { id: 't1', tableNumber: '7', qrToken: 'q', zoneId: 'z1', positionX: 0, positionY: 0 },
    ]
    await store.updateTablePosition('t1', 99, 42)

    expect(mockTablesApi.update).toHaveBeenCalledWith('t1', { positionX: 99, positionY: 42 })
    expect(store.tables[0]).toEqual(updated)
  })

  it('fetchZones sorts by sortOrder and selects the first zone', async () => {
    mockZonesApi.list.mockResolvedValueOnce([
      { id: 'z2', name: 'B', sortOrder: 2 },
      { id: 'z1', name: 'A', sortOrder: 1 },
    ])
    const store = useFloorStore()
    await store.fetchZones()

    expect(store.zones.map((z) => z.id)).toEqual(['z1', 'z2'])
    expect(store.selectedZoneId).toBe('z1')
  })

  it('deleteZone removes via api then refetches zones and tables', async () => {
    const store = useFloorStore()
    await store.deleteZone('z1')

    expect(mockZonesApi.remove).toHaveBeenCalledWith('z1')
    expect(mockZonesApi.list).toHaveBeenCalled()
    expect(mockTablesApi.list).toHaveBeenCalled()
  })
})

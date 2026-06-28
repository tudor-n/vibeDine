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
  },
}))

import { tablesApi, zonesApi, apiClient } from '@/services/api-client'
import { useFloorStore } from '@/stores/useFloorStore'

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
  // playChime must be a no-op in tests.
  ;(window as unknown as { AudioContext?: unknown }).AudioContext = undefined
  ;(globalThis as unknown as { AudioContext?: unknown }).AudioContext = undefined
})

describe('useFloorStore — realtime overlays', () => {
  it('OrderSubmitted adds the tableId, OrderFinalized removes it', async () => {
    const store = useFloorStore()
    await store.startRealtime('rest-123')

    mockHandlers['OrderSubmitted']({
      orderId: 'o1',
      orderNumber: '1',
      tableId: 't-1',
      status: 'submitted',
    })
    expect(store.activeOrderTableIds.has('t-1')).toBe(true)

    mockHandlers['OrderFinalized']({ orderId: 'o1', orderNumber: '1', tableId: 't-1' })
    expect(store.activeOrderTableIds.has('t-1')).toBe(false)
  })

  it('HelpRequested adds to waiterCallTableIds, HelpAcknowledged removes it by requestId', async () => {
    const store = useFloorStore()
    await store.startRealtime('rest-123')

    mockHandlers['HelpRequested']({
      requestId: 'req-9',
      tableId: 't-2',
      tableNumber: '5',
      createdAt: '2026-06-27T10:00:00Z',
    })
    expect(store.waiterCallTableIds.has('t-2')).toBe(true)
    expect(store.helpReqToTable.get('req-9')).toBe('t-2')

    mockHandlers['HelpAcknowledged']({ requestId: 'req-9' })
    expect(store.waiterCallTableIds.has('t-2')).toBe(false)
    expect(store.helpReqToTable.has('req-9')).toBe(false)
  })

  it('joins both admin and WaitStaff groups and seeds live state on start', async () => {
    mockApiClient.getActiveOrders.mockResolvedValueOnce([{ tableId: 't-a' }, { tableId: 't-b' }])
    mockApiClient.getHelpRequests.mockResolvedValueOnce([
      { requestId: 'r1', tableId: 't-c', tableNumber: '3', createdAt: '' },
    ])

    const store = useFloorStore()
    await store.startRealtime('rest-123')

    expect(mockHub.joinAdminGroup).toHaveBeenCalledWith('rest-123')
    expect(mockHub.joinRoleGroup).toHaveBeenCalledWith('rest-123', 'WaitStaff')
    expect(store.isConnected).toBe(true)
    expect(store.activeOrderTableIds.has('t-a')).toBe(true)
    expect(store.activeOrderTableIds.has('t-b')).toBe(true)
    expect(store.waiterCallTableIds.has('t-c')).toBe(true)
  })

  it('onReconnected re-seeds live state', async () => {
    const store = useFloorStore()
    await store.startRealtime('rest-123')

    mockApiClient.getActiveOrders.mockResolvedValueOnce([{ tableId: 't-x' }])
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

// @vitest-environment jsdom
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { WaiterFloorResponse, WaiterFloorTableDto } from '@shared/types/waiter'

// ── Mock the auth store before importing the store under test ───────────────
// A plain mutable object: the store reads `accessToken` / `user` / `isWaiter`
// off it, and tests flip them in beforeEach.
const authState = {
  accessToken: 'tok-abc' as string | null,
  activeRestaurantId: 'rest-123' as string | null,
  user: { id: 'u-me' } as { id: string } | null,
  isWaiter: true,
  tryRefresh: vi.fn().mockResolvedValue(false),
}
vi.mock('@/stores/useAuthStore', () => ({ useAuthStore: () => authState }))

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

import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'

// ── Fixtures ─────────────────────────────────────────────────────────────────
const session = (over: Record<string, unknown> = {}) => ({
  id: 's-1',
  tableId: 't-1',
  tableNumber: '1',
  waiterUserId: 'u-me',
  waiterName: 'Ana',
  status: 'open',
  openedAt: '2026-08-05T10:00:00Z',
  closedAt: null,
  closedReason: null,
  paymentMethod: null,
  cashAmount: null,
  cardAmount: null,
  totalAmount: null,
  activeOrderCount: 1,
  cumulativeTotal: 42.5,
  ...over,
})

const table = (over: Record<string, unknown> = {}): WaiterFloorTableDto =>
  ({
    id: 't-1',
    tableNumber: '1',
    zoneId: 'z-1',
    capacity: 4,
    shape: 'square',
    positionX: 0,
    positionY: 0,
    session: null,
    hasOpenHelpRequest: false,
    activeOrderCount: 0,
    ...over,
  }) as WaiterFloorTableDto

const floorPayload = (tables: WaiterFloorTableDto[] = []): WaiterFloorResponse => ({
  zones: [{ id: 'z-1', name: 'Terasă', sortOrder: 0 }],
  tables,
})

// ── fetch double (the store's REST seed) ────────────────────────────────────
const mockFetch = vi.fn()

function jsonOnce(body: unknown, status = 200) {
  mockFetch.mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    text: async () => JSON.stringify(body),
    json: async () => body,
  })
}

/** The URL the store is expected to hit — asserted as a suffix so a local
 *  `.env` overriding VITE_API_URL cannot make the suite flaky. */
const FLOOR_PATH = '/api/restaurants/rest-123/waiter/floor'

const TRIGGER_EVENTS = [
  'TableSessionOpened',
  'TableSessionTransferred',
  'TableSessionClosed',
  'OrderSubmitted',
  'OrderStatusUpdated',
  'OrderFinalized',
  'HelpRequested',
  'HelpAcknowledged',
]

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  for (const k of Object.keys(mockHandlers)) delete mockHandlers[k]
  mockReconnectHandlers.length = 0
  // Re-establish handler-capturing implementations (clearAllMocks strips them).
  mockHub.on.mockImplementation((event: string, handler: (p: unknown) => void) => {
    mockHandlers[event] = handler
  })
  mockHub.onReconnected.mockImplementation((handler: () => void | Promise<void>) => {
    mockReconnectHandlers.push(handler)
  })
  mockHub.start.mockResolvedValue(undefined)
  mockHub.stop.mockResolvedValue(undefined)
  mockHub.joinRoleGroup.mockResolvedValue(undefined)
  mockHub.leaveRoleGroup.mockResolvedValue(undefined)

  authState.accessToken = 'tok-abc'
  authState.activeRestaurantId = 'rest-123'
  authState.user = { id: 'u-me' }
  authState.isWaiter = true
  authState.tryRefresh.mockResolvedValue(false)

  mockFetch.mockReset()
  // Default: an empty floor, for tests that don't care about the seed body.
  mockFetch.mockResolvedValue({
    ok: true,
    status: 200,
    text: async () => JSON.stringify(floorPayload()),
    json: async () => floorPayload(),
  })
  vi.stubGlobal('fetch', mockFetch)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('useWaiterRealtimeStore — seed & connect', () => {
  it('seeds the floor over REST and joins the WaitStaff group for a waiter', async () => {
    jsonOnce(floorPayload([table({ id: 't-1' }), table({ id: 't-2', tableNumber: '2' })]))

    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    expect(mockFetch).toHaveBeenCalledTimes(1)
    expect(mockFetch.mock.calls[0][0]).toContain(FLOOR_PATH)
    expect(mockFetch.mock.calls[0][1].headers.Authorization).toBe('Bearer tok-abc')
    expect(mockHub.joinRoleGroup).toHaveBeenCalledWith('rest-123', 'WaitStaff')
    expect(store.isConnected).toBe(true)
    expect(store.isLoading).toBe(false)
    expect(store.tables).toHaveLength(2)
    expect(store.zones).toEqual([{ id: 'z-1', name: 'Terasă', sortOrder: 0 }])
  })

  it('joins the admin group for a manager/owner', async () => {
    authState.isWaiter = false

    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    expect(mockHub.joinRoleGroup).toHaveBeenCalledWith('rest-123', 'admin')
  })

  it('subscribes to every trigger event plus the availability broadcast', async () => {
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    for (const ev of TRIGGER_EVENTS) {
      expect(typeof mockHandlers[ev]).toBe('function')
    }
    expect(typeof mockHandlers['MenuItemAvailabilityChanged']).toBe('function')
  })

  it('derives myTables, freeTables and tableById from the floor', async () => {
    jsonOnce(
      floorPayload([
        table({ id: 't-1', session: session({ waiterUserId: 'u-me' }) }),
        table({ id: 't-2', tableNumber: '2', session: session({ id: 's-2', waiterUserId: 'u-other', waiterName: 'Bogdan' }) }),
        table({ id: 't-3', tableNumber: '3', session: null }),
      ]),
    )

    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    expect(store.myTables.map((t) => t.id)).toEqual(['t-1'])
    expect(store.freeTables.map((t) => t.id)).toEqual(['t-3'])
    expect(store.tableById('t-2')?.session?.waiterName).toBe('Bogdan')
    expect(store.tableById('nope')).toBeUndefined()
  })

  it('survives a failed seed and stays usable', async () => {
    mockFetch.mockReset()
    mockFetch.mockRejectedValue(new Error('network down'))

    const store = useWaiterRealtimeStore()
    await expect(store.start('rest-123')).resolves.toBeUndefined()

    expect(store.floor).toBeNull()
    expect(store.tables).toEqual([])
    expect(store.isLoading).toBe(false)
    // The hub still connected — the seed failure must not abort the subscription.
    expect(store.isConnected).toBe(true)
  })

  it('refreshes with a retry after a 401 refresh', async () => {
    mockFetch.mockReset()
    jsonOnce({ message: 'expired' }, 401)
    jsonOnce(floorPayload([table({ id: 't-9' })]))
    authState.tryRefresh.mockResolvedValueOnce(true)

    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    expect(authState.tryRefresh).toHaveBeenCalledTimes(1)
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(store.tables.map((t) => t.id)).toEqual(['t-9'])
  })
})

describe('useWaiterRealtimeStore — events are triggers, REST is truth', () => {
  it('coalesces a burst of live events into exactly one refetch', async () => {
    vi.useFakeTimers()
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')
    expect(mockFetch).toHaveBeenCalledTimes(1) // the seed

    jsonOnce(floorPayload([table({ id: 't-5', session: session({ tableId: 't-5' }) })]))
    mockHandlers['TableSessionOpened']({ sessionId: 's-1', tableId: 't-5' })
    mockHandlers['OrderSubmitted']({ orderId: 'o-1', tableId: 't-5' })
    mockHandlers['OrderStatusUpdated']({ orderId: 'o-1' })
    await vi.advanceTimersByTimeAsync(300)

    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(store.tables.map((t) => t.id)).toEqual(['t-5'])
  })

  it('refetches once per event outside the debounce window', async () => {
    vi.useFakeTimers()
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    mockHandlers['HelpRequested']({ requestId: 'r-1', tableId: 't-1' })
    await vi.advanceTimersByTimeAsync(300)
    mockHandlers['HelpAcknowledged']({ requestId: 'r-1' })
    await vi.advanceTimersByTimeAsync(300)

    expect(mockFetch).toHaveBeenCalledTimes(3) // seed + two refreshes
  })

  it('folds MenuItemAvailabilityChanged in directly, without a refetch', async () => {
    vi.useFakeTimers()
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    mockHandlers['MenuItemAvailabilityChanged']({
      items: [
        { productId: 'p-1', isAvailable: false, outOfStock: true },
        { productId: 'p-2', isAvailable: true, outOfStock: false },
      ],
    })
    await vi.advanceTimersByTimeAsync(300)

    expect(store.unavailableProductIds.has('p-1')).toBe(true)
    expect(store.unavailableProductIds.has('p-2')).toBe(false)
    expect(mockFetch).toHaveBeenCalledTimes(1) // still only the seed

    // A later broadcast restoring p-1 removes it again.
    mockHandlers['MenuItemAvailabilityChanged']({
      items: [{ productId: 'p-1', isAvailable: true, outOfStock: false }],
    })
    await vi.advanceTimersByTimeAsync(300)

    expect(store.unavailableProductIds.has('p-1')).toBe(false)
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it('re-joins the group and re-seeds on reconnect', async () => {
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')
    expect(mockReconnectHandlers).toHaveLength(1)

    jsonOnce(floorPayload([table({ id: 't-x' })]))
    mockHub.joinRoleGroup.mockClear()
    await mockReconnectHandlers[0]()

    expect(mockHub.joinRoleGroup).toHaveBeenCalledWith('rest-123', 'WaitStaff')
    expect(store.tables.map((t) => t.id)).toEqual(['t-x'])
  })

  it('polls the floor every 15s as a fallback for missed events', async () => {
    vi.useFakeTimers()
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')
    expect(mockFetch).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(15_000)
    expect(mockFetch).toHaveBeenCalledTimes(2)

    await vi.advanceTimersByTimeAsync(15_000)
    expect(mockFetch).toHaveBeenCalledTimes(3)

    void store.tables
  })
})

describe('useWaiterRealtimeStore — teardown', () => {
  it('stop() unsubscribes, leaves the group, stops the hub and clears state', async () => {
    jsonOnce(floorPayload([table({ id: 't-1', session: session() })]))

    const store = useWaiterRealtimeStore()
    await store.start('rest-123')
    mockHandlers['MenuItemAvailabilityChanged']({ items: [{ productId: 'p-1', isAvailable: false }] })
    expect(store.unavailableProductIds.has('p-1')).toBe(true)

    await store.stop()

    for (const ev of TRIGGER_EVENTS) expect(mockHub.off).toHaveBeenCalledWith(ev)
    expect(mockHub.off).toHaveBeenCalledWith('MenuItemAvailabilityChanged')
    expect(mockHub.leaveRoleGroup).toHaveBeenCalledWith('rest-123', 'WaitStaff')
    expect(mockHub.stop).toHaveBeenCalled()
    expect(store.isConnected).toBe(false)
    expect(store.floor).toBeNull()
    expect(store.unavailableProductIds.size).toBe(0)
  })

  it('stop() clears the polling and debounce timers', async () => {
    vi.useFakeTimers()
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')

    mockHandlers['OrderSubmitted']({ orderId: 'o-1' })
    await store.stop()
    mockFetch.mockClear()

    await vi.advanceTimersByTimeAsync(60_000)
    expect(mockFetch).not.toHaveBeenCalled()
  })

  it('start() is idempotent for the same restaurant', async () => {
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')
    await store.start('rest-123')

    expect(mockFetch).toHaveBeenCalledTimes(1)
    expect(mockHub.joinRoleGroup).toHaveBeenCalledTimes(1)
  })

  it('switching restaurants tears the old connection down first', async () => {
    const store = useWaiterRealtimeStore()
    await store.start('rest-123')
    await store.start('rest-456')

    expect(mockHub.leaveRoleGroup).toHaveBeenCalledWith('rest-123', 'WaitStaff')
    expect(mockHub.joinRoleGroup).toHaveBeenCalledWith('rest-456', 'WaitStaff')
    expect(mockFetch.mock.calls.at(-1)?.[0]).toContain('/api/restaurants/rest-456/waiter/floor')
  })
})

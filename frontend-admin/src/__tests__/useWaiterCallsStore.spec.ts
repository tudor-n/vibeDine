// @vitest-environment jsdom
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { WaiterCall } from '@/services/api-client'

/**
 * Ownership routing in the waiter-call queue: a waiter's store keeps (and
 * chimes for) only unassigned calls plus their own; managers keep the whole
 * queue; session events re-seed so assignments move between phones.
 */

// ── Mock the auth store before importing the store under test ───────────────
const authState = {
  user: { id: 'u-me' } as { id: string } | null,
  isWaiter: true,
}
vi.mock('@/stores/useAuthStore', () => ({ useAuthStore: () => authState }))

// ── Mock the API client (REST seed + acknowledge) ───────────────────────────
const getHelpRequests = vi.fn<() => Promise<WaiterCall[]>>().mockResolvedValue([])
const acknowledgeHelpRequest = vi.fn().mockResolvedValue({})
vi.mock('@/services/api-client', () => ({
  apiClient: {
    getHelpRequests: () => getHelpRequests(),
    acknowledgeHelpRequest: (id: string) => acknowledgeHelpRequest(id),
  },
}))

// ── Mock the SignalR client — capture handlers, fire them from tests ────────
const mockHandlers: Record<string, (payload: unknown) => void> = {}
const mockHub = {
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  joinRoleGroup: vi.fn().mockResolvedValue(undefined),
  leaveRoleGroup: vi.fn().mockResolvedValue(undefined),
  on: vi.fn().mockImplementation((event: string, handler: (p: unknown) => void) => {
    mockHandlers[event] = handler
  }),
  off: vi.fn(),
  onReconnected: vi.fn(),
}
vi.mock('@/services/signalRClient', () => ({
  createStaffHubClient: vi.fn(() => mockHub),
}))

import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'

const call = (id: string, assigned: string | null): WaiterCall => ({
  requestId: id,
  tableId: `t-${id}`,
  tableNumber: id,
  createdAt: '2026-08-08T10:00:00Z',
  assignedWaiterUserId: assigned,
  assignedWaiterName: assigned ? 'Ana' : null,
})

const flush = () => new Promise((r) => setTimeout(r))

beforeEach(() => {
  setActivePinia(createPinia())
  for (const k of Object.keys(mockHandlers)) delete mockHandlers[k]
  getHelpRequests.mockReset().mockResolvedValue([])
  authState.isWaiter = true
  authState.user = { id: 'u-me' }
})

describe('useWaiterCallsStore — ownership routing', () => {
  it('a waiter keeps unassigned calls and their own, never a colleague’s', async () => {
    getHelpRequests.mockResolvedValue([
      call('1', null),
      call('2', 'u-me'),
      // Defensive: the API filters this out for waiters, but a stale payload must not slip through.
      call('3', 'u-colleague'),
    ])

    const store = useWaiterCallsStore()
    await store.start('rest-1')

    expect(store.count).toBe(2)
    expect(store.unassignedCalls.map((c) => c.requestId)).toEqual(['1'])
    expect(store.myCalls.map((c) => c.requestId)).toEqual(['2'])
    expect(store.otherCalls).toHaveLength(0)
  })

  it('a live HelpRequested for a colleague’s table is ignored on a waiter phone', async () => {
    const store = useWaiterCallsStore()
    await store.start('rest-1')

    mockHandlers['HelpRequested']!(call('9', 'u-colleague'))
    expect(store.count).toBe(0)

    mockHandlers['HelpRequested']!(call('10', null))
    mockHandlers['HelpRequested']!(call('11', 'u-me'))
    expect(store.count).toBe(2)
    expect(store.myCalls.map((c) => c.requestId)).toEqual(['11'])
  })

  it('a manager keeps the whole queue, colleagues’ calls included', async () => {
    authState.isWaiter = false
    getHelpRequests.mockResolvedValue([call('1', null), call('2', 'u-colleague')])

    const store = useWaiterCallsStore()
    await store.start('rest-1')

    expect(store.count).toBe(2)
    expect(store.otherCalls.map((c) => c.requestId)).toEqual(['2'])
  })

  it('session events re-seed, so a claim moves the call off other phones', async () => {
    getHelpRequests.mockResolvedValue([call('1', null)])

    const store = useWaiterCallsStore()
    await store.start('rest-1')
    expect(store.unassignedCalls).toHaveLength(1)

    // A colleague claims the table: the fresh seed reports the call as theirs,
    // and this waiter's phone drops it.
    getHelpRequests.mockResolvedValue([call('1', 'u-colleague')])
    mockHandlers['TableSessionOpened']!({ sessionId: 's-1' })
    await flush()

    expect(store.count).toBe(0)
  })
})

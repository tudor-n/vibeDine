import type { OrderStatus, UUID } from '@shared/types/models'
import type { MenuResponse } from '@shared/types/api-responses'
import type {
    SessionPaymentMethod,
    WaiterFloorResponse,
    WaiterOrderDto,
    WaiterSessionDetailDto,
    WaiterSessionDto,
    WaiterStaffMemberDto,
} from '@shared/types/waiter'
import { authedFetcher, fetcher, scoped } from '@/services/api-client'

/**
 * Waiter Mode REST client — every call in Appendix B.4, prefixed
 * `api/restaurants/{restaurantId}/waiter` via the shared `scoped()` helper so the
 * active restaurant (and therefore the tenant scope) always comes from the auth
 * store and is never passed in by a caller.
 *
 * Not to be confused with `waiterApi` in `api-client.ts`, which owns the waiter
 * *signup link / approval* endpoints. This module is the order-taking surface.
 *
 * Errors surface as plain `Error`s carrying the API's Romanian `message` (see
 * `parseOrThrow` in api-client). That is deliberate for the claim conflict: the
 * 409 body's message already names the owning waiter, which is exactly what the
 * floor view shows in its toast.
 */

// Client-side mirrors of the frozen server caps (B.0). Enforced before the
// request so a waiter gets an instant Romanian message instead of a 400.
export const WAITER_CAPS = {
    MaxOrderItems: 100,
    MaxLineQuantity: 99,
    NotesMaxLength: 200,
} as const

// ── Request bodies (B.4). Requests never carry a restaurantId. ───────────────
export interface WaiterCloseSessionRequest {
    paymentMethod: SessionPaymentMethod
    cashAmount?: number | null
    cardAmount?: number | null
}

export interface WaiterOrderItemInput {
    productId: UUID
    quantity: number
    notes?: string | null
}

// ── Narrow responses (B.4 rows 10 & 11) ──────────────────────────────────────
export interface WaiterSubmitOrderResult {
    orderId: UUID
    orderNumber: number | null
    status: OrderStatus
    totalAmount: number
}

export interface WaiterCancelOrderResult {
    id: UUID
    status: OrderStatus
}

/** 1 — the live floor: zones + tables + their open sessions. */
export const getFloor = () => authedFetcher<WaiterFloorResponse>(scoped('/waiter/floor'))

/** 2 — claim a free table. 201 on success, 409 `{ message, session }` when taken. */
export const claimTable = (tableId: UUID) =>
    authedFetcher<WaiterSessionDto>(scoped(`/waiter/tables/${tableId}/claim`), { method: 'POST' })

/** 3 — one session with its rounds and the running total. */
export const getSession = (sessionId: UUID) =>
    authedFetcher<WaiterSessionDetailDto>(scoped(`/waiter/sessions/${sessionId}`))

/** 4 — the caller's own open sessions. */
export const getMySessions = () => authedFetcher<WaiterSessionDto[]>(scoped('/waiter/my-sessions'))

/** 5 — hand the table to another waiter. */
export const transferSession = (sessionId: UUID, toUserId: UUID) =>
    authedFetcher<WaiterSessionDto>(scoped(`/waiter/sessions/${sessionId}/transfer`), {
        method: 'POST',
        body: JSON.stringify({ toUserId }),
    })

/** 6 — settle the bill and close the table. 409 while a round is still dispatching. */
export const closeSession = (sessionId: UUID, req: WaiterCloseSessionRequest) =>
    authedFetcher<WaiterSessionDto>(scoped(`/waiter/sessions/${sessionId}/close`), {
        method: 'POST',
        body: JSON.stringify(req),
    })

/** 7 — manager-only escape hatch; audited, never fiscal. */
export const forceReleaseSession = (sessionId: UUID) =>
    authedFetcher<WaiterSessionDto>(scoped(`/waiter/sessions/${sessionId}/force-release`), {
        method: 'POST',
    })

/** 8 — open a new draft round on a table the caller owns. */
export const createOrder = (tableId: UUID) =>
    authedFetcher<WaiterOrderDto>(scoped(`/waiter/tables/${tableId}/orders`), { method: 'POST' })

/** 9 — full-replace the draft's lines (omission removes, like the customer cart). */
export const updateOrderItems = (orderId: UUID, items: WaiterOrderItemInput[]) =>
    authedFetcher<WaiterOrderDto>(scoped(`/waiter/orders/${orderId}/items`), {
        method: 'PATCH',
        body: JSON.stringify({ items }),
    })

/** 10 — submit; the backend dispatches to the kitchen immediately (B.3-3). */
export const submitOrder = (orderId: UUID) =>
    authedFetcher<WaiterSubmitOrderResult>(scoped(`/waiter/orders/${orderId}/submit`), {
        method: 'POST',
    })

/** 11 — cancel a round (draft is hard-deleted, anything later restores stock). */
export const cancelOrder = (orderId: UUID) =>
    authedFetcher<WaiterCancelOrderResult>(scoped(`/waiter/orders/${orderId}/cancel`), {
        method: 'POST',
    })

/** 12 — transfer targets: active Waiter / RestaurantAdmin members of this location. */
export const getStaff = () => authedFetcher<WaiterStaffMemberDto[]>(scoped('/waiter/staff'))

/**
 * The order screen's menu. Deliberately the existing anonymous customer menu
 * endpoint (B.4: "no new menu endpoint") — it already merges out-of-stock state
 * and hides staff-hidden products, so it needs no bearer token.
 */
export const getMenu = () => fetcher<MenuResponse>(scoped('/menu'))

export const waiterApi = {
    getFloor,
    claimTable,
    getSession,
    getMySessions,
    transferSession,
    closeSession,
    forceReleaseSession,
    createOrder,
    updateOrderItems,
    submitOrder,
    cancelOrder,
    getStaff,
    getMenu,
}

import type { OrderStatus, UUID } from '@shared/types/models'
import type { MenuResponse } from '@shared/types/api-responses'
import type {
    SessionPaymentMethod,
    WaiterAuxTableResponse,
    WaiterFloorResponse,
    WaiterOrderDto,
    WaiterSessionDetailDto,
    WaiterSessionDto,
    WaiterStaffMemberDto,
    WaiterUnclaimedOrderDto,
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
 * 13 — the "Comenzi nepreluate" feed: live rounds on tables no session covers,
 * oldest first. `claimTable` on the round's table adopts it onto the new bill,
 * which is what removes it from this list.
 */
export const getUnclaimedOrders = () =>
    authedFetcher<WaiterUnclaimedOrderDto[]>(scoped('/waiter/unclaimed-orders'))

// ── Masa auxiliară — the caller's private holding area ───────────────────────

/** 14 — the caller's parked lines + their total. */
export const getAuxTable = () => authedFetcher<WaiterAuxTableResponse>(scoped('/waiter/aux-table'))

/** 15 — pull (part of) a line off a live round onto the caller's aux table. */
export const moveItemToAux = (orderId: UUID, itemId: UUID, quantity?: number) =>
    authedFetcher<{ orderId: UUID; orderStatus: OrderStatus; movedQuantity: number }>(
        scoped(`/waiter/orders/${orderId}/items/${itemId}/move-to-aux`),
        { method: 'POST', body: JSON.stringify({ quantity: quantity ?? null }) },
    )

/** 16 — land a parked line on one of the caller's tables (as an archived round). */
export const moveAuxItemToTable = (auxItemId: UUID, tableId: UUID) =>
    authedFetcher<{ orderId: UUID; orderNumber: number | null; tableNumber: string | null }>(
        scoped(`/waiter/aux-table/items/${auxItemId}/move-to-table`),
        { method: 'POST', body: JSON.stringify({ tableId }) },
    )

/** 17 — write the parked line off (stock stays consumed). */
export const discardAuxItem = (auxItemId: UUID) =>
    authedFetcher<{ id: UUID }>(scoped(`/waiter/aux-table/items/${auxItemId}`), {
        method: 'DELETE',
    })

/**
 * The order screen's menu. Deliberately the existing anonymous customer menu
 * endpoint (B.4: "no new menu endpoint") — it already merges out-of-stock state
 * and hides staff-hidden products, so it needs no bearer token.
 */
export const getMenu = () => fetcher<MenuResponse>(scoped('/menu'))

export const waiterApi = {
    getFloor,
    getUnclaimedOrders,
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
    getAuxTable,
    moveItemToAux,
    moveAuxItemToTable,
    discardAuxItem,
    getMenu,
}

// @vitest-environment node
import { describe, expect, it } from 'vitest'
import {
  ORDER_SOURCES,
  SESSION_CLOSED_REASONS,
  SESSION_PAYMENT_METHODS,
  SESSION_STATUSES,
  closedSession,
  draftOrder,
  floorResponse,
  forceReleasedPayload,
  freeTable,
  occupiedTable,
  openSession,
  orderItem,
  sessionClosedPayload,
  sessionDetail,
  sessionOpenedPayload,
  sessionTransferredPayload,
  staffMember,
  waiterOrder,
} from './waiterContractFixtures'

/**
 * Contract guard for `@shared/types/waiter` — the frozen wire shape the backend
 * DTOs, this app and the Logistic consumer all hand-sync against.
 *
 * Two layers:
 *  1. `waiterContractFixtures.ts` annotates one literal per type and IS covered
 *     by `npm run type-check` (specs are excluded from the tsconfig, plain `.ts`
 *     modules are not) — a renamed or retyped field fails to compile there.
 *  2. The key-set assertions below run under `npm test`, so a field added to or
 *     dropped from the contract surfaces in CI too.
 */

const keys = (o: object) => Object.keys(o).sort()

describe('waiter contract — string unions', () => {
  it('freezes the four vocabularies', () => {
    expect(ORDER_SOURCES).toEqual(['qr', 'waiter'])
    expect(SESSION_STATUSES).toEqual(['open', 'closed'])
    expect(SESSION_CLOSED_REASONS).toEqual(['paid', 'force_released'])
    expect(SESSION_PAYMENT_METHODS).toEqual(['cash', 'card', 'mixed'])
  })
})

describe('waiter contract — DTO field sets', () => {
  it('WaiterSessionDto', () => {
    expect(keys(openSession)).toEqual(
      keys({
        id: 0,
        tableId: 0,
        tableNumber: 0,
        waiterUserId: 0,
        waiterName: 0,
        status: 0,
        openedAt: 0,
        closedAt: 0,
        closedReason: 0,
        paymentMethod: 0,
        cashAmount: 0,
        cardAmount: 0,
        totalAmount: 0,
        activeOrderCount: 0,
        cumulativeTotal: 0,
      }),
    )
    // A closed session carries the payment snapshot; an open one carries nulls.
    expect(openSession.totalAmount).toBeNull()
    expect(closedSession.totalAmount).toBe(187.5)
    expect(closedSession.cashAmount! + closedSession.cardAmount!).toBe(closedSession.totalAmount)
  })

  it('WaiterOrderItemDto', () => {
    expect(keys(orderItem)).toEqual(
      keys({
        id: 0,
        productId: 0,
        productName: 0,
        quantity: 0,
        unitPriceAtPurchase: 0,
        notes: 0,
        isFlashDealItem: 0,
      }),
    )
  })

  it('WaiterOrderDto', () => {
    expect(keys(waiterOrder)).toEqual(
      keys({
        id: 0,
        orderNumber: 0,
        status: 0,
        source: 0,
        placedByUserId: 0,
        totalAmount: 0,
        createdAt: 0,
        items: 0,
      }),
    )
    // A draft has no per-restaurant order number until submit assigns one.
    expect(draftOrder.orderNumber).toBeNull()
    expect(draftOrder.status).toBe('draft')
    expect(waiterOrder.source).toBe('waiter')
  })

  it('WaiterSessionDetailDto', () => {
    expect(keys(sessionDetail)).toEqual(keys({ session: 0, orders: 0, cumulativeTotal: 0 }))
  })

  it('WaiterFloorTableDto', () => {
    expect(keys(occupiedTable)).toEqual(
      keys({
        id: 0,
        tableNumber: 0,
        zoneId: 0,
        capacity: 0,
        shape: 0,
        positionX: 0,
        positionY: 0,
        session: 0,
        hasOpenHelpRequest: 0,
        activeOrderCount: 0,
      }),
    )
    // `session === null` is what marks a table free — nothing else.
    expect(freeTable.session).toBeNull()
    expect(occupiedTable.session).not.toBeNull()
  })

  it('WaiterFloorResponse', () => {
    expect(keys(floorResponse)).toEqual(keys({ zones: 0, tables: 0 }))
    expect(keys(floorResponse.zones[0])).toEqual(keys({ id: 0, name: 0, sortOrder: 0 }))
  })

  it('WaiterStaffMemberDto', () => {
    expect(keys(staffMember)).toEqual(keys({ userId: 0, fullName: 0, role: 0 }))
  })
})

describe('waiter contract — SignalR payload field sets', () => {
  it('TableSessionOpened', () => {
    expect(keys(sessionOpenedPayload)).toEqual(
      keys({
        sessionId: 0,
        tableId: 0,
        tableNumber: 0,
        waiterUserId: 0,
        waiterName: 0,
        openedAt: 0,
      }),
    )
  })

  it('TableSessionTransferred', () => {
    expect(keys(sessionTransferredPayload)).toEqual(
      keys({ sessionId: 0, tableId: 0, fromUserId: 0, toUserId: 0, toName: 0 }),
    )
  })

  it('TableSessionClosed', () => {
    expect(keys(sessionClosedPayload)).toEqual(
      keys({
        sessionId: 0,
        tableId: 0,
        tableNumber: 0,
        paymentMethod: 0,
        totalAmount: 0,
        closedReason: 0,
      }),
    )
    // A force-release closes with no payment method — the fiscal consumer keys
    // off `closedReason`, so both fields must survive the round trip.
    expect(forceReleasedPayload.paymentMethod).toBeNull()
    expect(forceReleasedPayload.closedReason).toBe('force_released')
  })
})

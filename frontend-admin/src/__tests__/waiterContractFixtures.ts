/**
 * Compile-time contract guard for `@shared/types/waiter`.
 *
 * One fully-populated literal per DTO and SignalR payload, each explicitly
 * annotated with its contract type. This file is deliberately **not** a
 * `.spec.ts`: `tsconfig.json` excludes `*.spec.ts`/`*.test.ts` from
 * `type-check`, so the literals only get real type coverage from a plain `.ts`
 * module under `src/`. Renaming, retyping or dropping a contract field breaks
 * `npm run type-check` here — cheaply, before any view does.
 *
 * `waiterTypes.spec.ts` imports these and asserts their runtime key sets, so
 * drift is caught by `npm test` too.
 */
import type {
  OrderSource,
  SessionClosedReason,
  SessionPaymentMethod,
  SessionStatus,
  TableSessionClosedPayload,
  TableSessionOpenedPayload,
  TableSessionTransferredPayload,
  WaiterFloorResponse,
  WaiterFloorTableDto,
  WaiterOrderDto,
  WaiterOrderItemDto,
  WaiterSessionDetailDto,
  WaiterSessionDto,
  WaiterStaffMemberDto,
} from '@shared/types/waiter'

// Every literal of each string-union — a renamed member fails to compile.
export const ORDER_SOURCES: OrderSource[] = ['qr', 'waiter']
export const SESSION_STATUSES: SessionStatus[] = ['open', 'closed']
export const SESSION_CLOSED_REASONS: SessionClosedReason[] = ['paid', 'force_released']
export const SESSION_PAYMENT_METHODS: SessionPaymentMethod[] = ['cash', 'card', 'mixed']

export const openSession: WaiterSessionDto = {
  id: '5e3f0d5a-2a1e-4f2b-9c7d-0a1b2c3d4e5f',
  tableId: 'b7c3f1a2-1111-2222-3333-444455556666',
  tableNumber: '12',
  waiterUserId: 'a1a1a1a1-2b2b-3c3c-4d4d-5e5e5e5e5e5e',
  waiterName: 'Ana Popescu',
  status: 'open',
  openedAt: '2026-08-05T18:00:00Z',
  closedAt: null,
  closedReason: null,
  paymentMethod: null,
  cashAmount: null,
  cardAmount: null,
  totalAmount: null,
  activeOrderCount: 2,
  cumulativeTotal: 187.5,
}

export const closedSession: WaiterSessionDto = {
  ...openSession,
  status: 'closed',
  closedAt: '2026-08-05T19:42:00Z',
  closedReason: 'paid',
  paymentMethod: 'mixed',
  cashAmount: 100,
  cardAmount: 87.5,
  totalAmount: 187.5,
  activeOrderCount: 0,
}

export const orderItem: WaiterOrderItemDto = {
  id: 'c2c2c2c2-1111-2222-3333-444455556666',
  productId: 'd3d3d3d3-1111-2222-3333-444455556666',
  productName: 'Ciorbă de burtă',
  quantity: 2,
  unitPriceAtPurchase: 24.5,
  notes: 'fără smântână',
  isFlashDealItem: false,
}

export const waiterOrder: WaiterOrderDto = {
  id: 'e4e4e4e4-1111-2222-3333-444455556666',
  orderNumber: 41,
  status: 'preparing',
  source: 'waiter',
  placedByUserId: openSession.waiterUserId,
  totalAmount: 49,
  createdAt: '2026-08-05T18:05:00Z',
  items: [orderItem],
}

export const draftOrder: WaiterOrderDto = {
  ...waiterOrder,
  id: 'f5f5f5f5-1111-2222-3333-444455556666',
  orderNumber: null,
  status: 'draft',
  items: [],
  totalAmount: 0,
}

export const sessionDetail: WaiterSessionDetailDto = {
  session: openSession,
  orders: [waiterOrder, draftOrder],
  cumulativeTotal: 187.5,
}

export const occupiedTable: WaiterFloorTableDto = {
  id: openSession.tableId,
  tableNumber: '12',
  zoneId: '11111111-2222-3333-4444-555555555555',
  capacity: 4,
  shape: 'square',
  positionX: 120,
  positionY: 240,
  session: openSession,
  hasOpenHelpRequest: true,
  activeOrderCount: 2,
}

export const freeTable: WaiterFloorTableDto = {
  ...occupiedTable,
  id: '99999999-2222-3333-4444-555555555555',
  tableNumber: '13',
  zoneId: null,
  session: null,
  hasOpenHelpRequest: false,
  activeOrderCount: 0,
}

export const floorResponse: WaiterFloorResponse = {
  zones: [{ id: '11111111-2222-3333-4444-555555555555', name: 'Terasă', sortOrder: 0 }],
  tables: [occupiedTable, freeTable],
}

export const staffMember: WaiterStaffMemberDto = {
  userId: 'a1a1a1a1-2b2b-3c3c-4d4d-5e5e5e5e5e5e',
  fullName: 'Ana Popescu',
  role: 'Waiter',
}

// ── SignalR payloads (mirror the StaffHub broadcasts field-for-field) ────────
export const sessionOpenedPayload: TableSessionOpenedPayload = {
  sessionId: openSession.id,
  tableId: openSession.tableId,
  tableNumber: '12',
  waiterUserId: 'a1a1a1a1-2b2b-3c3c-4d4d-5e5e5e5e5e5e',
  waiterName: 'Ana Popescu',
  openedAt: '2026-08-05T18:00:00Z',
}

export const sessionTransferredPayload: TableSessionTransferredPayload = {
  sessionId: openSession.id,
  tableId: openSession.tableId,
  fromUserId: 'a1a1a1a1-2b2b-3c3c-4d4d-5e5e5e5e5e5e',
  toUserId: 'b2b2b2b2-2b2b-3c3c-4d4d-5e5e5e5e5e5e',
  toName: 'Bogdan Ionescu',
}

export const sessionClosedPayload: TableSessionClosedPayload = {
  sessionId: openSession.id,
  tableId: openSession.tableId,
  tableNumber: '12',
  paymentMethod: 'cash',
  totalAmount: 187.5,
  closedReason: 'paid',
}

export const forceReleasedPayload: TableSessionClosedPayload = {
  ...sessionClosedPayload,
  paymentMethod: null,
  closedReason: 'force_released',
}

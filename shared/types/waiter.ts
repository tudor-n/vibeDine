import type { UUID, ISODateString, OrderStatus } from './models';

// ── Waiter Mode ──────────────────────────────────────────────────────────────
// The wire contract for waiter-run table service: a waiter claims a table (a
// `table_service_session`), places orders against it from their phone, and closes
// the bill with a physical payment method. Mirrors the C# DTOs in
// `backend/RestaurantApi/DTO/{Requests,Responses}/Waiter*.cs` field-for-field —
// the two are hand-synced, so change them together.

/** Where an order originated. NOT a fulfillment channel (that is `OrderType`). */
export type OrderSource = 'qr' | 'waiter';
export type SessionStatus = 'open' | 'closed';
export type SessionClosedReason = 'paid' | 'force_released';
export type SessionPaymentMethod = 'cash' | 'card' | 'mixed';

/** A table claim. Exactly one `open` session may exist per table (partial unique index). */
export interface WaiterSessionDto {
  id: UUID;
  tableId: UUID;
  tableNumber: string | null;
  waiterUserId: UUID | null;
  waiterName: string;
  status: SessionStatus;
  openedAt: ISODateString;
  closedAt: ISODateString | null;
  closedReason: SessionClosedReason | null;
  paymentMethod: SessionPaymentMethod | null;
  cashAmount: number | null;
  cardAmount: number | null;
  /** Snapshot of `cumulativeTotal` taken at close; null while open. */
  totalAmount: number | null;
  activeOrderCount: number;
  cumulativeTotal: number;
}

export interface WaiterOrderItemDto {
  id: UUID;
  productId: UUID;
  productName: string;
  quantity: number;
  unitPriceAtPurchase: number;
  /** Kitchen remark, ≤200 chars (API-enforced). */
  notes: string | null;
  isFlashDealItem: boolean;
}

export interface WaiterOrderDto {
  id: UUID;
  orderNumber: number | null;
  status: OrderStatus;
  source: OrderSource;
  placedByUserId: UUID | null;
  totalAmount: number;
  createdAt: ISODateString;
  items: WaiterOrderItemDto[];
}

export interface WaiterSessionDetailDto {
  session: WaiterSessionDto;
  /** The session's orders, statuses per the cumulative-total contract rule. */
  orders: WaiterOrderDto[];
  cumulativeTotal: number;
}

export interface WaiterFloorTableDto {
  id: UUID;
  tableNumber: string | null;
  zoneId: UUID | null;
  capacity: number;
  shape: string;
  positionX: number;
  positionY: number;
  /** null = free table. */
  session: WaiterSessionDto | null;
  hasOpenHelpRequest: boolean;
  /** Non-deleted orders in pending_dispatch..ready. */
  activeOrderCount: number;
}

export interface WaiterFloorResponse {
  zones: { id: UUID; name: string; sortOrder: number }[];
  tables: WaiterFloorTableDto[];
}

export interface WaiterStaffMemberDto {
  userId: UUID;
  fullName: string;
  role: string;
}

// ── SignalR payloads (StaffHub; event names PascalCase, payloads camelCase) ──
export interface TableSessionOpenedPayload {
  sessionId: UUID;
  tableId: UUID;
  tableNumber: string | null;
  waiterUserId: UUID;
  waiterName: string;
  openedAt: ISODateString;
}

export interface TableSessionTransferredPayload {
  sessionId: UUID;
  tableId: UUID;
  fromUserId: UUID | null;
  toUserId: UUID;
  toName: string;
}

export interface TableSessionClosedPayload {
  sessionId: UUID;
  tableId: UUID;
  tableNumber: string | null;
  paymentMethod: SessionPaymentMethod | null;
  totalAmount: number;
  closedReason: SessionClosedReason;
}

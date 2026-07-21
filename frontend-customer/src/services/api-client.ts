import type { MenuResponse, VibeyRecommendationResponse } from '@shared/types/api-responses';
import type { UpdateOrderItemsRequest, VibeyRecommendationRequest } from '@shared/types/api-requests';
import type { ActiveHappyHour, PendingFlashDealPayload, RestaurantBranding } from '@shared/types/models';
import { resolveResponseMediaUrls } from '@/services/mediaUrl';

const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005';
const BASE_URL = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl;

export interface TableSession {
  restaurantId: string;
  restaurantName: string;
  restaurantAddress: string | null;
  organizationName: string;
  tableId: string;
  tableNumber: string;
  activeOrder: { id: string };
  branding: RestaurantBranding | null;
}

// ── Multi-person ordering (party) ──────────────────────────────────────────
export interface PartyOrderItem {
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  isFlashDealItem: boolean;
  imageUrl: string | null;
}

export interface PartyMember {
  guestId: string;
  displayName: string | null;
  itemCount: number;
  total: number;
  status: string;
}

// One already-submitted round of a device's visit, as returned inside
// `session.orders` — same narrow shape as `activeOrder` (not the full shared
// `Order` model, which carries fields the join endpoint doesn't send).
export interface DeviceSessionOrder {
  id: string;
  status: string;
  totalAmount: number;
  items: PartyOrderItem[];
}

// The requesting device's whole visit at this table (every already-submitted
// round + cumulative total + any flash deal still pending on one of them).
// Present only when the request carried a usable deviceId.
export interface DeviceSessionPayload {
  id: string;
  linkExpirySeconds: number;
  cumulativeTotal: number;
  orders: DeviceSessionOrder[];
  pendingDeal: PendingFlashDealPayload | null;
}

export interface JoinTableResponse {
  guestId: string;
  displayName: string | null;
  restaurantId: string;
  restaurantName: string;
  restaurantAddress: string | null;
  organizationName: string;
  tableId: string;
  tableNumber: string;
  activeOrder: { id: string; status: string; totalAmount: number; items: PartyOrderItem[] };
  partyMembers: PartyMember[];
  branding: RestaurantBranding | null;
  session: DeviceSessionPayload | null;
}

export interface MergePartyResponse {
  targetOrderId: string;
  mergedOrderIds: string[];
  activeOrder: { id: string; status: string; totalAmount: number; items: PartyOrderItem[] };
  partyMembers: PartyMember[];
}

// Resolved from the table's QR session; every menu/order call is scoped to it.
let currentRestaurantId: string | null = sessionStorage.getItem('restaurantId');

export function setRestaurantId(id: string) {
  currentRestaurantId = id;
  sessionStorage.setItem('restaurantId', id);
}

function rid(): string {
  const id = currentRestaurantId || sessionStorage.getItem('restaurantId');
  if (!id) throw new Error('Restaurantul nu a fost identificat. Scanează din nou codul QR.');
  return id;
}

async function fetcher<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${endpoint}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
  });

  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    throw new Error(errorData.message || errorData.error || `API Error: ${response.status}`);
  }

  if (response.status === 204) {
    return {} as T;
  }

  return resolveResponseMediaUrls(await response.json()) as T;
}

export const apiClient = {
  getMenu: () => fetcher<MenuResponse>(`/api/restaurants/${rid()}/menu`),

  // The live happy hour for this restaurant (204 → {} when none is active).
  getActiveHappyHour: () =>
    fetcher<ActiveHappyHour | null>(`/api/restaurants/${rid()}/happy-hour`),

  initSession: async (qrToken: string) => {
    const session = await fetcher<TableSession>(`/api/tables/${qrToken}/session`);
    if (session.restaurantId) setRestaurantId(session.restaurantId);
    return session;
  },

  // Guest-aware session entry: claims/restores this phone's own draft under the
  // shared table and returns the current party. Replaces initSession in the app.
  // deviceId identifies this phone (independent of the per-table guestId) so the
  // backend can attach/restore a DeviceSession spanning multiple rounds.
  joinTable: async (qrToken: string, guestId: string | null, deviceId: string | null) => {
    const res = await fetcher<JoinTableResponse>(`/api/tables/${qrToken}/join`, {
      method: 'POST',
      body: JSON.stringify({ guestId, deviceId }),
    });
    if (res.restaurantId) setRestaurantId(res.restaurantId);
    return res;
  },

  // guestId rides along as a presence heartbeat: it keeps this phone's seat
  // "fresh" server-side, so polling the party also signals "I'm still here".
  getParty: (qrToken: string, guestId?: string | null) =>
    fetcher<{ partyMembers: PartyMember[] }>(
      `/api/tables/${qrToken}/party${guestId ? `?guestId=${guestId}` : ''}`,
    ),

  mergeParty: (qrToken: string, initiatorGuestId: string) =>
    fetcher<MergePartyResponse>(`/api/tables/${qrToken}/merge`, {
      method: 'POST',
      body: JSON.stringify({ initiatorGuestId }),
    }),

  updateCartItems: (orderId: string, payload: UpdateOrderItemsRequest) =>
    fetcher<void>(`/api/restaurants/${rid()}/orders/${orderId}/items`, {
      method: 'PATCH',
      body: JSON.stringify(payload),
    }),

  submitOrder: (orderId: string) =>
    fetcher<void>(`/api/restaurants/${rid()}/orders/${orderId}/submit`, { method: 'POST' }),

  claimFlashDeal: (orderId: string, dealId: string) =>
    fetcher<void>(`/api/restaurants/${rid()}/orders/${orderId}/claim-deal/${dealId}`, {
      method: 'POST',
    }),

  // The ONE Vibey model call — invoked only from useVibeyStore.openSheet() on
  // the first "Da, arată-mi" tap. Never call this eagerly (cart load, qty
  // change, sheet re-open) — see docs/superpowers/specs/2026-07-09-vibey-recommendations-design.md.
  getVibeyRecommendations: (payload: VibeyRecommendationRequest) =>
    fetcher<VibeyRecommendationResponse>(`/api/restaurants/${rid()}/recommendations`, {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  // Dedicated helper: the generic fetcher throws a plain Error and would lose
  // the 429 retryAfterSeconds payload. Surface status + body so the store can
  // distinguish "rate limited" from generic failures.
  callWaiter: async (
    tableId: string,
  ): Promise<
    | { ok: true; cooldownEndsAt: string }
    | { ok: false; status: number; retryAfterSeconds?: number; message?: string }
  > => {
    const res = await fetch(
      `${BASE_URL}/api/restaurants/${rid()}/tables/${tableId}/call-waiter`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
      },
    );
    const body = await res.json().catch(() => ({}));
    if (res.ok) return { ok: true, cooldownEndsAt: body.cooldownEndsAt };
    return {
      ok: false,
      status: res.status,
      retryAfterSeconds: body.retryAfterSeconds,
      message: body.message,
    };
  },
};

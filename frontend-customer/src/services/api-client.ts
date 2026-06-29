import type { MenuResponse, RecommendationsResponse } from '@shared/types/api-responses';
import type { UpdateOrderItemsRequest } from '@shared/types/api-requests';
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

  initSession: async (qrToken: string) => {
    const session = await fetcher<TableSession>(`/api/tables/${qrToken}/session`);
    if (session.restaurantId) setRestaurantId(session.restaurantId);
    return session;
  },

  // Guest-aware session entry: claims/restores this phone's own draft under the
  // shared table and returns the current party. Replaces initSession in the app.
  joinTable: async (qrToken: string, guestId: string | null) => {
    const res = await fetcher<JoinTableResponse>(`/api/tables/${qrToken}/join`, {
      method: 'POST',
      body: JSON.stringify({ guestId }),
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

  getRecommendations: (orderId: string, cartItemIds: string[], language: string) => {
    const query = new URLSearchParams({
      current_item_ids: cartItemIds.join(','),
      language,
    }).toString();
    return fetcher<RecommendationsResponse | any[]>(
      `/api/restaurants/${rid()}/orders/${orderId}/recommendations?${query}`,
    );
  },

  claimFlashDeal: (orderId: string, dealId: string) =>
    fetcher<void>(`/api/restaurants/${rid()}/orders/${orderId}/claim-deal/${dealId}`, {
      method: 'POST',
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

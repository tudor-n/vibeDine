import type { MenuResponse, RecommendationsResponse } from '@shared/types/api-responses';
import type { UpdateOrderItemsRequest } from '@shared/types/api-requests';

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

  return response.json();
}

export const apiClient = {
  getMenu: () => fetcher<MenuResponse>(`/api/restaurants/${rid()}/menu`),

  initSession: async (qrToken: string) => {
    const session = await fetcher<TableSession>(`/api/tables/${qrToken}/session`);
    if (session.restaurantId) setRestaurantId(session.restaurantId);
    return session;
  },

  updateCartItems: (orderId: string, payload: UpdateOrderItemsRequest) =>
    fetcher<void>(`/api/restaurants/${rid()}/orders/${orderId}/items`, {
      method: 'PATCH',
      body: JSON.stringify(payload),
    }),

  submitOrder: (orderId: string) =>
    fetcher<void>(`/api/restaurants/${rid()}/orders/${orderId}/submit`, { method: 'POST' }),

  getRecommendations: (orderId: string, cartItemIds: string[]) => {
    const query = new URLSearchParams({ current_item_ids: cartItemIds.join(',') }).toString();
    return fetcher<RecommendationsResponse | any[]>(
      `/api/restaurants/${rid()}/orders/${orderId}/recommendations?${query}`,
    );
  },

  claimFlashDeal: (orderId: string, dealId: string) =>
    fetcher<void>(`/api/restaurants/${rid()}/orders/${orderId}/claim-deal/${dealId}`, {
      method: 'POST',
    }),
};

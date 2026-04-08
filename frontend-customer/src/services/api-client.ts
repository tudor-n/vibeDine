import type { MenuResponse, RecommendationsResponse } from '@shared/types/api-responses';
import type { UpdateOrderItemsRequest } from '@shared/types/api-requests';

const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005';
const BASE_URL = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl;

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
  getMenu: () => fetcher<MenuResponse>('/api/menu'),

  initSession: (qrToken: string) =>
    fetcher<{ tableId: string; activeOrder: { id: string } }>(`/api/tables/${qrToken}/session`),

  updateCartItems: (orderId: string, payload: UpdateOrderItemsRequest) =>
    fetcher<void>(`/api/orders/${orderId}/items`, { method: 'PATCH', body: JSON.stringify(payload) }),

  submitOrder: (orderId: string) =>
    fetcher<void>(`/api/orders/${orderId}/submit`, { method: 'POST' }),

  getRecommendations: (orderId: string, cartItemIds: string[]) => {
    const query = new URLSearchParams({ current_item_ids: cartItemIds.join(',') }).toString();
    return fetcher<RecommendationsResponse | any[]>(`/api/orders/${orderId}/recommendations?${query}`);
  },

  claimFlashDeal: (orderId: string, dealId: string) =>
    fetcher<void>(`/api/orders/${orderId}/claim-deal/${dealId}`, { method: 'POST' }),
};

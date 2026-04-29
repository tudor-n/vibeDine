const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005';
const BASE_URL = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl;
async function fetcher(endpoint, options) {
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
        return {};
    }
    return response.json();
}
export const apiClient = {
    getMenu: () => fetcher('/api/menu'),
    initSession: (qrToken) => fetcher(`/api/tables/${qrToken}/session`),
    updateCartItems: (orderId, payload) => fetcher(`/api/orders/${orderId}/items`, { method: 'PATCH', body: JSON.stringify(payload) }),
    submitOrder: (orderId) => fetcher(`/api/orders/${orderId}/submit`, { method: 'POST' }),
    getRecommendations: (orderId, cartItemIds) => {
        const query = new URLSearchParams({ current_item_ids: cartItemIds.join(',') }).toString();
        return fetcher(`/api/orders/${orderId}/recommendations?${query}`);
    },
    claimFlashDeal: (orderId, dealId) => fetcher(`/api/orders/${orderId}/claim-deal/${dealId}`, { method: 'POST' }),
};

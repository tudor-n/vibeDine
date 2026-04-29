import type { MenuResponse, RecommendationsResponse } from '@shared/types/api-responses';
import type { UpdateOrderItemsRequest } from '@shared/types/api-requests';
export declare const apiClient: {
    getMenu: () => Promise<MenuResponse>;
    initSession: (qrToken: string) => Promise<{
        tableId: string;
        activeOrder: {
            id: string;
        };
    }>;
    updateCartItems: (orderId: string, payload: UpdateOrderItemsRequest) => Promise<void>;
    submitOrder: (orderId: string) => Promise<void>;
    getRecommendations: (orderId: string, cartItemIds: string[]) => Promise<any[] | RecommendationsResponse>;
    claimFlashDeal: (orderId: string, dealId: string) => Promise<void>;
};

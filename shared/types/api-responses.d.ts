import type { Category, Product, Order, TableSession, FlashDeal, Recommendation, AdminAIConfig, FlashDealConfig, SimilarityRule, DailyMetrics, FlashDealStatus, UUID, ISODateString } from './models';
export interface MenuCategory extends Category {
    products: Product[];
}
export interface MenuResponse {
    categories: MenuCategory[];
}
export type SessionResponse = TableSession;
export type OrderResponse = Order;
export interface SubmitOrderResponse {
    order: Order;
    message: string;
}
export interface RecommendationsResponse {
    pairings: Recommendation[];
    upsells: Recommendation[];
    flash_deal: FlashDeal | null;
}
export interface ActiveFlashDealResponse {
    flash_deal: FlashDeal | null;
    remaining_seconds: number | null;
}
export interface ClaimFlashDealResponse {
    order: Order;
    message: string;
}
export interface FlashDealOffer {
    id: UUID;
    order_id: UUID | null;
    product_id: UUID | null;
    discount_price: number | null;
    expires_at: ISODateString | null;
    status: FlashDealStatus | null;
}
export interface AdminAIConfigResponse {
    config: AdminAIConfig | null;
    defaults: {
        flash_deal_config: FlashDealConfig;
    };
}
export interface AdminAIConfigHistoryResponse {
    configs: AdminAIConfig[];
    performance: DailyMetrics[];
}
export interface Pagination {
    total: number;
    page: number;
    per_page: number;
}
export interface SimilarityRulesResponse {
    rules: SimilarityRule[];
    pagination: Pagination;
}
export interface AutoGenerateRulesResponse {
    generatedRules: number;
    rules: SimilarityRule[];
}
export type MetricsResponse = DailyMetrics;
export interface AdminFlashDealsResponse {
    offers: FlashDealOffer[];
}
export interface ApiError {
    error: string;
    code: string;
    details?: unknown;
}

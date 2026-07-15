import type { UUID, SimilarityRelationship, FlashDealConfig } from '../types/models';

export interface CreateOrderRequest {
  tableId: UUID;
}

export interface UpdateOrderItemsRequest {
  items: {
    productId: UUID;
    quantity: number;
    wasAiRecommended?: boolean;
  }[];
}


export interface ClaimFlashDealRequest {
  selectedModifiers?: {
    modifierId: UUID;
    optionId: UUID;
  }[];
}


export interface PostAdminAIConfigRequest {
  dailyPrompt: string;
  promotedItems: UUID[];
  suppressedItems: UUID[];
  flashDealConfig: FlashDealConfig;
}


export interface CreateSimilarityRuleRequest {
  sourceItemId: UUID | null;
  sourceCategoryId: UUID | null;
  targetItemId: UUID;
  relationship: SimilarityRelationship;
  weight?: number;
}

export interface AutoGenerateSimilarityRulesRequest {
  minCoOccurrence?: number;
  lookbackDays?: number;
}


export interface TriggerFlashDealRequest {
  productId: UUID;
  discountPercent: number;
  countdownSeconds: number;
}


export interface VibeyCartItem {
  id: string;
  name: string;
  category?: string | null;
  qty: number;
}

export interface VibeyRecommendationRequest {
  items: VibeyCartItem[];
  locale: string;
}

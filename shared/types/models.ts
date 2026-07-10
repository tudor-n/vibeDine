export type UUID = string;
export type ISODateString = string;

export interface Category {
  id: UUID;
  name: string;
  sortOrder: number | null;
  imageUrl?: string | null;
}

export interface AdminCategory {
  id: UUID;
  name: string;
  sortOrder: number | null;
  imageUrl: string | null;
  productCount: number;
}

export interface AdminProduct {
  id: UUID;
  categoryId: UUID;
  categoryName: string;
  name: string;
  description: string | null;
  price: number;
  imageUrl: string | null;
  isAvailable: boolean;
  sortOrder: number;
}

export interface AdminMenuCategoryNode {
  id: UUID;
  name: string;
  sortOrder: number | null;
  imageUrl: string | null;
  products: AdminProduct[];
}

export interface AdminMenuTree {
  categories: AdminMenuCategoryNode[];
}

// ── Inventory / stock ────────────────────────────────────────────────────────
export type StockUnitCode = 'piece' | 'gram' | 'kilogram' | 'milliliter' | 'liter';
export type StockTrackingMode = 'untracked' | 'recipe' | 'simple';

export interface AdminIngredient {
  id: UUID;
  name: string;
  unit: StockUnitCode;
  quantityOnHand: number;
  lowStockThreshold: number;
  isLow: boolean;
  /** Non-null when this stock row backs a simple-tracked product. */
  linkedProductId: UUID | null;
  linkedProductName: string | null;
}

export interface RecipeLine {
  ingredientId: UUID;
  ingredientName: string;
  unit: StockUnitCode;
  quantityPerPortion: number;
}

export interface ProductStock {
  productId: UUID;
  mode: StockTrackingMode;
  recipe: RecipeLine[];
  simpleIngredientId: UUID | null;
  simpleQuantity: number | null;
  simpleUnit: StockUnitCode | null;
  simpleThreshold: number | null;
}

export interface Product {
  id: UUID;
  categoryId: UUID;
  categoryName?: string;
  name: string;
  description: string;
  price: number;
  imageUrl: string | null;
  isAvailable: boolean;
  /** Stock-tracked product that currently can't be made — shown blurred, not orderable. */
  outOfStock?: boolean;
  /** Ingredients of a recipe-tracked dish; rendered as a subtle line under the description on the
   *  customer menu. Absent/empty for non-recipe products. */
  recipe?: RecipeLine[];
}


export interface RestaurantTable {
  id: UUID;
  tableNumber: string | null;
  qrToken: UUID | null;
}

export interface TableSession {
  tableId: UUID;
  orderId: UUID;
  sessionToken: string;
}


export type OrderStatus =
  | 'draft'
  | 'pending_dispatch'
  | 'submitted'
  | 'preparing'
  | 'ready'
  | 'completed'
  | 'cancelled'
  | 'merged';
export type PaymentStatus = 'unpaid' | 'paid' | 'refunded';

export interface OrderItem {
  id: UUID;
  orderId: UUID;
  productId: UUID;
  product: Pick<Product, 'id' | 'name' | 'price' | 'imageUrl'>;
  quantity: number;
  unitPriceAtPurchase: number;
  wasAiRecommended: boolean;
  isFlashDealItem: boolean;
}

export interface Order {
  id: UUID;
  tableId: UUID;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  totalAmount: number;
  items: OrderItem[];
  createdAt: ISODateString;
  orderNumber?: number | null;
  readyAt?: ISODateString | null;
  deletedAt?: ISODateString | null;
}

export interface CartItem {
  product: Product;
  quantity: number;
}


export type FlashDealStatus = 'pending' | 'claimed' | 'expired';
export type FlashDealTrigger = 'post_order' | 'time_based' | 'slow_item';

export interface FlashDeal {
  id: UUID;
  menuItemId: UUID;
  menuItem: Pick<Product, 'id' | 'name' | 'price' | 'imageUrl'>;
  discountPercent: number;
  discountedPrice: number;
  trigger: FlashDealTrigger;
  countdownSeconds: number;
  expiresAt: ISODateString;
  reason: string;
  isActive: boolean;
  createdAt: ISODateString;
}


export type RecommendationType = 'pairing' | 'upsell' | 'flashDeal';
export type RecommendationReasonType =
  | 'chefPick'
  | 'pairsWithOrder'
  | 'popularCombo'
  | 'timeBased'
  | 'flashDeal';
export type RecommendationSource = 'ai' | 'manual' | 'ruleBased';

export interface Recommendation {
  id: UUID;
  type: RecommendationType;
  menuItem: Pick<Product, 'id' | 'name' | 'description' | 'price' | 'imageUrl' | 'categoryName'>;
  reason: string;
  reasonType: RecommendationReasonType;
  discount: {
    percent: number | null;
    originalPrice: number | null;
    discountedPrice: number | null;
  };
  confidence: number;
  source: RecommendationSource;
}


export interface FlashDealConfig {
  enabled: boolean;
  maxDealsPerSession: number;
  countdownSeconds: number;
  discountPercent: number;
}

export interface AdminAIConfig {
  id: UUID;
  date: ISODateString;
  dailyPrompt: string;
  promotedItems: UUID[];
  suppressedItems: UUID[];
  flashDealConfig: FlashDealConfig;
  createdBy: UUID;
  createdAt: ISODateString;
}

export interface HappyHourDeal {
  id: UUID;
  productId: UUID;
  productName: string | null;
  discountPercent: number;
  startTime: string; // "HH:mm" wall-clock, interpreted in the restaurant's timezone
  endTime: string; // "HH:mm"
  isEnabled: boolean;
  updatedAt: ISODateString;
}

// Deterministic flash-deal config (admin). Drives the post-order claimable popup.
export interface FlashDealRule {
  id: UUID;
  productId: UUID;
  productName: string | null;
  discountPercent: number;
  minOrderTotal: number | null; // null = offer after every order
  isEnabled: boolean;
  updatedAt: ISODateString;
}

// The customer-facing happy hour when it's live now (from GET /happy-hour).
export interface ActiveHappyHour {
  productId: UUID;
  productName: string;
  discountPercent: number;
  originalPrice: number;
  discountedPrice: number;
  startTime: string; // "HH:mm"
  endTime: string; // "HH:mm"
}

export interface RecommendationDirective {
  id: UUID;
  rawPrompt: string | null;
  parsedRules: string | null;
  createdAt: ISODateString;
  isActive: boolean | null;
}

export interface RecommendationDirectiveRule {
  trigger_product_ids: UUID[];
  recommend_product_ids: UUID[];
  reason: string;
  confidence: number;
}

export interface ParsedRecommendationDirective {
  rules: RecommendationDirectiveRule[];
  unresolved: string[];
}


export type SimilarityRelationship = 'pairsWith' | 'upgradeTo' | 'followWith' | 'sameVibe';

export interface SimilarityRule {
  id: UUID;
  productAId: UUID;
  productAName: string | null;
  productBId: UUID;
  productBName: string | null;
  score: number;
  ruleType: string | null;
  createdAt: ISODateString;
}


export interface DailyMetrics {
  date: ISODateString;
  flashDealsShown: number;
  flashDealsClaimed: number;
  recommendationClicks: number;
  upsellRevenue: number;
}


// ── White-label branding ─────────────────────────────────────────────────────
export interface RestaurantBranding {
  enabled: boolean;
  primary: string | null;
  secondary: string | null;
  background: string | null;
  logoUrl: string | null;
  photoUrl: string | null;
  presetKey?: string | null;
  logoBackground: string | null;
  logoIsDark: boolean | null;
}
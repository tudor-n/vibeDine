import type {
    HappyHourDeal,
    FlashDealRule,
    RecommendationDirective,
    SimilarityRule,
    DailyMetrics,
    AdminCategory,
    AdminProduct,
    AdminMenuTree,
    AdminIngredient,
    ProductStock,
    StockTrackingMode,
    StockUnitCode,
    RestaurantBranding,
    AuxTableRow,
    MoveOrderToAuxPayload,
    MoveOrderToAuxResult,
    AuxOrderMoveResult,
} from '@shared/types/models';
import { useAuthStore, type UserProfile } from '@/stores/useAuthStore';
import { assertBrandingAssetSize, type BrandingAssetKind } from '@/services/brandingUpload';

export interface CreateCategoryPayload {
    name: string;
    sortOrder?: number | null;
    imageUrl?: string | null;
}

export interface UpdateCategoryPayload {
    name?: string;
    sortOrder?: number | null;
    imageUrl?: string | null;
}

export interface CreateProductPayload {
    categoryId: string;
    name: string;
    description?: string | null;
    price: number;
    imageUrl?: string | null;
    isAvailable?: boolean;
}

export interface UpdateProductPayload {
    categoryId?: string;
    name?: string;
    description?: string | null;
    price?: number;
    imageUrl?: string | null;
    isAvailable?: boolean;
}

export interface CreateIngredientPayload {
    name: string;
    unit: StockUnitCode;
    quantityOnHand: number;
    lowStockThreshold: number;
}

export interface UpdateIngredientPayload {
    name?: string;
    unit?: StockUnitCode;
    lowStockThreshold?: number;
}

/** Restock / adjust. Pass `setTo` for an absolute count, or `delta` for a relative +/- change. */
export interface AdjustStockPayload {
    delta?: number;
    setTo?: number;
}

export interface RecipeLinePayload {
    ingredientId: string;
    quantityPerPortion: number;
}

export interface SetProductStockPayload {
    mode: StockTrackingMode;
    recipe?: RecipeLinePayload[];
    simpleQuantity?: number;
    simpleUnit?: StockUnitCode;
    simpleThreshold?: number;
}

export interface AuthResult {
    accessToken: string;
    expiresAt: string;
    refreshToken: string;
    user: UserProfile;
}

export interface RegisterResult {
    status: 'confirmation_sent';
    email: string;
    message: string;
}

export class EmailNotConfirmedError extends Error {
    email: string;
    constructor(email: string, message: string) {
        super(message);
        this.name = 'EmailNotConfirmedError';
        this.email = email;
    }
}

export class WaiterPendingApprovalError extends Error {
    email: string;
    constructor(email: string, message: string) {
        super(message);
        this.name = 'WaiterPendingApprovalError';
        this.email = email;
    }
}

export interface OrganizationSummary {
    id: string;
    name: string;
    slug: string;
    role: string;
    createdAt: string;
}

export interface RestaurantSummary {
    id: string;
    organizationId: string;
    name: string;
    slug: string;
    address: string | null;
    timezone: string;
    currency: string;
    isActive: boolean;
}

export interface InvitationSummary {
    id: string;
    email: string;
    restaurantId: string | null;
    role: string;
    status: string;
    token: string;
    expiresAt: string;
    acceptPath: string;
}

const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005';
const BASE_URL = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl;

async function parseOrThrow<T>(response: Response): Promise<T> {
    if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        if (errorData?.code === 'email_not_confirmed') {
            throw new EmailNotConfirmedError(
                errorData.email ?? '',
                errorData.message ?? 'Email neconfirmat.',
            );
        }
        if (errorData?.code === 'waiter_pending_approval') {
            throw new WaiterPendingApprovalError(
                errorData.email ?? '',
                errorData.message ?? 'Contul tău așteaptă aprobarea.',
            );
        }
        throw new Error(errorData.message || errorData.error || `API Error: ${response.status}`);
    }
    if (response.status === 204) return {} as T;
    // Tolerate empty 200 bodies (some endpoints return Ok() with no payload);
    // calling response.json() on those throws "Unexpected end of JSON input".
    const text = await response.text();
    return (text ? JSON.parse(text) : {}) as T;
}

/** Unauthenticated request — used for login / register / refresh. */
async function fetcher<T>(endpoint: string, options?: RequestInit): Promise<T> {
    const response = await fetch(`${BASE_URL}${endpoint}`, {
        ...options,
        headers: { 'Content-Type': 'application/json', ...options?.headers },
    });
    return parseOrThrow<T>(response);
}

/** Authenticated request. Attaches the bearer token and refreshes once on 401. */
async function authedFetcher<T>(endpoint: string, options?: RequestInit): Promise<T> {
    const auth = useAuthStore();

    const doFetch = () =>
        fetch(`${BASE_URL}${endpoint}`, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                ...(auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
                ...options?.headers,
            },
        });

    let response = await doFetch();
    if (response.status === 401 && (await auth.tryRefresh())) {
        response = await doFetch();
    }
    return parseOrThrow<T>(response);
}

/** Builds a restaurant-scoped path from the active restaurant in the auth store. */
function scoped(suffix: string): string {
    const auth = useAuthStore();
    const rid = auth.activeRestaurantId;
    if (!rid) throw new Error('No active restaurant selected.');
    return `/api/restaurants/${rid}${suffix}`;
}

// ── Auth ─────────────────────────────────────────────────────────────────────
export const authApi = {
    login: (email: string, password: string) =>
        fetcher<AuthResult>('/api/auth/login', {
            method: 'POST',
            body: JSON.stringify({ email, password }),
        }),

    register: (email: string, password: string, fullName: string) =>
        fetcher<RegisterResult>('/api/auth/register', {
            method: 'POST',
            body: JSON.stringify({ email, password, fullName }),
        }),

    confirmEmail: (userId: string, token: string) =>
        fetcher<AuthResult>('/api/auth/confirm-email', {
            method: 'POST',
            body: JSON.stringify({ userId, token }),
        }),

    resendConfirmation: (email: string) =>
        fetcher<{ message: string }>('/api/auth/resend-confirmation', {
            method: 'POST',
            body: JSON.stringify({ email }),
        }),

    refresh: (refreshToken: string) =>
        fetcher<AuthResult>('/api/auth/refresh', {
            method: 'POST',
            body: JSON.stringify({ refreshToken }),
        }),

    me: () => authedFetcher<UserProfile>('/api/auth/me'),

    acceptInvite: (token: string) =>
        authedFetcher<UserProfile>('/api/auth/accept-invite', {
            method: 'POST',
            body: JSON.stringify({ token }),
        }),
};

// ── Organizations & restaurants ──────────────────────────────────────────────
export const orgApi = {
    listOrganizations: () =>
        authedFetcher<OrganizationSummary[]>('/api/organizations'),

    createOrganization: (name: string) =>
        authedFetcher<OrganizationSummary>('/api/organizations', {
            method: 'POST',
            body: JSON.stringify({ name }),
        }),

    listRestaurants: (orgId: string) =>
        authedFetcher<RestaurantSummary[]>(`/api/organizations/${orgId}/restaurants`),

    createRestaurant: (
        orgId: string,
        payload: { name: string; address?: string; timezone?: string; currency?: string },
    ) =>
        authedFetcher<RestaurantSummary>(`/api/organizations/${orgId}/restaurants`, {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    listInvitations: (orgId: string) =>
        authedFetcher<InvitationSummary[]>(`/api/organizations/${orgId}/invitations`),

    inviteCoOwner: (orgId: string, email: string, restaurantId: string) =>
        authedFetcher<InvitationSummary>(`/api/organizations/${orgId}/invitations`, {
            method: 'POST',
            body: JSON.stringify({ email, restaurantId }),
        }),

    revokeInvitation: (orgId: string, inviteId: string) =>
        authedFetcher<void>(`/api/organizations/${orgId}/invitations/${inviteId}`, {
            method: 'DELETE',
        }),

    listEmployees: (orgId: string) =>
        authedFetcher<EmployeeRow[]>(`/api/organizations/${orgId}/employees`),
};

export interface EmployeeRow {
    membershipId: string;
    userId: string;
    fullName: string;
    email: string;
    avatarUrl: string | null;
    role: string;
    restaurantId: string | null;
    restaurantName: string | null;
    status: string;
    createdAt: string;
}

// ── Waiter signup links & approval ───────────────────────────────────────────
export interface WaiterSignupLink {
    token: string;
    signupPath: string;
    isActive: boolean;
    createdAt: string;
}

export interface WaiterSignupPreview {
    organizationName: string;
    restaurantName: string;
}

export interface WaiterSignupResult {
    status: 'confirmation_sent';
    email: string;
    message: string;
}

export interface WaiterRow {
    membershipId: string;
    userId: string;
    fullName: string;
    email: string;
    avatarUrl: string | null;
    restaurantId: string;
    restaurantName: string;
    status: string;
    createdAt: string;
    emailConfirmed: boolean;
}

export const waiterApi = {
    getLink: (restaurantId: string) =>
        authedFetcher<WaiterSignupLink>(`/api/restaurants/${restaurantId}/waiter-link`),

    regenerateLink: (restaurantId: string) =>
        authedFetcher<WaiterSignupLink>(`/api/restaurants/${restaurantId}/waiter-link/regenerate`, {
            method: 'POST',
        }),

    revokeLink: (restaurantId: string) =>
        authedFetcher<void>(`/api/restaurants/${restaurantId}/waiter-link/revoke`, {
            method: 'POST',
        }),

    listByRestaurant: (restaurantId: string, status?: string) => {
        const q = status ? `?status=${encodeURIComponent(status)}` : '';
        return authedFetcher<WaiterRow[]>(`/api/restaurants/${restaurantId}/waiters${q}`);
    },

    listPendingForOrg: (orgId: string) =>
        authedFetcher<WaiterRow[]>(`/api/organizations/${orgId}/waiters/pending`),

    approve: (membershipId: string) =>
        authedFetcher<void>(`/api/waiters/${membershipId}/approve`, { method: 'POST' }),

    reject: (membershipId: string) =>
        authedFetcher<void>(`/api/waiters/${membershipId}/reject`, { method: 'POST' }),

    remove: (membershipId: string) =>
        authedFetcher<void>(`/api/waiters/${membershipId}`, { method: 'DELETE' }),
};

// ── Profile: own account, avatar upload ──────────────────────────────────────
export interface AvatarResponse {
    avatarUrl: string;
}

export const profileApi = {
    updateName: (fullName: string) =>
        authedFetcher<void>('/api/auth/me', {
            method: 'PATCH',
            body: JSON.stringify({ fullName }),
        }),

    uploadAvatar: async (file: File): Promise<AvatarResponse> => {
        const auth = useAuthStore();
        const form = new FormData();
        form.append('file', file);

        const doFetch = () =>
            fetch(`${BASE_URL}/api/auth/me/avatar`, {
                method: 'POST',
                body: form,
                headers: {
                    ...(auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
                },
            });

        let response = await doFetch();
        if (response.status === 401 && (await auth.tryRefresh())) {
            response = await doFetch();
        }
        return parseOrThrow<AvatarResponse>(response);
    },

    deleteAvatar: () =>
        authedFetcher<void>('/api/auth/me/avatar', { method: 'DELETE' }),
};

// Public (unauthenticated) waiter signup endpoints.
export const waiterPublicApi = {
    preview: (token: string) =>
        fetcher<WaiterSignupPreview>(`/api/waiter-signup/${token}/preview`),

    signup: (token: string, email: string, password: string, fullName: string) =>
        fetcher<WaiterSignupResult>(`/api/waiter-signup/${token}`, {
            method: 'POST',
            body: JSON.stringify({ email, password, fullName }),
        }),
};

// ── Tables & QR (per active location) ────────────────────────────────────────
export interface TableSummary {
    id: string;
    tableNumber: string | null;
    qrToken: string;
    zoneId: string | null;
    positionX: number;
    positionY: number;
    capacity: number;
    shape: string;
}

export const tablesApi = {
    list: () => authedFetcher<TableSummary[]>(scoped('/admin/tables')),

    create: (payload: {
        tableNumber: string;
        zoneId?: string | null;
        positionX?: number;
        positionY?: number;
        capacity?: number;
        shape?: string;
    }) =>
        authedFetcher<TableSummary>(scoped('/admin/tables'), {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    update: (
        id: string,
        patch: {
            tableNumber?: string;
            zoneId?: string | null;
            positionX?: number;
            positionY?: number;
            capacity?: number;
            shape?: string;
        },
    ) =>
        authedFetcher<TableSummary>(scoped(`/admin/tables/${id}`), {
            method: 'PATCH',
            body: JSON.stringify(patch),
        }),

    regenerate: (id: string) =>
        authedFetcher<TableSummary>(scoped(`/admin/tables/${id}/regenerate`), {
            method: 'POST',
        }),

    remove: (id: string) =>
        authedFetcher<void>(scoped(`/admin/tables/${id}`), {
            method: 'DELETE',
        }),
};

// ── Zones (floor-plan areas, per active location) ────────────────────────────
export interface ZoneSummary {
    id: string;
    name: string;
    sortOrder: number;
}

export const zonesApi = {
    list: () => authedFetcher<ZoneSummary[]>(scoped('/admin/zones')),

    create: (name: string) =>
        authedFetcher<ZoneSummary>(scoped('/admin/zones'), {
            method: 'POST',
            body: JSON.stringify({ name }),
        }),

    update: (id: string, patch: { name?: string; sortOrder?: number }) =>
        authedFetcher<ZoneSummary>(scoped(`/admin/zones/${id}`), {
            method: 'PATCH',
            body: JSON.stringify(patch),
        }),

    remove: (id: string) =>
        authedFetcher<void>(scoped(`/admin/zones/${id}`), {
            method: 'DELETE',
        }),
};

// ── Aux tables (mese auxiliare) ──────────────────────────────────────────────
export const auxApi = {
    list: () => authedFetcher<AuxTableRow[]>(scoped('/admin/aux-tables')),

    moveOrderToAux: (orderId: string, payload: MoveOrderToAuxPayload) =>
        authedFetcher<MoveOrderToAuxResult>(scoped(`/admin/orders/${orderId}/move-to-aux`), {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    moveToTable: (orderId: string, tableId: string) =>
        authedFetcher<AuxOrderMoveResult>(scoped(`/admin/aux-tables/orders/${orderId}/move-to-table`), {
            method: 'POST',
            body: JSON.stringify({ tableId }),
        }),

    reassign: (orderId: string, waiterId: string) =>
        authedFetcher<AuxOrderMoveResult>(scoped(`/admin/aux-tables/orders/${orderId}/reassign`), {
            method: 'POST',
            body: JSON.stringify({ waiterId }),
        }),
}

export interface WaiterCall {
    requestId: string;
    tableId: string;
    tableNumber: string | null;
    createdAt: string;
}

export type ActiveOrderStatus = 'submitted' | 'preparing' | 'ready';

export interface ActiveOrderItem {
    id: string;
    productName: string;
    quantity: number;
    unitPrice: number;
    subtotal: number;
    wasAiRecommended?: boolean;
    isFlashDealItem?: boolean;
}

export interface ActiveOrder {
    id: string;
    orderNumber: number | null;
    status: ActiveOrderStatus;
    totalAmount: number;
    createdAt: string;
    readyAt: string | null;
    tableId: string;
    tableNumber: string | null;
    // Claim ("Preia comanda") state — null while the order is unclaimed.
    claimedByWaiterId: string | null;
    claimedByName: string | null;
    claimedAt: string | null;
    // Aux-table state (masa auxiliară) — present once the backend slice ships.
    isAuxTable?: boolean;
    auxWaiterName?: string | null;
    movedFromTableId?: string | null;
    items: ActiveOrderItem[];
}

export interface OrderClaimResult {
    id: string;
    claimedByWaiterId: string | null;
    claimedByName?: string | null;
    claimedAt?: string | null;
}

// ── Restaurant-scoped dashboard API ──────────────────────────────────────────
export const apiClient = {
    getMetrics: () =>
        authedFetcher<DailyMetrics>(scoped('/admin/metrics')),

    getOrders: (limit = 100) =>
        authedFetcher<any[]>(scoped(`/admin/orders?limit=${limit}`)),

    getActiveOrders: () =>
        authedFetcher<ActiveOrder[]>(scoped('/admin/orders/active')),

    updateOrderStatus: (orderId: string, status: ActiveOrderStatus) =>
        authedFetcher<any>(scoped(`/admin/orders/${orderId}/status`), {
            method: 'PATCH',
            body: JSON.stringify({ status }),
        }),

    cancelOrder: (orderId: string) =>
        authedFetcher<{ id: string; status: string }>(scoped(`/admin/orders/${orderId}/cancel`), {
            method: 'POST',
        }),

    // "Preia comanda" — take ownership of an order from the floor plan.
    claimOrder: (orderId: string) =>
        authedFetcher<OrderClaimResult>(scoped(`/admin/orders/${orderId}/claim`), {
            method: 'POST',
        }),

    // Hand a claimed order back to the pool.
    releaseOrder: (orderId: string) =>
        authedFetcher<OrderClaimResult>(scoped(`/admin/orders/${orderId}/release`), {
            method: 'POST',
        }),

    // Waiter calls (help requests) — the live queue shown on the staff dashboard.
    getHelpRequests: () =>
        authedFetcher<WaiterCall[]>(scoped('/help-requests')),

    acknowledgeHelpRequest: (requestId: string) =>
        authedFetcher<{ alreadyAcknowledged?: boolean }>(
            scoped(`/help-requests/${requestId}/acknowledge`),
            { method: 'POST' },
        ),

    getHappyHour: () =>
        authedFetcher<HappyHourDeal | null>(scoped('/admin/happy-hour')),

    saveHappyHour: (payload: {
        productId: string;
        discountPercent: number;
        startTime: string;
        endTime: string;
        isEnabled: boolean;
    }) =>
        authedFetcher<HappyHourDeal>(scoped('/admin/happy-hour'), {
            method: 'PUT',
            body: JSON.stringify(payload),
        }),

    clearHappyHour: () =>
        authedFetcher<void>(scoped('/admin/happy-hour'), {
            method: 'DELETE',
        }),

    getFlashDeal: () =>
        authedFetcher<FlashDealRule | null>(scoped('/admin/flash-deal')),

    saveFlashDeal: (payload: {
        productId: string;
        discountPercent: number;
        minOrderTotal: number | null;
        isEnabled: boolean;
    }) =>
        authedFetcher<FlashDealRule>(scoped('/admin/flash-deal'), {
            method: 'PUT',
            body: JSON.stringify(payload),
        }),

    clearFlashDeal: () =>
        authedFetcher<void>(scoped('/admin/flash-deal'), {
            method: 'DELETE',
        }),

    getRecommendationDirectives: () =>
        authedFetcher<RecommendationDirective[]>(scoped('/admin/recommendation-directives')),

    postRecommendationDirective: (prompt: string) =>
        authedFetcher<RecommendationDirective>(scoped('/admin/recommendation-directives'), {
            method: 'POST',
            body: JSON.stringify({ rawPrompt: prompt }),
        }),

    toggleRecommendationDirective: (id: string, isActive: boolean) =>
        authedFetcher<void>(scoped(`/admin/recommendation-directives/${id}`), {
            method: 'PATCH',
            body: JSON.stringify(isActive),
        }),

    deleteRecommendationDirective: (id: string) =>
        authedFetcher<void>(scoped(`/admin/recommendation-directives/${id}`), {
            method: 'DELETE',
        }),

    getSimilarityRules: () =>
        authedFetcher<SimilarityRule[]>(scoped('/admin/similarity-rules')),

    postSimilarityRule: (rulePayload: any) =>
        authedFetcher<SimilarityRule>(scoped('/admin/similarity-rules'), {
            method: 'POST',
            body: JSON.stringify(rulePayload),
        }),

    deleteSimilarityRule: (id: string) =>
        authedFetcher<void>(scoped(`/admin/similarity-rules/${id}`), {
            method: 'DELETE',
        }),

    autoGenerateRules: () =>
        authedFetcher<SimilarityRule[]>(scoped('/admin/similarity-rules/auto-generate'), {
            method: 'POST',
        }),

    getMenu: () =>
        authedFetcher<{ categories: Array<{ id: string; name: string; products: Array<{ id: string; name: string }> }> }>(
            scoped('/menu'),
        ),

    getAdminMenu: () =>
        authedFetcher<AdminMenuTree>(scoped('/admin/menu')),

    getAdminCategories: () =>
        authedFetcher<AdminCategory[]>(scoped('/admin/menu/categories')),

    createCategory: (payload: CreateCategoryPayload) =>
        authedFetcher<AdminCategory>(scoped('/admin/menu/categories'), {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    updateCategory: (id: string, payload: UpdateCategoryPayload) =>
        authedFetcher<AdminCategory>(scoped(`/admin/menu/categories/${id}`), {
            method: 'PATCH',
            body: JSON.stringify(payload),
        }),

    deleteCategory: (id: string) =>
        authedFetcher<void>(scoped(`/admin/menu/categories/${id}`), {
            method: 'DELETE',
        }),

    createProduct: (payload: CreateProductPayload) =>
        authedFetcher<AdminProduct>(scoped('/admin/menu/products'), {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    updateProduct: (id: string, payload: UpdateProductPayload) =>
        authedFetcher<AdminProduct>(scoped(`/admin/menu/products/${id}`), {
            method: 'PATCH',
            body: JSON.stringify(payload),
        }),

    softDeleteProduct: (id: string) =>
        authedFetcher<void>(scoped(`/admin/menu/products/${id}`), {
            method: 'DELETE',
        }),

    restoreProduct: (id: string) =>
        authedFetcher<AdminProduct>(scoped(`/admin/menu/products/${id}/restore`), {
            method: 'POST',
        }),

    reorderProducts: (categoryId: string, productIds: string[]) =>
        authedFetcher<void>(scoped(`/admin/menu/categories/${categoryId}/products/reorder`), {
            method: 'POST',
            body: JSON.stringify({ productIds }),
        }),

    // ── Inventory / stock ────────────────────────────────────────────────────
    getIngredients: (opts?: { lowStockOnly?: boolean; includeLinked?: boolean }) => {
        const params = new URLSearchParams();
        if (opts?.lowStockOnly) params.set('lowStockOnly', 'true');
        if (opts?.includeLinked === false) params.set('includeLinked', 'false');
        const q = params.toString();
        return authedFetcher<AdminIngredient[]>(
            scoped(`/admin/inventory/ingredients${q ? `?${q}` : ''}`),
        );
    },

    createIngredient: (payload: CreateIngredientPayload) =>
        authedFetcher<AdminIngredient>(scoped('/admin/inventory/ingredients'), {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    updateIngredient: (id: string, payload: UpdateIngredientPayload) =>
        authedFetcher<AdminIngredient>(scoped(`/admin/inventory/ingredients/${id}`), {
            method: 'PATCH',
            body: JSON.stringify(payload),
        }),

    adjustIngredient: (id: string, payload: AdjustStockPayload) =>
        authedFetcher<AdminIngredient>(scoped(`/admin/inventory/ingredients/${id}/adjust`), {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    deleteIngredient: (id: string) =>
        authedFetcher<void>(scoped(`/admin/inventory/ingredients/${id}`), {
            method: 'DELETE',
        }),

    getProductStock: (productId: string) =>
        authedFetcher<ProductStock>(scoped(`/admin/inventory/products/${productId}/stock`)),

    setProductStock: (productId: string, payload: SetProductStockPayload) =>
        authedFetcher<ProductStock>(scoped(`/admin/inventory/products/${productId}/stock`), {
            method: 'PUT',
            body: JSON.stringify(payload),
        }),
}

// ── White-label branding ──────────────────────────────────────────────────────
export interface BrandingUpdatePayload {
    enabled: boolean;
    primary: string | null;
    secondary: string | null;
    background: string | null;
    presetKey: string | null;
    logoBackground: string | null;
    logoIsDark: boolean | null;
    menuBackgroundStyle: string | null;
}

export interface BrandingAssetResponse {
    logoUrl?: string;
    photoUrl?: string;
    menuBackgroundUrl?: string;
}

// NOTE: deliberately bypasses authedFetcher for the upload calls. authedFetcher
// unconditionally sets `Content-Type: application/json` (see line ~184), which
// would stomp the multipart boundary the browser needs to set on a FormData body
// (the same reason profileApi.uploadAvatar above does its own raw fetch instead
// of going through authedFetcher). Mirrors that exact pattern, scoped to the
// active restaurant.
async function uploadBrandingAsset(kind: BrandingAssetKind, file: File): Promise<BrandingAssetResponse> {
    // Reject oversize files before the request so the user gets a clear message
    // instead of the server's cross-origin "Failed to fetch" on RequestSizeLimit.
    assertBrandingAssetSize(kind, file);

    const auth = useAuthStore();
    const form = new FormData();
    form.append('file', file);

    const doFetch = () =>
        fetch(`${BASE_URL}${scoped(`/admin/branding/${kind}`)}`, {
            method: 'POST',
            body: form,
            headers: {
                ...(auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
            },
        });

    let response = await doFetch();
    if (response.status === 401 && (await auth.tryRefresh())) {
        response = await doFetch();
    }
    return parseOrThrow<BrandingAssetResponse>(response);
}

export const brandingApi = {
    get: () => authedFetcher<RestaurantBranding>(scoped('/admin/branding')),

    update: (payload: BrandingUpdatePayload) =>
        authedFetcher<RestaurantBranding>(scoped('/admin/branding'), {
            method: 'PUT',
            body: JSON.stringify(payload),
        }),

    uploadLogo: (file: File) => uploadBrandingAsset('logo', file),
    uploadPhoto: (file: File) => uploadBrandingAsset('photo', file),
    uploadMenuBackground: (file: File) => uploadBrandingAsset('menu-background', file),

    // Remove a stored asset (logo / photo / menu-background). Returns 204.
    remove: (kind: BrandingAssetKind) =>
        authedFetcher<void>(scoped(`/admin/branding/${kind}`), { method: 'DELETE' }),
};

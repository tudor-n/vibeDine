import type {
    AIDirective,
    RecommendationDirective,
    SimilarityRule,
    DailyMetrics,
    AdminCategory,
    AdminProduct,
    AdminMenuTree,
} from '@shared/types/models';
import { useAuthStore, type UserProfile } from '@/stores/useAuthStore';

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
}

export const tablesApi = {
    list: () => authedFetcher<TableSummary[]>(scoped('/admin/tables')),

    create: (tableNumber: string) =>
        authedFetcher<TableSummary>(scoped('/admin/tables'), {
            method: 'POST',
            body: JSON.stringify({ tableNumber }),
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

export interface WaiterCall {
    requestId: string;
    tableId: string;
    tableNumber: string | null;
    createdAt: string;
}

// ── Restaurant-scoped dashboard API ──────────────────────────────────────────
export const apiClient = {
    getMetrics: () =>
        authedFetcher<DailyMetrics>(scoped('/admin/metrics')),

    getOrders: (limit = 100) =>
        authedFetcher<any[]>(scoped(`/admin/orders?limit=${limit}`)),

    getActiveOrders: () =>
        authedFetcher<any[]>(scoped('/admin/orders/active')),

    updateOrderStatus: (orderId: string, status: 'submitted' | 'preparing' | 'ready') =>
        authedFetcher<any>(scoped(`/admin/orders/${orderId}/status`), {
            method: 'PATCH',
            body: JSON.stringify({ status }),
        }),

    // Waiter calls (help requests) — the live queue shown on the staff dashboard.
    getHelpRequests: () =>
        authedFetcher<WaiterCall[]>(scoped('/help-requests')),

    acknowledgeHelpRequest: (requestId: string) =>
        authedFetcher<{ alreadyAcknowledged?: boolean }>(
            scoped(`/help-requests/${requestId}/acknowledge`),
            { method: 'POST' },
        ),

    getDirectives: () =>
        authedFetcher<AIDirective[]>(scoped('/admin/directives')),

    postDirective: (prompt: string) =>
        authedFetcher<AIDirective>(scoped('/admin/directives'), {
            method: 'POST',
            body: JSON.stringify({ rawPrompt: prompt }),
        }),

    toggleActiveDirective: (id: string, isActive: boolean) =>
        authedFetcher<void>(scoped(`/admin/directives/${id}`), {
            method: 'PATCH',
            body: JSON.stringify(isActive),
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
}

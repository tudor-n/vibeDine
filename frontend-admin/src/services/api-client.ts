import type {
    AIDirective,
    RecommendationDirective,
    SimilarityRule,
    DailyMetrics,
    AdminCategory,
    AdminProduct,
    AdminMenuTree,
} from '@shared/types/models';

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

  if(!response.ok){
    const errorData = await response.json().catch(() => ({}));
    throw new Error(errorData.message || errorData.error || `API Error: ${response.status}`);
  }

  if (response.status === 204){
    return {} as T;
  }

  return response.json();
} 

export const apiClient = {
    getMetrics: () => 
        fetcher<DailyMetrics>('/api/admin/metrics'),

    getOrders: (limit = 100) =>
        fetcher<any[]>(`/api/admin/orders?limit=${limit}`),

    getDirectives: () => 
        fetcher<AIDirective[]>('/api/admin/directives'),

    postDirective: (prompt: string) => 
        fetcher<AIDirective>('/api/admin/directives', {
      method: 'POST',
      body: JSON.stringify({ rawPrompt: prompt }),
    }),

    toggleActiveDirective: (id: string, isActive: boolean) =>
        fetcher<void>(`/api/admin/directives/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(isActive)
    }),

    getRecommendationDirectives: () =>
        fetcher<RecommendationDirective[]>('/api/admin/recommendation-directives'),

    postRecommendationDirective: (prompt: string) =>
        fetcher<RecommendationDirective>('/api/admin/recommendation-directives', {
            method: 'POST',
            body: JSON.stringify({ rawPrompt: prompt }),
        }),

    toggleRecommendationDirective: (id: string, isActive: boolean) =>
        fetcher<void>(`/api/admin/recommendation-directives/${id}`, {
            method: 'PATCH',
            body: JSON.stringify(isActive),
        }),

    deleteRecommendationDirective: (id: string) =>
        fetcher<void>(`/api/admin/recommendation-directives/${id}`, {
            method: 'DELETE',
        }),

    getSimilarityRules: () =>
        fetcher<SimilarityRule[]>('/api/admin/similarity-rules'),

    postSimilarityRule: (rulePayload: any) => 
        fetcher<SimilarityRule>('/api/admin/similarity-rules', {
          method: 'POST',
          body: JSON.stringify(rulePayload)
        }),

    deleteSimilarityRule: (id: string) =>
        fetcher<void>(`/api/admin/similarity-rules/${id}`, {
            method: 'DELETE'
        }),

    autoGenerateRules: () =>
        fetcher<SimilarityRule[]>('/api/admin/similarity-rules/auto-generate', {
            method: 'POST'
        }),

    getMenu: () =>
        fetcher<{ categories: Array<{ id: string; name: string; products: Array<{ id: string; name: string }> }> }>('/api/menu'),

    getAdminMenu: () =>
        fetcher<AdminMenuTree>('/api/admin/menu'),

    getAdminCategories: () =>
        fetcher<AdminCategory[]>('/api/admin/menu/categories'),

    createCategory: (payload: CreateCategoryPayload) =>
        fetcher<AdminCategory>('/api/admin/menu/categories', {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    updateCategory: (id: string, payload: UpdateCategoryPayload) =>
        fetcher<AdminCategory>(`/api/admin/menu/categories/${id}`, {
            method: 'PATCH',
            body: JSON.stringify(payload),
        }),

    deleteCategory: (id: string) =>
        fetcher<void>(`/api/admin/menu/categories/${id}`, {
            method: 'DELETE',
        }),

    createProduct: (payload: CreateProductPayload) =>
        fetcher<AdminProduct>('/api/admin/menu/products', {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    updateProduct: (id: string, payload: UpdateProductPayload) =>
        fetcher<AdminProduct>(`/api/admin/menu/products/${id}`, {
            method: 'PATCH',
            body: JSON.stringify(payload),
        }),

    softDeleteProduct: (id: string) =>
        fetcher<void>(`/api/admin/menu/products/${id}`, {
            method: 'DELETE',
        }),

    restoreProduct: (id: string) =>
        fetcher<AdminProduct>(`/api/admin/menu/products/${id}/restore`, {
            method: 'POST',
        }),

    reorderProducts: (categoryId: string, productIds: string[]) =>
        fetcher<void>(`/api/admin/menu/categories/${categoryId}/products/reorder`, {
            method: 'POST',
            body: JSON.stringify({ productIds }),
        }),
}
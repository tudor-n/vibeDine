import type { AdminAIConfig, SimilarityRule } from '@shared/types/models'


const BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:4000';

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
        fetcher<any>('/api/admin/metrics'),

    getDirectives: () => 
        fetcher<AdminAIConfig[]>('/api/admin/directives'),

    postDirective: (prompt: string) => 
        fetcher<AdminAIConfig>('/api/admin/directives', {
      method: 'POST',
      body: JSON.stringify({ raw_prompt: prompt }),
    }),

    toggleActiveDirective: (id: string, isActive: boolean) =>
        fetcher<void>(`/api/admin/directives/${id}`, {
      method: 'PATCH',
      body: JSON.stringify({ is_active: isActive })
    }),

   
    getSimilarityRules: () => 
        fetcher<SimilarityRule[]>('/api/admin/similarity-rules'),
}

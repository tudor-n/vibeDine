import type { AIDirective, SimilarityRule , DailyMetrics } from '@shared/types/models';

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
        fetcher<SimilarityRule[]>('/api/admin/similarity-rules/generate', {
            method: 'POST'
        })
}
import { useAuthStore } from '@/stores/useAuthStore'
import type {
  AuthResponse,
  LocationDetail,
  LocationListItem,
  Member,
  Metrics,
  OrganizationDetail,
  OrganizationListItem,
  RegistrationLink,
} from '@/types/platform'

const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5006'
const BASE_URL = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl

async function parseOrThrow<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const data = await response.json().catch(() => ({}))
    throw new Error(data.message || data.error || `API Error: ${response.status}`)
  }
  if (response.status === 204) return {} as T
  const text = await response.text()
  return (text ? JSON.parse(text) : {}) as T
}

/** Unauthenticated request (login). */
async function fetcher<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${endpoint}`, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...options?.headers },
  })
  return parseOrThrow<T>(response)
}

/** Authenticated request — attaches the bearer token; logs out on 401. */
async function authed<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const auth = useAuthStore()
  const response = await fetch(`${BASE_URL}${endpoint}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
      ...options?.headers,
    },
  })
  if (response.status === 401) {
    auth.logout()
    throw new Error('Session expired. Please sign in again.')
  }
  return parseOrThrow<T>(response)
}

function qs(params: Record<string, string | undefined>): string {
  const entries = Object.entries(params).filter(([, v]) => v != null && v !== '')
  if (entries.length === 0) return ''
  return '?' + entries.map(([k, v]) => `${k}=${encodeURIComponent(v as string)}`).join('&')
}

export const authApi = {
  login: (email: string, password: string) =>
    fetcher<AuthResponse>('/api/platform/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),
  me: () => authed<{ id: string; email: string; fullName: string }>('/api/platform/auth/me'),
}

export const apiClient = {
  getMetrics: () => authed<Metrics>('/api/platform/metrics'),

  // ── Organizations ──────────────────────────────────────────────────────
  listOrganizations: (search?: string) =>
    authed<OrganizationListItem[]>(`/api/platform/organizations${qs({ search })}`),
  getOrganization: (id: string) =>
    authed<OrganizationDetail>(`/api/platform/organizations/${id}`),
  createOrganization: (payload: { name: string; slug?: string }) =>
    authed<OrganizationListItem>('/api/platform/organizations', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateOrganization: (id: string, payload: { name?: string; slug?: string }) =>
    authed<OrganizationListItem>(`/api/platform/organizations/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(payload),
    }),
  deleteOrganization: (id: string) =>
    authed<void>(`/api/platform/organizations/${id}`, { method: 'DELETE' }),

  // ── Locations ──────────────────────────────────────────────────────────
  listLocations: (params: { organizationId?: string; search?: string; status?: string } = {}) =>
    authed<LocationListItem[]>(`/api/platform/locations${qs(params)}`),
  getLocation: (id: string) => authed<LocationDetail>(`/api/platform/locations/${id}`),
  createLocation: (payload: {
    organizationId: string
    name: string
    address?: string
    timezone?: string
    currency?: string
    locale?: string
  }) =>
    authed<LocationListItem>('/api/platform/locations', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateLocation: (
    id: string,
    payload: { name?: string; address?: string; timezone?: string; currency?: string; locale?: string },
  ) =>
    authed<LocationListItem>(`/api/platform/locations/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(payload),
    }),
  setLocationStatus: (id: string, isActive: boolean) =>
    authed<{ id: string; isActive: boolean }>(`/api/platform/locations/${id}/status`, {
      method: 'PATCH',
      body: JSON.stringify({ isActive }),
    }),
  deleteLocation: (id: string, force = false) =>
    authed<void>(`/api/platform/locations/${id}${force ? '?force=true' : ''}`, { method: 'DELETE' }),

  // ── Members (admins + hires) ───────────────────────────────────────────
  listMembers: (locationId: string) =>
    authed<Member[]>(`/api/platform/locations/${locationId}/members`),
  setMemberStatus: (locationId: string, membershipId: string, status: string) =>
    authed<{ id: string; status: string }>(
      `/api/platform/locations/${locationId}/members/${membershipId}/status`,
      { method: 'PATCH', body: JSON.stringify({ status }) },
    ),
  removeMember: (locationId: string, membershipId: string) =>
    authed<void>(`/api/platform/locations/${locationId}/members/${membershipId}`, { method: 'DELETE' }),
  assignAdmin: (locationId: string, email: string) =>
    authed<Member>(`/api/platform/locations/${locationId}/members`, {
      method: 'POST',
      body: JSON.stringify({ email }),
    }),

  // ── Registration links ─────────────────────────────────────────────────
  listRegistrationLinks: (status?: string) =>
    authed<RegistrationLink[]>(`/api/platform/registration-links${qs({ status })}`),
  createRegistrationLink: (payload: {
    type: string
    organizationId?: string
    label?: string
    intendedEmail?: string
    expiresInDays?: number
  }) =>
    authed<RegistrationLink>('/api/platform/registration-links', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  revokeRegistrationLink: (id: string) =>
    authed<RegistrationLink>(`/api/platform/registration-links/${id}/revoke`, { method: 'POST' }),
}

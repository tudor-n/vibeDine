import { useAuthStore } from '@/stores/useAuthStore'
import type {
  AuditEntry,
  AuthResponse,
  Connection,
  ConnectionTestResult,
  CreateConnectionPayload,
  Deployment,
  FleetMetrics,
  FleetOverview,
  LogEntry,
  MetricPoint,
  OpsAdmin,
  Project,
  ProjectDetail,
  ProviderInfo,
  ServiceOverviewItem,
  Variable,
} from '@/types/ops'

const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5007'
export const BASE_URL = rawBaseUrl.endsWith('/') ? rawBaseUrl.slice(0, -1) : rawBaseUrl

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

function qs(params: Record<string, string | number | boolean | undefined | null>): string {
  const entries = Object.entries(params).filter(([, v]) => v != null && v !== '')
  if (entries.length === 0) return ''
  return '?' + entries.map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`).join('&')
}

export const authApi = {
  login: (email: string, password: string) =>
    fetcher<AuthResponse>('/api/ops/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),
  me: () => authed<OpsAdmin>('/api/ops/auth/me'),
}

const C = '/api/ops/connections'

export const opsApi = {
  // ── Providers & connections ──────────────────────────────────────────────
  listProviders: () => authed<ProviderInfo[]>('/api/ops/providers'),
  listConnections: () => authed<Connection[]>(C),
  getFleet: () => authed<FleetOverview>(`${C}/fleet`),
  getConnection: (id: string) => authed<Connection>(`${C}/${id}`),
  createConnection: (payload: CreateConnectionPayload) =>
    authed<Connection>(C, { method: 'POST', body: JSON.stringify(payload) }),
  testConnection: (id: string) =>
    authed<ConnectionTestResult>(`${C}/${id}/test`, { method: 'POST' }),
  updateConnection: (id: string, patch: { name?: string; role?: string | null }) =>
    authed<Connection>(`${C}/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  deleteConnection: (id: string) => authed<void>(`${C}/${id}`, { method: 'DELETE' }),

  // ── Resources ────────────────────────────────────────────────────────────
  listProjects: (connId: string) => authed<Project[]>(`${C}/${connId}/projects`),
  getOverview: (connId: string, p: { projectId: string; environmentId?: string }) =>
    authed<ServiceOverviewItem[]>(`${C}/${connId}/overview${qs(p)}`),
  getMetrics: (
    connId: string,
    p: { serviceId: string; environmentId: string; windowMinutes?: number },
  ) => authed<MetricPoint[]>(`${C}/${connId}/metrics${qs(p)}`),
  getAggregateMetrics: (
    connId: string,
    p: { projectId?: string; environmentId?: string; windowMinutes?: number; maxServices?: number },
  ) => authed<MetricPoint[]>(`${C}/${connId}/metrics/aggregate${qs(p)}`),
  getFleetMetrics: (p: { windowMinutes?: number; maxServices?: number }) =>
    authed<FleetMetrics>(`/api/ops/fleet/metrics${qs(p)}`),
  getProject: (connId: string, projectId: string) =>
    authed<ProjectDetail>(`${C}/${connId}/projects/${encodeURIComponent(projectId)}`),
  listDeployments: (
    connId: string,
    p: { projectId: string; serviceId?: string; environmentId?: string; first?: number },
  ) => authed<Deployment[]>(`${C}/${connId}/deployments${qs(p)}`),
  getDeployment: (connId: string, deploymentId: string) =>
    authed<Deployment>(`${C}/${connId}/deployments/${encodeURIComponent(deploymentId)}`),

  // ── Deployment control ───────────────────────────────────────────────────
  deploymentAction: (connId: string, deploymentId: string, action: string) =>
    authed<{ success: boolean; status: string | null; detail: string | null }>(
      `${C}/${connId}/deployments/${encodeURIComponent(deploymentId)}/${action}`,
      { method: 'POST' },
    ),
  triggerDeploy: (connId: string, body: { serviceId: string; environmentId: string }) =>
    authed<{ success: boolean }>(`${C}/${connId}/deploy`, { method: 'POST', body: JSON.stringify(body) }),

  // ── Logs ─────────────────────────────────────────────────────────────────
  getLogs: (
    connId: string,
    p: {
      logType: string
      deploymentId?: string
      projectId?: string
      serviceId?: string
      environmentId?: string
      filter?: string
      limit?: number
      archive?: boolean
    },
  ) => authed<LogEntry[]>(`${C}/${connId}/logs${qs(p)}`),
  getArchive: (connId: string, p: { deploymentId?: string; logType?: string; limit?: number }) =>
    authed<LogEntry[]>(`${C}/${connId}/logs/archive${qs(p)}`),

  // ── Variables ──────────────────────────────────────────────────────────────
  getVariables: (connId: string, p: { projectId: string; environmentId: string; serviceId?: string }) =>
    authed<Variable[]>(`${C}/${connId}/variables${qs(p)}`),
  upsertVariables: (
    connId: string,
    body: {
      projectId: string
      environmentId: string
      serviceId?: string
      variables: Record<string, string>
      replace: boolean
      skipDeploys: boolean
    },
  ) => authed<void>(`${C}/${connId}/variables`, { method: 'PUT', body: JSON.stringify(body) }),

  // ── Audit ──────────────────────────────────────────────────────────────────
  getAudit: (p: { connectionId?: string; limit?: number }) =>
    authed<AuditEntry[]>(`/api/ops/audit${qs(p)}`),
}

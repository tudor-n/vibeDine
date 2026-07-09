// Mirror the OpsApi DTOs (camelCase JSON).

export interface OpsAdmin {
  id: string
  email: string
  fullName: string
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  admin: OpsAdmin
}

export interface ProviderCapabilities {
  projects: boolean
  deployments: boolean
  triggerDeploy: boolean
  buildLogs: boolean
  deployLogs: boolean
  httpLogs: boolean
  liveTail: boolean
  variables: boolean
  providerAudit: boolean
  metrics: boolean
  redeploy: boolean
  restart: boolean
  rollback: boolean
  stop: boolean
  remove: boolean
  overview: boolean
  tokenKinds: string[]
}

export interface ProviderInfo {
  key: string
  capabilities: ProviderCapabilities
}

export interface Connection {
  id: string
  provider: string
  name: string
  role: string | null
  accountLabel: string | null
  status: string
  statusDetail: string | null
  createdAt: string
  lastCheckedAt: string | null
  capabilities: ProviderCapabilities
  metadata: Record<string, string | null>
}

export interface ConnectionTestResult {
  success: boolean
  accountLabel: string | null
  detail: string | null
}

export interface FleetItem {
  connectionId: string
  provider: string
  name: string
  role: string | null
  status: string
  statusDetail: string | null
  accountLabel: string | null
  lastCheckedAt: string | null
}

export interface FleetOverview {
  total: number
  active: number
  unhealthy: number
  servers: FleetItem[]
}

export interface Project {
  id: string
  name: string
  description: string | null
}

export interface ServiceItem {
  id: string
  name: string
}

export interface EnvironmentItem {
  id: string
  name: string
}

export interface ProjectDetail {
  id: string
  name: string
  description: string | null
  services: ServiceItem[]
  environments: EnvironmentItem[]
}

export interface Deployment {
  id: string
  status: string
  createdAt: string | null
  url: string | null
  staticUrl: string | null
  canRedeploy: boolean
  canRollback: boolean
}

export interface LogEntry {
  timestamp: string
  message: string
  severity: string | null
  logType: string
  attributes: Record<string, unknown> | null
}

export interface ServiceOverviewItem {
  serviceId: string
  serviceName: string
  status: string | null
  latestDeploymentId: string | null
  latestCreatedAt: string | null
  url: string | null
}

export interface MetricPoint {
  timestamp: string
  metric: string
  value: number
}

export interface ServerMetricSummary {
  connectionId: string
  name: string
  provider: string
  role: string | null
  supported: boolean
  error: string | null
  points: MetricPoint[]
}

export interface FleetMetrics {
  windowMinutes: number
  servers: ServerMetricSummary[]
}

export interface Variable {
  key: string
  value: string
}

export interface AuditEntry {
  id: string
  actorEmail: string | null
  action: string
  provider: string | null
  connectionId: string | null
  targetType: string | null
  targetId: string | null
  summary: string | null
  result: string
  createdAt: string
}

export interface CreateConnectionPayload {
  provider: string
  name: string
  role?: string | null
  token: string
  tokenKind?: string | null
  metadata: Record<string, string | null>
}

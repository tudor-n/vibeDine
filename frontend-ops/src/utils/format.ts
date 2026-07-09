export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function timeAgo(iso: string | null | undefined): string {
  if (!iso) return 'never'
  const d = new Date(iso).getTime()
  if (Number.isNaN(d)) return 'never'
  const secs = Math.round((Date.now() - d) / 1000)
  if (secs < 60) return `${secs}s ago`
  const mins = Math.round(secs / 60)
  if (mins < 60) return `${mins}m ago`
  const hours = Math.round(mins / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.round(hours / 24)}d ago`
}

/** Maps a deployment status to a badge class. */
export function deploymentBadgeClass(status: string): string {
  const s = status.toUpperCase()
  if (s === 'SUCCESS') return 'badge badge-green'
  if (s === 'FAILED' || s === 'CRASHED') return 'badge badge-red'
  if (s === 'BUILDING' || s === 'DEPLOYING' || s === 'QUEUED' || s === 'WAITING') return 'badge badge-amber'
  return 'badge'
}

/** Maps a connection status to a badge class. */
export function connectionBadgeClass(status: string): string {
  if (status === 'active') return 'badge badge-green'
  if (status === 'invalid') return 'badge badge-red'
  return 'badge badge-amber'
}

export function connectionDotColor(status: string): string {
  if (status === 'active') return 'var(--green)'
  if (status === 'invalid') return 'var(--red)'
  return 'var(--amber)'
}

export function severityClass(severity: string | null): string {
  const s = (severity ?? '').toLowerCase()
  if (s === 'error') return 'badge badge-red'
  if (s === 'warn') return 'badge badge-amber'
  return 'badge'
}

export function roleLabel(role: string | null | undefined): string {
  if (!role) return 'untagged'
  return role.charAt(0).toUpperCase() + role.slice(1)
}

// ── Metrics helpers (shared by the connection Metrics tab and the fleet Metrics page) ──
import type { MetricPoint } from '@/types/ops'

export interface MetricSeries {
  metric: string
  values: number[]
  last: number
  max: number
}

export function groupMetrics(points: MetricPoint[]): MetricSeries[] {
  const groups: Record<string, number[]> = {}
  for (const p of points) (groups[p.metric] ??= []).push(p.value)
  return Object.entries(groups).map(([metric, values]) => ({
    metric,
    values,
    last: values.length ? values[values.length - 1] : 0,
    max: values.length ? Math.max(...values) : 0,
  }))
}

export function metricLabel(m: string): string {
  const map: Record<string, string> = {
    CPU_USAGE: 'CPU',
    MEMORY_USAGE_GB: 'Memory (GB)',
    NETWORK_RX_GB: 'Network In (GB)',
    NETWORK_TX_GB: 'Network Out (GB)',
  }
  return map[m] ?? m
}

export function metricColor(m: string): string {
  if (m.startsWith('CPU')) return 'var(--accent)'
  if (m.startsWith('MEMORY')) return 'var(--blue)'
  if (m.startsWith('NETWORK')) return 'var(--green)'
  return 'var(--amber)'
}

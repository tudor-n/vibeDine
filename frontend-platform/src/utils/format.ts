export function formatDate(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

export function statusBadgeClass(status: string): string {
  if (status === 'active') return 'badge badge-green'
  if (status === 'pending_approval') return 'badge badge-amber'
  if (status === 'rejected' || status === 'revoked') return 'badge badge-red'
  if (status === 'used') return 'badge'
  return 'badge'
}

export function statusLabel(status: string): string {
  const map: Record<string, string> = {
    active: 'Active',
    pending_approval: 'Pending',
    rejected: 'Rejected',
    used: 'Used',
    revoked: 'Revoked',
  }
  return map[status] ?? status
}

export function linkTypeLabel(type: string): string {
  const map: Record<string, string> = {
    new_organization: 'New organization',
    new_location: 'New location',
  }
  return map[type] ?? type
}

import type {
  BulkCreateProductsRequest,
  MenuScanResponse,
} from '@shared/types/menu-import'
import type { AdminProduct } from '@shared/types/models'
import { useAuthStore } from '@/stores/useAuthStore'

export interface MenuImportApiError extends Error {
  status: number
  missingCategoryIds?: string[]
  body?: unknown
}

function makeError(message: string, status: number, body?: unknown): MenuImportApiError {
  const err = new Error(message) as MenuImportApiError
  err.status = status
  err.body = body
  if (body && typeof body === 'object' && 'missingCategoryIds' in body) {
    err.missingCategoryIds = (body as { missingCategoryIds: string[] }).missingCategoryIds
  }
  return err
}

function resolveBaseUrl(): string {
  const raw = import.meta.env.VITE_API_URL ?? ''
  return raw.endsWith('/') ? raw.slice(0, -1) : raw
}

/** Restaurant-scoped admin URL + bearer header from the active session. */
function scopedAdmin(suffix: string): { url: string; authHeader: Record<string, string> } {
  const auth = useAuthStore()
  if (!auth.activeRestaurantId) throw makeError('No active restaurant selected.', 400)
  return {
    url: `${resolveBaseUrl()}/api/restaurants/${auth.activeRestaurantId}${suffix}`,
    authHeader: auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {},
  }
}

async function scan(files: File[]): Promise<MenuScanResponse> {
  const form = new FormData()
  for (const file of files) form.append('photos', file, file.name)
  const { url, authHeader } = scopedAdmin('/admin/menu/scan')
  const res = await fetch(url, { method: 'POST', body: form, headers: authHeader })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`scan failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as MenuScanResponse
}

async function bulkCreate(payload: BulkCreateProductsRequest): Promise<AdminProduct[]> {
  const { url, authHeader } = scopedAdmin('/admin/menu/products/bulk')
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeader },
    body: JSON.stringify(payload),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`bulk-create failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as AdminProduct[]
}

export const menuImportApi = { scan, bulkCreate }

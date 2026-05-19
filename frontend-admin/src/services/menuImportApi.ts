import type {
  BulkCreateProductsRequest,
  MenuScanResponse,
} from '@shared/types/menu-import'
import type { AdminProduct } from '@shared/types/models'

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

async function scan(files: File[]): Promise<MenuScanResponse> {
  const BASE_URL = import.meta.env.VITE_API_URL ?? ''
  const form = new FormData()
  for (const file of files) form.append('photos', file, file.name)
  const res = await fetch(`${BASE_URL}/api/admin/menu/scan`, { method: 'POST', body: form })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`scan failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as MenuScanResponse
}

async function bulkCreate(payload: BulkCreateProductsRequest): Promise<AdminProduct[]> {
  const BASE_URL = import.meta.env.VITE_API_URL ?? ''
  const res = await fetch(`${BASE_URL}/api/admin/menu/products/bulk`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`bulk-create failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as AdminProduct[]
}

export const menuImportApi = { scan, bulkCreate }

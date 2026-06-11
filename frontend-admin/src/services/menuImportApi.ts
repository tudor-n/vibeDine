import type {
  BulkCreateProductsRequest,
  ExtractedMenuItem,
  FailedPage,
  MenuScanResponse,
} from '@shared/types/menu-import'
import type { AdminProduct } from '@shared/types/models'
import { useAuthStore } from '@/stores/useAuthStore'

export interface StartPdfImportResponse {
  jobId: string
}

export interface MenuImportJobResponse {
  id: string
  status: 'pending' | 'extracting' | 'complete' | 'failed' | 'cancelled'
  phase: 'parsing' | 'classifying' | 'extracting' | 'finalizing'
  totalPages: number
  processedPages: number
  failedPages: number
  originalFileName: string
  fileSizeBytes: number
  providerUsed: string
  result?: {
    items: ExtractedMenuItem[]
    failedPages: FailedPage[]
    model: string
    schemaVersion: number
  }
  errorMessage?: string
  createdAt: string
  updatedAt: string
  completedAt?: string
}

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

function asRecord(value: unknown): Record<string, unknown> | null {
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    return value as Record<string, unknown>
  }
  return null
}

function asOptionalString(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

function asOptionalNumber(value: unknown): number | null {
  return typeof value === 'number' ? value : null
}

function normalizeExtractedItem(value: unknown): ExtractedMenuItem {
  const raw = asRecord(value) ?? {}
  const item: ExtractedMenuItem = {
    name: typeof raw.name === 'string' ? raw.name : '',
    price: typeof raw.price === 'number' ? raw.price : Number(raw.price ?? 0),
    sourcePage: Number(raw.sourcePage ?? raw.source_page ?? 0),
    sourceIndex: Number(raw.sourceIndex ?? raw.source_index ?? 0),
    categoryHint: asOptionalString(raw.categoryHint ?? raw.category_hint),
  }
  if ('description' in raw) item.description = asOptionalString(raw.description)
  if ('confidence' in raw) item.confidence = asOptionalNumber(raw.confidence)
  return item
}

function normalizeFailedPage(value: unknown): FailedPage {
  const raw = asRecord(value) ?? {}
  return {
    sourcePage: Number(raw.sourcePage ?? raw.source_page ?? 0),
    reason: raw.reason as FailedPage['reason'],
  }
}

function normalizeScanResponse(value: unknown): MenuScanResponse | undefined {
  const raw = asRecord(value)
  if (!raw) return undefined
  const items = Array.isArray(raw.items) ? raw.items : []
  const failedPagesRaw = raw.failedPages ?? raw.failed_pages
  const failedPages = Array.isArray(failedPagesRaw) ? failedPagesRaw : []
  return {
    items: items.map(normalizeExtractedItem),
    failedPages: failedPages.map(normalizeFailedPage),
    model: typeof raw.model === 'string' ? raw.model : '',
    schemaVersion: Number(raw.schemaVersion ?? raw.schema_version ?? 1),
  }
}

function normalizeImportJob(value: MenuImportJobResponse): MenuImportJobResponse {
  return {
    ...value,
    result: normalizeScanResponse(value.result),
  }
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

async function startPdfImport(file: File): Promise<StartPdfImportResponse> {
  const form = new FormData()
  form.append('pdf', file, file.name)
  const { url, authHeader } = scopedAdmin('/admin/menu/imports/pdf')
  const res = await fetch(url, { method: 'POST', body: form, headers: authHeader })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`pdf import upload failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as StartPdfImportResponse
}

async function getImportJob(jobId: string): Promise<MenuImportJobResponse> {
  const { url, authHeader } = scopedAdmin(`/admin/menu/imports/${jobId}`)
  const res = await fetch(url, { method: 'GET', headers: authHeader })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`getImportJob failed: HTTP ${res.status}`, res.status, body)
  }
  return normalizeImportJob((await res.json()) as MenuImportJobResponse)
}

async function cancelImportJob(jobId: string): Promise<void> {
  const { url, authHeader } = scopedAdmin(`/admin/menu/imports/${jobId}`)
  const res = await fetch(url, { method: 'DELETE', headers: authHeader })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`cancelImportJob failed: HTTP ${res.status}`, res.status, body)
  }
}

export const menuImportApi = { scan, bulkCreate, startPdfImport, getImportJob, cancelImportJob }

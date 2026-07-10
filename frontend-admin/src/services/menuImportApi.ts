import type {
  BulkCreateHierarchyRequest,
  BulkCreateHierarchyResponse,
  BulkCreateProductsRequest,
  ExtractedMenuItem,
  FailedPage,
  ItemRef,
  MenuHierarchy,
  MenuScanResponse,
  OrganizedCategory,
  OrganizedSubcategory,
} from '@shared/types/menu-import'
import type { AdminProduct } from '@shared/types/models'
import { useAuthStore } from '@/stores/useAuthStore'
import { resolveMediaUrl } from '@/services/mediaUrl'

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
    hierarchy?: MenuHierarchy
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
  if ('imageUrl' in raw || 'image_url' in raw)
    item.imageUrl = asOptionalString(raw.imageUrl ?? raw.image_url)
  return item
}

function normalizeFailedPage(value: unknown): FailedPage {
  const raw = asRecord(value) ?? {}
  return {
    sourcePage: Number(raw.sourcePage ?? raw.source_page ?? 0),
    reason: raw.reason as FailedPage['reason'],
  }
}

function normalizeItemRef(value: unknown): ItemRef {
  const raw = asRecord(value) ?? {}
  return {
    sourcePage: Number(raw.sourcePage ?? raw.source_page ?? 0),
    sourceIndex: Number(raw.sourceIndex ?? raw.source_index ?? 0),
    confidence: Number(raw.confidence ?? 0),
  }
}

function normalizeOrganizedSubcategory(value: unknown): OrganizedSubcategory {
  const raw = asRecord(value) ?? {}
  const itemRefsRaw = raw.itemRefs ?? raw.item_refs
  const itemRefs = Array.isArray(itemRefsRaw) ? itemRefsRaw : []
  return {
    name: typeof raw.name === 'string' ? raw.name : '',
    sortOrder: Number(raw.sortOrder ?? raw.sort_order ?? 0),
    itemRefs: itemRefs.map(normalizeItemRef),
  }
}

function normalizeOrganizedCategory(value: unknown): OrganizedCategory {
  const raw = asRecord(value) ?? {}
  const subRaw = Array.isArray(raw.subcategories) ? raw.subcategories : []
  const directRaw = raw.directItemRefs ?? raw.direct_item_refs
  const directItemRefs = Array.isArray(directRaw) ? directRaw : []
  return {
    name: typeof raw.name === 'string' ? raw.name : '',
    sortOrder: Number(raw.sortOrder ?? raw.sort_order ?? 0),
    subcategories: subRaw.map(normalizeOrganizedSubcategory),
    directItemRefs: directItemRefs.map(normalizeItemRef),
  }
}

function normalizeHierarchy(value: unknown): MenuHierarchy | undefined {
  const raw = asRecord(value)
  if (!raw) return undefined
  const source = raw.source === 'ai' || raw.source === 'hint-fallback' ? raw.source : 'hint-fallback'
  const categoriesRaw = Array.isArray(raw.categories) ? raw.categories : []
  const uncategorizedRaw = raw.uncategorizedItemRefs ?? raw.uncategorized_item_refs
  const uncategorized = Array.isArray(uncategorizedRaw) ? uncategorizedRaw : []
  const modelNotes = (raw.modelNotes ?? raw.model_notes) as string | null | undefined
  return {
    source,
    categories: categoriesRaw.map(normalizeOrganizedCategory),
    uncategorizedItemRefs: uncategorized.map(normalizeItemRef),
    modelNotes: typeof modelNotes === 'string' ? modelNotes : null,
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
    hierarchy: normalizeHierarchy(raw.hierarchy),
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

/**
 * Resolve a stored item-photo URL to one the browser can load. Crops are stored
 * as root-relative paths (e.g. "/uploads/menu-items/..."); in split-origin dev
 * the admin SPA and API live on different hosts, so we prefix the API base. Empty
 * and already-absolute URLs are returned untouched.
 */
export function resolvePhotoUrl(url: string | null | undefined): string {
  return resolveMediaUrl(url)
}

/**
 * Authenticated fetch for the restaurant-scoped import endpoints. Attaches the
 * bearer token and, on a 401, performs the same one-shot refresh-and-retry as
 * `authedFetcher` in api-client — sharing the single-flight `tryRefresh`, so a
 * token that expires mid-import is transparently renewed instead of hard-failing.
 *
 * Supports FormData bodies: it never sets a JSON `Content-Type`, so the browser
 * keeps the multipart boundary intact. JSON callers pass their own Content-Type.
 * Returns the raw Response so each caller keeps its typed `makeError` handling.
 */
async function authedImportFetch(suffix: string, init: RequestInit = {}): Promise<Response> {
  const auth = useAuthStore()
  if (!auth.activeRestaurantId) throw makeError('No active restaurant selected.', 400)
  const url = `${resolveBaseUrl()}/api/restaurants/${auth.activeRestaurantId}${suffix}`

  const doFetch = () =>
    fetch(url, {
      ...init,
      headers: {
        ...(auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
        ...init.headers,
      },
    })

  let res = await doFetch()
  if (res.status === 401 && (await auth.tryRefresh())) {
    res = await doFetch()
  }
  return res
}

async function scan(
  files: File[],
  locale: string = 'ro-RO',
  extractItemPhotos: boolean = false,
): Promise<MenuScanResponse> {
  const form = new FormData()
  for (const file of files) form.append('photos', file, file.name)
  form.append('locale', locale)
  // Only append when opted in — keeps the off-path request byte-identical.
  if (extractItemPhotos) form.append('extractItemPhotos', 'true')
  const res = await authedImportFetch('/admin/menu/scan', { method: 'POST', body: form })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`scan failed: HTTP ${res.status}`, res.status, body)
  }
  const raw = (await res.json()) as unknown
  // The backend forwards `hierarchy` as the opaque snake_case blob from Python.
  // normalizeScanResponse handles both snake_case and camelCase variants.
  return normalizeScanResponse(raw) ?? {
    items: [],
    failedPages: [],
    model: '',
    schemaVersion: 1,
  }
}

async function bulkCreate(payload: BulkCreateProductsRequest): Promise<AdminProduct[]> {
  const res = await authedImportFetch('/admin/menu/products/bulk', {
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

async function bulkCreateHierarchy(payload: BulkCreateHierarchyRequest): Promise<BulkCreateHierarchyResponse> {
  const res = await authedImportFetch('/admin/menu/bulk-create-hierarchy', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`bulk-create-hierarchy failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as BulkCreateHierarchyResponse
}

async function startPdfImport(
  file: File,
  extractItemPhotos: boolean = false,
): Promise<StartPdfImportResponse> {
  const form = new FormData()
  form.append('pdf', file, file.name)
  if (extractItemPhotos) form.append('extractItemPhotos', 'true')
  const res = await authedImportFetch('/admin/menu/imports/pdf', { method: 'POST', body: form })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`pdf import upload failed: HTTP ${res.status}`, res.status, body)
  }
  return (await res.json()) as StartPdfImportResponse
}

async function getImportJob(jobId: string): Promise<MenuImportJobResponse> {
  const res = await authedImportFetch(`/admin/menu/imports/${jobId}`, { method: 'GET' })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`getImportJob failed: HTTP ${res.status}`, res.status, body)
  }
  return normalizeImportJob((await res.json()) as MenuImportJobResponse)
}

async function cancelImportJob(jobId: string): Promise<void> {
  const res = await authedImportFetch(`/admin/menu/imports/${jobId}`, { method: 'DELETE' })
  if (!res.ok) {
    const body = await res.json().catch(() => undefined)
    throw makeError(`cancelImportJob failed: HTTP ${res.status}`, res.status, body)
  }
}

export const menuImportApi = { scan, bulkCreate, bulkCreateHierarchy, startPdfImport, getImportJob, cancelImportJob }

import { defineStore } from 'pinia'
import { computed, reactive, ref } from 'vue'

import { menuImportApi, type MenuImportJobResponse } from '@/services/menuImportApi'
import { createStaffHubClient } from '@/services/signalRClient'
import type { StaffHubClient } from '@/services/signalRClient'
import { useToast } from '@/composables/useToast'
import { useAuthStore } from '@/stores/useAuthStore'
import { MENU_VALIDATION } from '@shared/types/menu-validation'
import type {
  BulkCreateCategoryItem,
  BulkCreateProductItem,
  BulkCreateProductsRequest,
  ExtractedMenuItem,
  FailedPage,
} from '@shared/types/menu-import'

export type ImportMode = 'photos' | 'pdf'

export interface PdfJobUiState {
  id: string
  status: 'uploading' | 'pending' | 'extracting' | 'complete' | 'failed' | 'cancelled'
  phase: 'parsing' | 'classifying' | 'extracting' | 'finalizing'
  fileName: string
  fileSizeBytes: number
  processedPages: number
  totalPages: number
  failedPages: number
  etaSeconds?: number
  errorMessage?: string
}

export type MappedCategory =
  | { kind: 'existing'; categoryId: string }
  | { kind: 'new'; tempId: string; name: string }

export interface ReviewItem {
  clientId: string
  name: string
  price: number
  sourcePage: number
  categoryHint: string | null
  resolvedCategory: MappedCategory
  skipped: boolean
}

function normaliseHint(hint: string): string {
  return hint.normalize('NFD').replace(/\p{Diacritic}/gu, '').trim().toLowerCase()
}

function makeId(): string {
  return crypto.randomUUID()
}

const PDF_IMPORT_POLL_INTERVAL_MS = 5_000

export const useMenuImportStore = defineStore('menuImport', () => {
  const toast = useToast()

  const step = ref<1 | 2 | 3>(1)
  const files = ref<File[]>([])
  const isScanning = ref(false)
  const scanError = ref<string | null>(null)
  const isImporting = ref(false)
  const importError = ref<string | null>(null)

  const extractedItems = ref<ExtractedMenuItem[]>([])
  const failedPages = ref<FailedPage[]>([])
  const detectedHints = ref<string[]>([])
  const categoryMapping = reactive<Record<string, MappedCategory>>({})
  const unassignedMapping = ref<MappedCategory | null>(null)
  const reviewItems = ref<ReviewItem[]>([])

  // ── PDF mode state ─────────────────────────────────────────────────────────
  const mode = ref<ImportMode>('photos')
  const pdfJob = ref<PdfJobUiState | null>(null)
  const pdfHub = ref<StaffHubClient | null>(null)
  const pdfPollTimer = ref<ReturnType<typeof setInterval> | null>(null)

  const canAdvanceFromStep1 = computed(() => files.value.length > 0 && !isScanning.value)
  const unassignedCount = computed(
    () => extractedItems.value.filter((it) => it.categoryHint == null).length,
  )
  const mappingComplete = computed(() => {
    const allHintsMapped = detectedHints.value.every((h) => h in categoryMapping)
    const unassignedOk = unassignedCount.value === 0 || unassignedMapping.value !== null
    return allHintsMapped && unassignedOk
  })

  function addFiles(incoming: File[]): { rejected: { file: File; reason: string }[] } {
    const rejected: { file: File; reason: string }[] = []
    for (const f of incoming) {
      if (files.value.length >= MENU_VALIDATION.IMPORT_MAX_FILES) {
        rejected.push({ file: f, reason: 'too_many' })
        continue
      }
      if (f.size > MENU_VALIDATION.IMPORT_MAX_FILE_BYTES) {
        rejected.push({ file: f, reason: 'too_large' })
        continue
      }
      if (!(MENU_VALIDATION.IMPORT_ALLOWED_MIME as readonly string[]).includes(f.type)) {
        rejected.push({ file: f, reason: 'bad_mime' })
        continue
      }
      if (files.value.some((existing) => existing.name === f.name && existing.size === f.size)) continue
      files.value.push(f)
    }
    return { rejected }
  }

  function removeFile(name: string) {
    files.value = files.value.filter((f) => f.name !== name)
  }

  async function scan() {
    isScanning.value = true
    scanError.value = null
    try {
      const result = await menuImportApi.scan(files.value)
      extractedItems.value = result.items
      failedPages.value = result.failedPages
      detectedHints.value = Array.from(
        new Set(result.items.map((it) => it.categoryHint).filter((h): h is string => !!h)),
      )
      // Reset mapping for current hint set, then seed keys for all detected hints.
      for (const k of Object.keys(categoryMapping)) delete categoryMapping[k]
      for (const hint of detectedHints.value) {
        categoryMapping[hint] = { kind: 'new', tempId: makeId(), name: hint }
      }
      unassignedMapping.value = null
      step.value = 2
    } catch (err) {
      scanError.value = err instanceof Error ? err.message : 'Scan failed.'
      throw err
    } finally {
      isScanning.value = false
    }
  }

  function setMapping(hint: string, mapping: MappedCategory) {
    categoryMapping[hint] = mapping
  }

  function setUnassignedMapping(mapping: MappedCategory) {
    unassignedMapping.value = mapping
  }

  function autoMatch(existingCategories: { id: string; name: string }[]) {
    // Overwrites any existing default mapping with a matched existing category;
    // hints with no match keep their previous mapping (typically the `new` seed from scan()).
    const byNormalised = new Map(existingCategories.map((c) => [normaliseHint(c.name), c.id]))
    for (const hint of detectedHints.value) {
      const match = byNormalised.get(normaliseHint(hint))
      if (match) {
        categoryMapping[hint] = { kind: 'existing', categoryId: match }
      } else if (!(hint in categoryMapping)) {
        categoryMapping[hint] = { kind: 'new', tempId: makeId(), name: hint }
      }
    }
  }

  function resolveCategoryForItem(item: ExtractedMenuItem): MappedCategory {
    if (item.categoryHint && categoryMapping[item.categoryHint]) {
      return categoryMapping[item.categoryHint]
    }
    if (unassignedMapping.value) return unassignedMapping.value
    // Should not be reachable if mappingComplete gates the transition; fall through to a placeholder.
    return { kind: 'new', tempId: 'unmapped', name: 'Unassigned' }
  }

  function advanceToReview() {
    reviewItems.value = extractedItems.value
      .slice()
      .sort((a, b) => a.sourcePage - b.sourcePage || a.sourceIndex - b.sourceIndex)
      .map((it) => ({
        clientId: makeId(),
        name: it.name,
        price: it.price,
        sourcePage: it.sourcePage,
        categoryHint: it.categoryHint ?? null,
        resolvedCategory: resolveCategoryForItem(it),
        skipped: false,
      }))
    step.value = 3
  }

  function updateItem(clientId: string, patch: Partial<Pick<ReviewItem, 'name' | 'price'>>) {
    const target = reviewItems.value.find((r) => r.clientId === clientId)
    if (!target) return
    if (patch.name !== undefined) target.name = patch.name
    if (patch.price !== undefined) target.price = patch.price
  }

  function toggleSkip(clientId: string) {
    const target = reviewItems.value.find((r) => r.clientId === clientId)
    if (target) target.skipped = !target.skipped
  }

  function validateRow(row: ReviewItem): { ok: boolean; field?: 'name' | 'price' } {
    const name = row.name.trim()
    if (name.length < MENU_VALIDATION.NAME_MIN || name.length > MENU_VALIDATION.NAME_MAX)
      return { ok: false, field: 'name' }
    if (!(row.price > 0) || row.price > MENU_VALIDATION.PRICE_MAX) return { ok: false, field: 'price' }
    return { ok: true }
  }

  const canImport = computed(() => {
    const active = reviewItems.value.filter((r) => !r.skipped)
    if (active.length === 0) return false
    return active.every((r) => validateRow(r).ok)
  })

  function buildBulkPayload(): BulkCreateProductsRequest {
    const newCategoriesByTempId = new Map<string, BulkCreateCategoryItem>()
    const items: BulkCreateProductItem[] = []
    for (const row of reviewItems.value) {
      if (row.skipped) continue
      let categoryId: string
      if (row.resolvedCategory.kind === 'existing') {
        categoryId = row.resolvedCategory.categoryId
      } else {
        categoryId = row.resolvedCategory.tempId
        if (!newCategoriesByTempId.has(row.resolvedCategory.tempId)) {
          newCategoriesByTempId.set(row.resolvedCategory.tempId, {
            tempId: row.resolvedCategory.tempId,
            name: row.resolvedCategory.name,
          })
        }
      }
      items.push({
        name: row.name.trim(),
        price: row.price,
        categoryId,
        isAvailable: true,
      })
    }
    return { items, newCategories: Array.from(newCategoriesByTempId.values()) }
  }

  async function importAll() {
    isImporting.value = true
    importError.value = null
    try {
      await menuImportApi.bulkCreate(buildBulkPayload())
    } catch (err) {
      importError.value = err instanceof Error ? err.message : 'Import failed.'
      throw err
    } finally {
      isImporting.value = false
    }
  }

  // ── PDF actions ────────────────────────────────────────────────────────────

  function makeUploadingPdfJob(file: File): PdfJobUiState {
    return {
      id: '',
      status: 'uploading',
      phase: 'parsing',
      fileName: file.name,
      fileSizeBytes: file.size,
      processedPages: 0,
      totalPages: 0,
      failedPages: 0,
      etaSeconds: undefined,
      errorMessage: undefined,
    }
  }

  function isActivePdfStatus(status: PdfJobUiState['status']): boolean {
    return status === 'uploading' || status === 'pending' || status === 'extracting'
  }

  function messageFromFailedPayload(payload: Record<string, unknown>, fallback: string): string {
    const errorMessage = payload['errorMessage']
    if (typeof errorMessage === 'string' && errorMessage.trim()) return errorMessage
    const error = payload['error']
    if (typeof error === 'string' && error.trim()) return error
    return fallback
  }

  function messageFromJob(job: MenuImportJobResponse, fallback: string): string {
    if (typeof job.errorMessage === 'string' && job.errorMessage.trim()) return job.errorMessage
    const error = (job as MenuImportJobResponse & { error?: unknown }).error
    if (typeof error === 'string' && error.trim()) return error
    return fallback
  }

  function applyPdfJobSnapshot(job: MenuImportJobResponse) {
    if (!pdfJob.value || pdfJob.value.id !== job.id) return
    pdfJob.value.status = job.status
    pdfJob.value.processedPages = job.processedPages
    pdfJob.value.totalPages = job.totalPages
    pdfJob.value.failedPages = job.failedPages
    pdfJob.value.phase = job.phase
    pdfJob.value.errorMessage = messageFromJob(job, pdfJob.value.errorMessage ?? '')
  }

  function hydrateCompletedPdfJob(job: MenuImportJobResponse): boolean {
    applyPdfJobSnapshot(job)
    if (!job.result) return false

    extractedItems.value = job.result.items
    failedPages.value = job.result.failedPages

    // Mirror the scan() logic: seed detectedHints & categoryMapping
    const hints = Array.from(
      new Set(
        job.result.items
          .map((it) => it.categoryHint)
          .filter((h): h is string => !!h),
      ),
    )
    detectedHints.value = hints
    for (const k of Object.keys(categoryMapping)) delete categoryMapping[k]
    for (const hint of hints) {
      categoryMapping[hint] = { kind: 'new', tempId: makeId(), name: hint }
    }
    unassignedMapping.value = null

    // Mirror scan(): if hints exist -> step 2 (map categories); else -> step 3 (review)
    if (hints.length > 0) {
      step.value = 2
    } else {
      reviewItems.value = extractedItems.value
        .slice()
        .sort((a, b) => a.sourcePage - b.sourcePage || a.sourceIndex - b.sourceIndex)
        .map((it) => ({
          clientId: makeId(),
          name: it.name,
          price: it.price,
          sourcePage: it.sourcePage,
          categoryHint: it.categoryHint ?? null,
          resolvedCategory: resolveCategoryForItem(it),
          skipped: false,
        }))
      step.value = 3
    }
    mode.value = 'photos'
    return true
  }

  async function failPdfJob(message: string) {
    if (pdfJob.value) {
      pdfJob.value.status = 'failed'
      pdfJob.value.errorMessage = message
    }
    importError.value = message
    toast.error(importError.value)
    await _disconnect()
  }

  function _stopPdfPolling() {
    if (pdfPollTimer.value === null) return
    clearInterval(pdfPollTimer.value)
    pdfPollTimer.value = null
  }

  function _startPdfPolling(jobId: string) {
    _stopPdfPolling()
    pdfPollTimer.value = setInterval(() => {
      void _pollPdfJob(jobId)
    }, PDF_IMPORT_POLL_INTERVAL_MS)
  }

  async function _pollPdfJob(jobId: string) {
    if (!pdfJob.value || pdfJob.value.id !== jobId || !isActivePdfStatus(pdfJob.value.status)) {
      _stopPdfPolling()
      return
    }
    try {
      const job = await menuImportApi.getImportJob(jobId)
      if (!pdfJob.value || pdfJob.value.id !== jobId) return
      applyPdfJobSnapshot(job)
      if (job.status === 'complete') {
        if (!hydrateCompletedPdfJob(job)) {
          await failPdfJob('Import PDF finalizat fără rezultat.')
          return
        }
        toast.success('Import PDF finalizat!')
        await _disconnect()
      } else if (job.status === 'failed') {
        await failPdfJob(messageFromJob(job, 'Import PDF eșuat.'))
      } else if (job.status === 'cancelled') {
        await _disconnect()
      }
    } catch {
      // Polling is a fallback; keep the active job visible and try again on the next tick.
    }
  }

  async function startPdfImport(file: File) {
    mode.value = 'pdf'
    importError.value = null
    _stopPdfPolling()
    pdfJob.value = makeUploadingPdfJob(file)
    const startingJob = pdfJob.value

    let jobId: string
    try {
      ;({ jobId } = await menuImportApi.startPdfImport(file))
    } catch (err) {
      importError.value = err instanceof Error ? err.message : 'Eroare la pornirea importului PDF.'
      if (pdfJob.value === startingJob) {
        pdfJob.value = null
        mode.value = 'photos'
      }
      throw err
    }

    if (pdfJob.value !== startingJob) return
    if (startingJob.status === 'cancelled') {
      try {
        await menuImportApi.cancelImportJob(jobId)
      } catch {
        // best-effort cleanup if the user cancelled while the upload was still starting
      }
      return
    }

    pdfJob.value = {
      ...pdfJob.value,
      id: jobId,
      status: 'pending',
    }

    const auth = useAuthStore()
    const restaurantId = auth.activeRestaurantId ?? ''
    _startPdfPolling(jobId)
    try {
      await _connectAndSubscribe(jobId, restaurantId)
    } catch (err) {
      importError.value = err instanceof Error ? err.message : 'Conexiunea live pentru importul PDF a eșuat.'
    }
  }

  async function cancelPdfImport() {
    if (!pdfJob.value) return
    if (pdfJob.value.id) {
      try {
        await menuImportApi.cancelImportJob(pdfJob.value.id)
      } catch {
        // best-effort cancel
      }
    }
    pdfJob.value.status = 'cancelled'
    await _disconnect()
    toast.info('Import PDF anulat.')
  }

  async function _connectAndSubscribe(jobId: string, restaurantId: string) {
    const hub = createStaffHubClient()
    pdfHub.value = hub

    hub.on<Record<string, unknown>>('MenuImportProgress', async (payload) => {
      if (!pdfJob.value || payload['jobId'] !== jobId) return
      if (payload['processedPages'] !== undefined)
        pdfJob.value.processedPages = payload['processedPages'] as number
      if (payload['totalPages'] !== undefined)
        pdfJob.value.totalPages = payload['totalPages'] as number
      if (payload['failedPages'] !== undefined)
        pdfJob.value.failedPages = payload['failedPages'] as number
      if (payload['phase'] !== undefined)
        pdfJob.value.phase = payload['phase'] as PdfJobUiState['phase']
      if (payload['etaSeconds'] !== undefined)
        pdfJob.value.etaSeconds = (payload['etaSeconds'] as number | null) ?? undefined
      if (payload['status'] === 'failed') {
        await failPdfJob(messageFromFailedPayload(payload, 'Import PDF eșuat.'))
      } else if (payload['status'] === 'cancelled') {
        pdfJob.value.status = 'cancelled'
        await _disconnect()
      } else if (payload['status'] === 'complete') {
        void _pollPdfJob(jobId)
      } else {
        pdfJob.value.status = 'extracting'
      }
    })

    hub.on<Record<string, unknown>>('MenuImportCompleted', async (payload) => {
      if (payload['jobId'] !== jobId) return
      try {
        const job = await menuImportApi.getImportJob(jobId)
        if (!hydrateCompletedPdfJob(job)) {
          await failPdfJob('Import PDF finalizat fără rezultat.')
          return
        }
        toast.success('Import PDF finalizat!')
      } catch (err) {
        importError.value = err instanceof Error ? err.message : 'Eroare la finalizarea importului.'
        toast.error(importError.value)
      }
      await _disconnect()
    })

    hub.on<Record<string, unknown>>('MenuImportFailed', async (payload) => {
      if (payload['jobId'] !== jobId) return
      await failPdfJob(messageFromFailedPayload(payload, 'Import PDF eșuat.'))
    })

    hub.onReconnected(async () => {
      if (!pdfJob.value || pdfJob.value.id !== jobId || !isActivePdfStatus(pdfJob.value.status)) return
      await hub.joinAdminGroup(restaurantId)
    })

    await hub.start()
    await hub.joinAdminGroup(restaurantId)
  }

  async function _disconnect() {
    _stopPdfPolling()
    if (!pdfHub.value) return
    try {
      // activeRestaurantId may already be gone if store was reset
      const auth = useAuthStore()
      if (auth.activeRestaurantId) {
        await pdfHub.value.leaveAdminGroup(auth.activeRestaurantId)
      }
    } catch {
      // ignore
    }
    try {
      await pdfHub.value.stop()
    } catch {
      // ignore
    }
    pdfHub.value = null
  }

  function $reset() {
    step.value = 1
    files.value = []
    isScanning.value = false
    scanError.value = null
    isImporting.value = false
    importError.value = null
    extractedItems.value = []
    failedPages.value = []
    detectedHints.value = []
    for (const k of Object.keys(categoryMapping)) delete categoryMapping[k]
    unassignedMapping.value = null
    reviewItems.value = []
    mode.value = 'photos'
    pdfJob.value = null
    void _disconnect()
  }

  return {
    // state
    step,
    files,
    isScanning,
    scanError,
    isImporting,
    importError,
    extractedItems,
    failedPages,
    detectedHints,
    categoryMapping,
    unassignedMapping,
    reviewItems,
    // PDF state
    mode,
    pdfJob,
    // getters
    canAdvanceFromStep1,
    unassignedCount,
    mappingComplete,
    canImport,
    // actions
    addFiles,
    removeFile,
    scan,
    setMapping,
    setUnassignedMapping,
    autoMatch,
    advanceToReview,
    updateItem,
    toggleSkip,
    validateRow,
    buildBulkPayload,
    importAll,
    // PDF actions
    startPdfImport,
    cancelPdfImport,
    $reset,
  }
})

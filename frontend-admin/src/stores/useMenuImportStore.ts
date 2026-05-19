import { defineStore } from 'pinia'
import { computed, reactive, ref } from 'vue'

import { menuImportApi } from '@/services/menuImportApi'
import { MENU_VALIDATION } from '@shared/types/menu-validation'
import type {
  BulkCreateCategoryItem,
  BulkCreateProductItem,
  BulkCreateProductsRequest,
  ExtractedMenuItem,
  FailedPage,
} from '@shared/types/menu-import'

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

export const useMenuImportStore = defineStore('menuImport', () => {
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
    $reset,
  }
})

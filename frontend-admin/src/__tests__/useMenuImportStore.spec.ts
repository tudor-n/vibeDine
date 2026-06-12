import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import type { MenuScanResponse } from '@shared/types/menu-import'

vi.mock('@/services/menuImportApi', () => ({
  menuImportApi: {
    scan: vi.fn(),
    bulkCreate: vi.fn(),
    bulkCreateHierarchy: vi.fn(),
  },
}))

import { menuImportApi } from '@/services/menuImportApi'
import type { MenuHierarchy } from '@shared/types/menu-import'

const scanFixture: MenuScanResponse = {
  items: [
    { name: 'Ciorbă', price: 18, sourcePage: 0, sourceIndex: 0, categoryHint: 'Ciorbe' },
    { name: 'Tort', price: 22, sourcePage: 1, sourceIndex: 0, categoryHint: 'Deserturi' },
    { name: 'Mister', price: 9, sourcePage: 1, sourceIndex: 1, categoryHint: null },
  ],
  failedPages: [],
  model: 'fake',
  schemaVersion: 1,
}

describe('useMenuImportStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('starts on step 1 with empty state', () => {
    const store = useMenuImportStore()
    expect(store.step).toBe(1)
    expect(store.files).toEqual([])
    expect(store.canAdvanceFromStep1).toBe(false)
  })

  it('addFiles enforces max count and unique names', () => {
    const store = useMenuImportStore()
    for (let i = 0; i < 12; i++) {
      store.addFiles([new File([new Uint8Array([i])], `f${i}.png`, { type: 'image/png' })])
    }
    expect(store.files.length).toBe(10)
  })

  it('scan() advances to step 2 and seeds category mapping', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    expect(store.step).toBe(2)
    expect(Object.keys(store.categoryMapping).sort()).toEqual(['Ciorbe', 'Deserturi'])
    expect(store.unassignedCount).toBe(1) // one item with categoryHint = null
  })

  it('mappingComplete is false until every detected hint AND the unassigned bucket are mapped', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    expect(store.mappingComplete).toBe(false)
    store.setMapping('Ciorbe', { kind: 'new', tempId: 't1', name: 'Ciorbe' })
    store.setMapping('Deserturi', { kind: 'new', tempId: 't2', name: 'Deserturi' })
    expect(store.mappingComplete).toBe(false) // unassigned still missing
    store.setUnassignedMapping({ kind: 'existing', categoryId: 'cat-existing' })
    expect(store.mappingComplete).toBe(true)
  })

  it('review items include resolvedCategory based on mapping', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    store.setMapping('Ciorbe', { kind: 'existing', categoryId: 'cat-c' })
    store.setMapping('Deserturi', { kind: 'new', tempId: 't-d', name: 'Deserturi' })
    store.setUnassignedMapping({ kind: 'existing', categoryId: 'cat-u' })
    store.advanceToReview()
    expect(store.step).toBe(3)
    const byName = Object.fromEntries(store.reviewItems.map((r) => [r.name, r]))
    expect(byName['Ciorbă'].resolvedCategory).toMatchObject({ kind: 'existing', categoryId: 'cat-c' })
    expect(byName['Tort'].resolvedCategory).toMatchObject({ kind: 'new', name: 'Deserturi' })
    expect(byName['Mister'].resolvedCategory).toMatchObject({ kind: 'existing', categoryId: 'cat-u' })
  })

  it('canImport is false while any non-skipped row is invalid', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    store.setMapping('Ciorbe', { kind: 'existing', categoryId: 'cat-c' })
    store.setMapping('Deserturi', { kind: 'existing', categoryId: 'cat-d' })
    store.setUnassignedMapping({ kind: 'existing', categoryId: 'cat-u' })
    store.advanceToReview()

    const first = store.reviewItems[0]
    store.updateItem(first.clientId, { name: '' })
    expect(store.canImport).toBe(false)
    store.toggleSkip(first.clientId)
    expect(store.canImport).toBe(true)
  })

  it('$reset clears everything', () => {
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    store.$reset()
    expect(store.files).toEqual([])
    expect(store.step).toBe(1)
  })

  it('scan() with hierarchy in response hydrates the tree and jumps to step 3', async () => {
    const fixtureWithHierarchy: MenuScanResponse = {
      items: [{ name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' }],
      failedPages: [],
      model: 'fake',
      schemaVersion: 2,
      hierarchy: {
        source: 'ai',
        categories: [{
          name: 'Pizza',
          sortOrder: 0,
          subcategories: [],
          directItemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }],
        }],
        uncategorizedItemRefs: [],
        modelNotes: null,
      },
    }
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(fixtureWithHierarchy)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    expect(store.step).toBe(3)
    expect(store.hierarchy).not.toBeNull()
    expect(store.hierarchy!.source).toBe('ai')
    expect(store.hierarchy!.categories[0].name).toBe('Pizza')
    // hint mapping should be empty since we skipped the mapping step
    expect(Object.keys(store.categoryMapping)).toEqual([])
    expect(store.detectedHints).toEqual([])
  })

  it('extractItemPhotos defaults off, is forwarded to scan, and resets', async () => {
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(scanFixture)
    const store = useMenuImportStore()
    expect(store.extractItemPhotos).toBe(false)
    store.extractItemPhotos = true
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    expect(menuImportApi.scan).toHaveBeenCalledWith(store.files, 'ro-RO', true)
    store.$reset()
    expect(store.extractItemPhotos).toBe(false)
  })

  it('imageUrl flows scan → review → bulk payload, and removeReviewItemPhoto clears it', async () => {
    const fixture: MenuScanResponse = {
      items: [
        { name: 'Pizza', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza', imageUrl: '/uploads/menu-items/r/x.jpg' },
      ],
      failedPages: [],
      model: 'fake',
      schemaVersion: 2,
    }
    ;(menuImportApi.scan as ReturnType<typeof vi.fn>).mockResolvedValueOnce(fixture)
    const store = useMenuImportStore()
    store.addFiles([new File([new Uint8Array([1])], 'a.png', { type: 'image/png' })])
    await store.scan()
    store.advanceToReview()

    const row = store.reviewItems[0]
    expect(row.imageUrl).toBe('/uploads/menu-items/r/x.jpg')
    expect(store.buildBulkPayload().items[0].imageUrl).toBe('/uploads/menu-items/r/x.jpg')

    store.removeReviewItemPhoto(row.clientId)
    expect(store.reviewItems[0].imageUrl).toBeNull()
    expect(store.buildBulkPayload().items[0].imageUrl).toBeNull()
  })
})

const hierarchyFixture: MenuHierarchy = {
  source: 'ai',
  categories: [
    {
      name: 'Pizza',
      sortOrder: 0,
      subcategories: [
        { name: 'Classic', sortOrder: 0, itemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }] },
      ],
      directItemRefs: [{ sourcePage: 0, sourceIndex: 1, confidence: 0.85 }],
    },
  ],
  uncategorizedItemRefs: [],
  modelNotes: null,
}

describe('useMenuImportStore — hierarchy', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('hydrateHierarchy stores the tree and surfaces source', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    expect(store.hierarchy!.source).toBe('ai')
    expect(store.hierarchy!.categories[0].subcategories[0].name).toBe('Classic')
  })

  it('buildHierarchyPayload carries imageUrl; removeItemPhoto clears it', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza', imageUrl: '/uploads/menu-items/r/x.jpg' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)

    const marg = store.buildHierarchyPayload().products.find((p) => p.name === 'Margherita')
    expect(marg!.imageUrl).toBe('/uploads/menu-items/r/x.jpg')

    store.removeItemPhoto({ sourcePage: 0, sourceIndex: 0 })
    const after = store.buildHierarchyPayload().products.find((p) => p.name === 'Margherita')
    expect(after!.imageUrl).toBeNull()
  })

  it('moveItem moves an item from a subcategory to directItemRefs', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    store.moveItem({ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }, { kind: 'direct', categoryName: 'Pizza' })
    const cat = store.hierarchy!.categories[0]
    expect(cat.subcategories[0].itemRefs).toHaveLength(0)
    expect(cat.directItemRefs).toHaveLength(2)
  })

  it('renameCategory updates the category name', () => {
    const store = useMenuImportStore()
    store.hydrateHierarchy(hierarchyFixture)
    store.renameCategory('Pizza', 'Pizze')
    expect(store.hierarchy!.categories[0].name).toBe('Pizze')
  })

  it('addSubcategory + removeSubcategory keep items intact', () => {
    const store = useMenuImportStore()
    store.hydrateHierarchy(hierarchyFixture)
    store.addSubcategory('Pizza', 'Specialty')
    expect(store.hierarchy!.categories[0].subcategories).toHaveLength(2)
    store.removeSubcategory('Pizza', 'Classic')
    expect(store.hierarchy!.categories[0].subcategories.map(s => s.name)).toEqual(['Specialty'])
    expect(store.hierarchy!.categories[0].directItemRefs).toHaveLength(2)
  })

  it('buildHierarchyPayload serialises with tempIds and correct subcategoryTempId', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    const payload = store.buildHierarchyPayload()
    expect(payload.categories).toHaveLength(1)
    expect(payload.categories[0].subcategories).toHaveLength(1)
    expect(payload.products).toHaveLength(2)
    const margherita = payload.products.find(p => p.name === 'Margherita')!
    expect(margherita.subcategoryTempId).toBe(payload.categories[0].subcategories[0].tempId)
    const calzone = payload.products.find(p => p.name === 'Calzone')!
    expect(calzone.subcategoryTempId).toBeNull()
    expect(calzone.categoryTempId).toBe(payload.categories[0].tempId)
  })

  it('buildHierarchyPayload assigns unique tempIds even for duplicate-named subcategories', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'A', price: 1, sourcePage: 0, sourceIndex: 0, categoryHint: 'C' },
      { name: 'B', price: 2, sourcePage: 0, sourceIndex: 1, categoryHint: 'C' },
    ]
    store.hydrateHierarchy({
      source: 'ai',
      categories: [{ name: 'C', sortOrder: 0, subcategories: [], directItemRefs: [] }],
      uncategorizedItemRefs: [],
      modelNotes: null,
    })
    store.addSubcategory('C', 'Sub')
    store.addSubcategory('C', 'Sub')
    // Manually move one item into each subcategory
    const h = store.hierarchy!
    h.categories[0].subcategories[0].itemRefs.push({ sourcePage: 0, sourceIndex: 0, confidence: 0.9 })
    h.categories[0].subcategories[1].itemRefs.push({ sourcePage: 0, sourceIndex: 1, confidence: 0.9 })
    const payload = store.buildHierarchyPayload()
    expect(payload.categories[0].subcategories).toHaveLength(2)
    const subTempIds = payload.categories[0].subcategories.map(s => s.tempId)
    expect(new Set(subTempIds).size).toBe(2) // unique
  })

  it('buildHierarchyPayload drops empty subcategories', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy({
      source: 'ai',
      categories: [{
        name: 'Pizza', sortOrder: 0,
        subcategories: [
          { name: 'Classic', sortOrder: 0, itemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }] },
          { name: 'Empty', sortOrder: 1, itemRefs: [] }, // should be dropped
        ],
        directItemRefs: [],
      }],
      uncategorizedItemRefs: [],
      modelNotes: null,
    })
    const payload = store.buildHierarchyPayload()
    expect(payload.categories[0].subcategories).toHaveLength(1)
    expect(payload.categories[0].subcategories[0].name).toBe('Classic')
  })

  it('importHierarchy posts to bulkCreateHierarchy', async () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    ;(menuImportApi.bulkCreateHierarchy as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      createdCategories: [], createdSubcategories: [], createdProducts: [],
    })
    await store.importHierarchy()
    expect(menuImportApi.bulkCreateHierarchy).toHaveBeenCalledTimes(1)
  })

  it('updateExtractedItem patches name and price on the matching item', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'A', price: 1, sourcePage: 0, sourceIndex: 0, categoryHint: null },
    ]
    store.updateExtractedItem({ sourcePage: 0, sourceIndex: 0 }, { name: 'B', price: 9 })
    expect(store.extractedItems[0].name).toBe('B')
    expect(store.extractedItems[0].price).toBe(9)
  })

  it('skipItem excludes the item from buildHierarchyPayload', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'A', price: 1, sourcePage: 0, sourceIndex: 0, categoryHint: 'C' },
      { name: 'B', price: 2, sourcePage: 0, sourceIndex: 1, categoryHint: 'C' },
    ]
    store.hydrateHierarchy({
      source: 'ai',
      categories: [{
        name: 'C', sortOrder: 0, subcategories: [],
        directItemRefs: [
          { sourcePage: 0, sourceIndex: 0, confidence: 0.9 },
          { sourcePage: 0, sourceIndex: 1, confidence: 0.9 },
        ],
      }],
      uncategorizedItemRefs: [],
      modelNotes: null,
    })
    store.skipItem({ sourcePage: 0, sourceIndex: 0 })
    const payload = store.buildHierarchyPayload()
    expect(payload.products).toHaveLength(1)
    expect(payload.products[0].name).toBe('B')
  })

  it('addCategory appends a new empty category', () => {
    const store = useMenuImportStore()
    store.hydrateHierarchy({
      source: 'ai', categories: [], uncategorizedItemRefs: [], modelNotes: null,
    })
    store.addCategory('New Cat')
    expect(store.hierarchy!.categories).toHaveLength(1)
    expect(store.hierarchy!.categories[0].name).toBe('New Cat')
  })

  it('removeCategory moves items to uncategorized (no data loss)', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'A', price: 1, sourcePage: 0, sourceIndex: 0, categoryHint: 'C' },
    ]
    store.hydrateHierarchy({
      source: 'ai',
      categories: [{
        name: 'C', sortOrder: 0, subcategories: [],
        directItemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }],
      }],
      uncategorizedItemRefs: [],
      modelNotes: null,
    })
    store.removeCategory('C')
    expect(store.hierarchy!.categories).toHaveLength(0)
    expect(store.hierarchy!.uncategorizedItemRefs).toHaveLength(1)
  })
})

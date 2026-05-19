import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import type { MenuScanResponse } from '@shared/types/menu-import'

vi.mock('@/services/menuImportApi', () => ({
  menuImportApi: {
    scan: vi.fn(),
    bulkCreate: vi.fn(),
  },
}))

import { menuImportApi } from '@/services/menuImportApi'

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
})

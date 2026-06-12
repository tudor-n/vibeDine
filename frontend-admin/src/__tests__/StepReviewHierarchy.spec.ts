import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

import StepReviewHierarchy from '@/components/menu-import/StepReviewHierarchy.vue'
import { useMenuImportStore } from '@/stores/useMenuImportStore'

vi.mock('@/services/menuImportApi', () => ({
  menuImportApi: { scan: vi.fn(), bulkCreate: vi.fn(), bulkCreateHierarchy: vi.fn() },
}))

beforeEach(() => setActivePinia(createPinia()))

describe('StepReviewHierarchy', () => {
  it('renders categories, subcategories, and direct items', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy({
      source: 'ai',
      categories: [{
        name: 'Pizza', sortOrder: 0,
        subcategories: [{ name: 'Classic', sortOrder: 0, itemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }] }],
        directItemRefs: [{ sourcePage: 0, sourceIndex: 1, confidence: 0.85 }],
      }],
      uncategorizedItemRefs: [],
      modelNotes: null,
    })
    const wrapper = mount(StepReviewHierarchy)
    expect(wrapper.text()).toContain('Pizza')
    expect(wrapper.text()).toContain('Classic')
    expect(wrapper.text()).toContain('Margherita')
    expect(wrapper.text()).toContain('Calzone')
  })

  it('shows amber dot only for items with confidence < 0.7', () => {
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
          { sourcePage: 0, sourceIndex: 1, confidence: 0.4 },
        ],
      }],
      uncategorizedItemRefs: [],
      modelNotes: null,
    })
    const wrapper = mount(StepReviewHierarchy)
    expect(wrapper.findAll('[data-test="confidence-dot"]')).toHaveLength(1)
  })

  it('shows hint-fallback banner only when source is hint-fallback', () => {
    const store = useMenuImportStore()
    store.hydrateHierarchy({
      source: 'hint-fallback', categories: [], uncategorizedItemRefs: [], modelNotes: null,
    })
    const wrapper = mount(StepReviewHierarchy)
    expect(wrapper.find('[data-test="hint-fallback-banner"]').exists()).toBe(true)
  })

  it('moves an uncategorized item to a category via dropdown', async () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'MysteryItem', price: 12, sourcePage: 0, sourceIndex: 0, categoryHint: null },
    ]
    store.hydrateHierarchy({
      source: 'ai',
      categories: [{
        name: 'Pizza', sortOrder: 0,
        subcategories: [],
        directItemRefs: [],
      }],
      uncategorizedItemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.3 }],
      modelNotes: null,
    })
    const wrapper = mount(StepReviewHierarchy)
    const select = wrapper.get('[data-test="uncategorized-move-0-0"]')
    await select.setValue('direct:Pizza')
    expect(store.hierarchy!.uncategorizedItemRefs).toHaveLength(0)
    expect(store.hierarchy!.categories[0].directItemRefs).toHaveLength(1)
  })

  it('skip removes item from main list and surfaces it in the skipped section', async () => {
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
    const wrapper = mount(StepReviewHierarchy)
    await wrapper.get('[data-test="skip-0-0"]').trigger('click')
    expect(store.isSkipped({ sourcePage: 0, sourceIndex: 0 })).toBe(true)
  })
})

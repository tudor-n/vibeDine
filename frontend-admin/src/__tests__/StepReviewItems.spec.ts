import { createPinia, setActivePinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'

import StepReviewItems from '@/components/menu-import/StepReviewItems.vue'
import { useMenuImportStore } from '@/stores/useMenuImportStore'

function seed(store: ReturnType<typeof useMenuImportStore>) {
  store.$patch({
    step: 3,
    reviewItems: [
      {
        clientId: 'a',
        name: 'Ciorbă',
        price: 18,
        sourcePage: 0,
        categoryHint: 'Ciorbe',
        resolvedCategory: { kind: 'existing', categoryId: 'cat-c' },
        skipped: false,
      },
    ],
  })
}

describe('StepReviewItems', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('renders one editable row per item', () => {
    const store = useMenuImportStore()
    seed(store)
    const wrapper = mount(StepReviewItems)
    expect(wrapper.findAll('[data-testid="review-row"]')).toHaveLength(1)
  })

  it('shows inline error when name is cleared', async () => {
    const store = useMenuImportStore()
    seed(store)
    const wrapper = mount(StepReviewItems)
    const nameInput = wrapper.find<HTMLInputElement>('[data-testid="row-name"]')
    await nameInput.setValue('')
    expect(wrapper.find('[data-testid="row-name-error"]').exists()).toBe(true)
    expect(wrapper.find<HTMLButtonElement>('[data-testid="import-btn"]').element.disabled).toBe(true)
  })

  it('skip toggles the row out of the import', async () => {
    const store = useMenuImportStore()
    seed(store)
    const wrapper = mount(StepReviewItems)
    await wrapper.find('[data-testid="row-name"]').setValue('')
    await wrapper.find('[data-testid="row-skip"]').trigger('click')
    expect(wrapper.find<HTMLButtonElement>('[data-testid="import-btn"]').element.disabled).toBe(true)
    // No active rows now — button stays disabled because canImport requires ≥ 1 active row.
  })
})

import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import MenuManagementView from '@/views/MenuManagementView.vue'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'

const baseUrl = 'http://api.test'
import.meta.env.VITE_API_URL = baseUrl

describe('MenuManagementView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('resolves stored root-relative category and product image URLs against the API origin', () => {
    const store = useMenuManagementStore()
    vi.spyOn(store, 'fetchMenu').mockResolvedValue(undefined)
    store.categories = [
      {
        id: 'cat-1',
        name: 'Pizza',
        sortOrder: 0,
        imageUrl: '/uploads/menu-items/r/category.jpg',
        products: [
          {
            id: 'prod-1',
            categoryId: 'cat-1',
            categoryName: 'Pizza',
            name: 'Margherita',
            description: '',
            price: 28,
            imageUrl: '/uploads/menu-items/r/product.jpg',
            isAvailable: true,
            sortOrder: 0,
          },
        ],
      },
    ]

    const wrapper = mount(MenuManagementView, {
      global: {
        stubs: {
          RouterLink: true,
        },
      },
    })

    expect(wrapper.find('.cat-thumb').attributes('style')).toContain(
      `${baseUrl}/uploads/menu-items/r/category.jpg`,
    )
    expect(wrapper.find('.prod-thumb').attributes('style')).toContain(
      `${baseUrl}/uploads/menu-items/r/product.jpg`,
    )
  })
})

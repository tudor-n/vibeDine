<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { apiClient } from '@/services/api-client'
import { useCartStore } from '@/stores/useCartStore'
import type { Category, Product } from '@shared/types/models'

const cartStore = useCartStore()

const categories = ref<Category[]>([])
const products = ref<Product[]>([])
const isLoading = ref(true)
const error = ref<string | null>(null)

const selectedCategoryId = ref<string | null>(null)

const getProductsByCategory = (categoryId: string) => {
  return products.value.filter(p => p.category_id === categoryId)
}


const filteredCategories = computed(() => {
  if (!selectedCategoryId.value) {
    return categories.value
  }
  return categories.value.filter(c => c.id === selectedCategoryId.value)
})

onMounted(async () => {
  try {
    isLoading.value = true

    const response = await apiClient.getMenu()

    categories.value = response.categories.sort((a, b) =>
      (a.sort_order || 0) - (b.sort_order || 0)
    )
    products.value = response.products
  } catch (err) {
    if (err instanceof Error) {
      error.value = err.message
    } else {
      error.value = 'Eroare necunoscută la încărcarea meniului.'
    }
  } finally {
    isLoading.value = false
  }
})
</script>

<template>
  <div class="menu-list">
    <div v-if="isLoading" class="p-4 text-center text-gray-500">
      <span class="animate-pulse">Se încarcă meniul...</span>
    </div>

    <div v-else-if="error" class="p-4 text-center text-red-500">
      {{ error }}
    </div>

    <div v-else>
      <div class="flex overflow-x-auto gap-3 px-4 mb-6 pb-2 snap-x hide-scrollbar">
        <button
          @click="selectedCategoryId = null"
          :class="[
            'whitespace-nowrap px-5 py-2 rounded-full font-bold transition-all shadow-sm snap-start',
            selectedCategoryId === null
              ? 'bg-brand text-white scale-105'
              : 'bg-white border border-gray-200 text-gray-600 hover:bg-gray-50'
          ]"
        >
          Toate
        </button>

        <button
          v-for="category in categories"
          :key="category.id"
          @click="selectedCategoryId = category.id"
          :class="[
            'whitespace-nowrap px-5 py-2 rounded-full font-bold transition-all shadow-sm snap-start',
            selectedCategoryId === category.id
              ? 'bg-brand text-white scale-105'
              : 'bg-white border border-gray-200 text-gray-600 hover:bg-gray-50'
          ]"
        >
          {{ category.name }}
        </button>
      </div>

      <section v-for="category in filteredCategories" :key="category.id" class="mb-8 transition-all">
        <h2 class="text-2xl font-bold mb-4 px-4">{{ category.name }}</h2>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4 px-4">
          <div
            v-for="product in getProductsByCategory(category.id)"
            :key="product.id"
            class="bg-white border border-gray-100 rounded-xl p-4 flex justify-between items-center shadow-sm hover:shadow-md transition-shadow"
          >
            <div class="pr-4">
              <h3 class="font-bold text-lg text-gray-800">{{ product.name }}</h3>
              <p class="text-gray-500 text-sm mb-2 line-clamp-2">{{ product.description }}</p>
              <span class="font-black text-brand">{{ product.price }} RON</span>
            </div>

            <button
              @click="cartStore.addItem(product)"
              :disabled="!product.is_available"
              class="shrink-0 bg-brand hover:bg-brand-light text-white font-bold px-5 py-2 rounded-lg transition-transform active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed disabled:active:scale-100 shadow-sm"
            >
              {{ product.is_available ? 'Adaugă' : 'Epuizat' }}
            </button>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>

.hide-scrollbar::-webkit-scrollbar {
  display: none;
}
.hide-scrollbar {
  -ms-overflow-style: none;
  scrollbar-width: none;
}
</style>

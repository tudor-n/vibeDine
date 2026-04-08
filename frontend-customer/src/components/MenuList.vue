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
  return products.value.filter(p => p.categoryId === categoryId)
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

    categories.value = response.categories.map((c: any) => ({
      id: c.id,
      name: c.name,
      sortOrder: c.sortOrder || c.sort_order
    })).sort((a, b) => (a.sortOrder || 0) - (b.sortOrder || 0))

    const allProducts: Product[] = []

    response.categories.forEach((cat: any) => {
      if (cat.products && Array.isArray(cat.products)) {
        cat.products.forEach((p: any) => {
          allProducts.push({
            id: p.id,
            categoryId: cat.id,
            name: p.name,
            description: p.description,
            price: p.price,
            imageUrl: p.imageUrl || p.image_url || '',
            isAvailable: p.isAvailable !== undefined ? p.isAvailable : (p.is_available !== undefined ? p.is_available : true)
          })
        })
      }
    })

    products.value = allProducts

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
      <div v-if="selectedCategoryId === null" class="animate-fade-in px-4">
        <h2 class="text-2xl font-black text-gray-800 mb-6 text-center">Ce ai poftă să mănânci?</h2>

        <div class="grid grid-cols-2 gap-4">
          <button
            v-for="category in categories"
            :key="category.id"
            @click="selectedCategoryId = category.id"
            class="relative overflow-hidden bg-white rounded-2xl p-6 shadow-sm border border-gray-100 flex flex-col items-center justify-center cursor-pointer hover:shadow-md active:scale-95 transition-all duration-200 group h-32"
          >
            <div class="absolute -right-4 -top-4 w-16 h-16 bg-brand opacity-10 rounded-full group-hover:scale-150 transition-transform duration-500"></div>

            <span class="text-lg font-bold text-gray-800 text-center z-10">{{ category.name }}</span>
            <span class="text-xs text-brand font-semibold mt-2 opacity-0 group-hover:opacity-100 transition-opacity z-10">Vezi meniul →</span>
          </button>
        </div>
      </div>

      <div v-else class="animate-fade-in">
        <div class="flex overflow-x-auto gap-3 px-4 mb-6 pb-2 snap-x hide-scrollbar items-center">
          <button
            @click="selectedCategoryId = null"
            class="whitespace-nowrap flex items-center gap-1 px-5 py-2 rounded-full font-bold transition-all shadow-sm snap-start bg-white border border-gray-200 text-gray-600 hover:bg-gray-50"
          >
            <span class="text-lg leading-none">&larr;</span> Înapoi
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
                :disabled="!product.isAvailable"
                class="shrink-0 bg-brand hover:bg-brand-light text-white font-bold px-5 py-2 rounded-lg transition-transform active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed disabled:active:scale-100 shadow-sm"
              >
                {{ product.isAvailable ? 'Adaugă' : 'Epuizat' }}
              </button>
            </div>
          </div>
        </section>
      </div>

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

.animate-fade-in {
  animation: fadeIn 0.3s ease-out forwards;
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(10px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>

<script setup lang="ts">
import { useRecommendations } from '@/composables/useRecommendations'
import { useCartStore } from '@/stores/useCartStore'
import type { Product, Recommendation } from '@shared/types/models'

const { pairings, upsells, isLoading } = useRecommendations()
const cartStore = useCartStore()


const addToCart = (rec: Recommendation) => {

  const productToAdd: Product = {
    id: rec.menu_item.id,
    category_id: '',
    name: rec.menu_item.name,
    description: rec.reason,
    price: rec.menu_item.price,
    image_url: rec.menu_item.image_url || '',
    is_available: true
  }
  cartStore.addItem(productToAdd)
}
</script>

<template>
  <div v-if="pairings.length > 0 || upsells.length > 0" class="mt-8 mb-4 border-t-2 border-dashed border-gray-200 pt-6">
    <h3 class="text-xl font-bold text-gray-800 mb-2 flex items-center gap-2">
      ✨ Completări perfecte
      <span v-if="isLoading" class="text-sm font-normal text-gray-400 animate-pulse">Se actualizează...</span>
    </h3>

    <p class="text-sm text-gray-500 mb-4">Bazat pe ce ai adăugat deja în coș</p>

    <div class="flex overflow-x-auto gap-4 pb-4 snap-x">
      <div
        v-for="item in [...pairings, ...upsells]"
        :key="item.id"
        class="min-w-[200px] bg-white border border-brand-light/30 rounded-xl p-4 shadow-sm snap-center flex flex-col justify-between"
      >
        <div>
          <h4 class="font-bold text-gray-800">{{ item.menu_item.name }}</h4>
          <p class="text-xs text-gray-600 mt-1 italic">"{{ item.reason }}"</p>
        </div>

        <div class="mt-4 flex items-center justify-between">
          <span class="font-bold text-brand">{{ item.menu_item.price }} RON</span>
          <button
            @click="addToCart(item)"
            class="text-sm bg-brand/10 text-brand font-bold py-1 px-3 rounded-full hover:bg-brand hover:text-white transition-colors"
          >
            + Adaugă
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.overflow-x-auto::-webkit-scrollbar {
  display: none;
}
.overflow-x-auto {
  -ms-overflow-style: none;
  scrollbar-width: none;
}
</style>

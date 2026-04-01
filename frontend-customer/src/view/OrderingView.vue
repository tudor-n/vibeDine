<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { useWebSocket } from '@/composables/useWebSocket'
import MenuList from '@/components/MenuList.vue'
import Cart from '@/components/Cart.vue';
import FlashDealBanner from '@/components/FlashDealBanner.vue';
import RecommendationWidget from '@/components/RecommendationWidget.vue';

const route = useRoute()
const cartStore = useCartStore()
const sessionStore = useSessionStore()

useWebSocket()

const isCartModalOpen = ref(false)

const totalItems = computed(() => {
  return cartStore.items.reduce((sum, item) => sum + item.quantity, 0)
})

onMounted(async () => {

  const tokenFromUrl = (route.params.qrToken as string) || '11111111-1111-1111-1111-111111111111'

  if (!sessionStore.tableId || sessionStore.qrToken !== tokenFromUrl) {
    try {
      await sessionStore.initializeSession(tokenFromUrl)
      console.log('Sesiune inițializată!', sessionStore.orderId)
    } catch (e) {
      console.error('Nu am putut inițializa sesiunea', e)
    }
  }
})
</script>


<template>
  <FlashDealBanner />
  <div class="min-h-screen bg-gray-50 pb-24 relative">

    <header class="bg-white shadow-sm p-4 sticky top-0 z-10">
      <h1 class="text-xl font-bold text-brand text-center">Meniul Nostru</h1>
    </header>

    <main class="container mx-auto p-4 max-w-3xl">
      <MenuList />
      <RecommendationWidget />
    </main>

    <div
      v-if="cartStore.items.length > 0"
      class="fixed bottom-0 left-0 right-0 bg-white border-t shadow-[0_-10px_15px_-3px_rgba(0,0,0,0.1)] p-4 z-20 flex justify-between items-center"
    >
      <div class="flex flex-col">
        <span class="text-sm text-gray-500">{{ totalItems }} produse</span>
        <span class="font-bold text-xl text-brand">{{ cartStore.total }} RON</span>
      </div>

      <button
        @click="isCartModalOpen = true"
        class="bg-brand text-white font-bold py-3 px-8 rounded-full shadow-md active:scale-95 transition-transform"
      >
        Vezi Comanda
      </button>
    </div>

    <div
      v-if="isCartModalOpen"
      class="fixed inset-0 z-30 flex flex-col bg-gray-50 slide-up"
    >
      <div class="p-4 border-b bg-white flex justify-between items-center shadow-sm">
        <h2 class="text-xl font-bold">Comanda ta</h2>
        <button
          @click="isCartModalOpen = false"
          class="text-gray-500 hover:text-black p-2 bg-gray-100 rounded-full w-10 h-10 flex items-center justify-center font-bold"
        >
          ✕
        </button>
      </div>

      <div class="flex-1 overflow-y-auto">
        <Cart />
      </div>
    </div>

  </div>
</template>



<style scoped>

.slide-up {
  animation: slideUp 0.3s ease-out forwards;
}
@keyframes slideUp {
  from { transform: translateY(100%); }
  to { transform: translateY(0); }
}
</style>

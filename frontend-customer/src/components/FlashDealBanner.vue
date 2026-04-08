<script setup lang="ts">
import { ref, watch, onUnmounted, computed } from 'vue'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { useCartStore } from '@/stores/useCartStore'
import { apiClient } from '@/services/api-client'
import type { Product } from '@shared/types/models'

const flashDealStore = useFlashDealStore()
const sessionStore = useSessionStore()
const cartStore = useCartStore()

const timeLeft = ref(0)
let timerInterval: ReturnType<typeof setInterval> | null = null
const isClaiming = ref(false)


const formattedTime = computed(() => {
  const m = Math.floor(timeLeft.value / 60).toString().padStart(2, '0')
  const s = (timeLeft.value % 60).toString().padStart(2, '0')
  return `${m}:${s}`
})

const startTimer = () => {
  if (timerInterval) clearInterval(timerInterval)

  const deal = flashDealStore.activeDeal
  if (!deal) return


  const updateTime = () => {
    const now = new Date().getTime()
    const expires = new Date(deal.expires_at).getTime()
    const diff = Math.floor((expires - now) / 1000)

    if (diff <= 0) {
      timeLeft.value = 0
      clearInterval(timerInterval!)
      flashDealStore.clearDeal()
    } else {
      timeLeft.value = diff
    }
  }

  updateTime()
  timerInterval = setInterval(updateTime, 1000)
}


watch(
  () => flashDealStore.activeDeal,
  (newDeal) => {
    if (newDeal) {
      startTimer()
    } else {
      if (timerInterval) clearInterval(timerInterval)
    }
  },
  { immediate: true }
)

onUnmounted(() => {
  if (timerInterval) clearInterval(timerInterval)
})

const claimDeal = async () => {
  const deal = flashDealStore.activeDeal
  if (!deal || !sessionStore.orderId) return

  try {
    isClaiming.value = true


    await apiClient.claimFlashDeal(sessionStore.orderId, deal.id)


    const productToAdd: Product = {
      id: deal.menu_item.id,
      category_id: '',
      name: `${deal.menu_item.name} (⚡ Flash Deal)`,
      description: deal.reason,
      price: deal.discounted_price,
      image_url: deal.menu_item.image_url,
      is_available: true
    }

    cartStore.addLocalItemOnly(productToAdd)


    flashDealStore.clearDeal()
  } catch (error) {
    console.error('Eroare la revendicarea ofertei:', error)
  } finally {
    isClaiming.value = false
  }
}
</script>

<template>
  <Transition name="slide-down">
    <div
      v-if="flashDealStore.activeDeal"
      class="fixed top-0 left-0 right-0 z-50 bg-brand text-white shadow-xl px-4 py-3 border-b-4 border-yellow-400"
    >
      <div class="max-w-3xl mx-auto flex flex-col md:flex-row items-center justify-between gap-4">

        <div class="flex-1 text-center md:text-left">
          <div class="flex items-center justify-center md:justify-start gap-2 mb-1">
            <span class="text-xl animate-bounce">⚡</span>
            <h3 class="font-black text-lg uppercase tracking-wider text-yellow-300">Ofertă Limitată!</h3>
          </div>
          <p class="font-medium">
            {{ flashDealStore.activeDeal.menu_item.name }}
          </p>
          <div class="mt-1 flex items-center justify-center md:justify-start gap-3">
            <span class="text-2xl font-bold">{{ flashDealStore.activeDeal.discounted_price }} RON</span>
            <span class="line-through text-brand-light opacity-80 text-sm">
              {{ flashDealStore.activeDeal.menu_item.price }} RON
            </span>
          </div>
        </div>

        <div class="flex items-center gap-4 w-full md:w-auto">
          <div class="bg-black/20 px-4 py-2 rounded-lg font-mono text-2xl font-bold min-w-[90px] text-center shadow-inner">
            {{ formattedTime }}
          </div>

          <button
            @click="claimDeal"
            :disabled="isClaiming"
            class="flex-1 md:flex-none bg-yellow-400 text-red-900 font-black py-3 px-6 rounded-xl shadow-lg hover:bg-yellow-300 active:scale-95 transition-all disabled:opacity-70 disabled:cursor-not-allowed"
          >
            {{ isClaiming ? 'Se adaugă...' : 'Revendică' }}
          </button>
        </div>

        <button
          @click="flashDealStore.clearDeal()"
          class="absolute top-2 right-2 text-white/70 hover:text-white"
        >
          ✕
        </button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.slide-down-enter-active,
.slide-down-leave-active {
  transition: transform 0.4s cubic-bezier(0.175, 0.885, 0.32, 1.275);
}

.slide-down-enter-from,
.slide-down-leave-to {
  transform: translateY(-100%);
}
</style>

<script setup lang="ts">
import { ref, watch, onUnmounted, computed } from 'vue'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { apiClient } from '@/services/api-client'

const flashDealStore = useFlashDealStore()

const timeLeft = ref(0)
let timerInterval: ReturnType<typeof setInterval> | null = null
const isClaiming = ref(false)
const claimError = ref<string | null>(null)
const claimSuccess = ref(false)


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
    const expires = new Date(deal.expiresAt).getTime()
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
      claimError.value = null
      claimSuccess.value = false
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
  const orderId = flashDealStore.activeDealOrderId

  if (!deal || !orderId) {
    claimError.value = 'Sesiune invalidă. Te rugăm să reîncărci pagina.'
    return
  }

  try {
    isClaiming.value = true
    claimError.value = null

    // Backend creates the OrderItem — we don't need addLocalItemOnly
    await apiClient.claimFlashDeal(orderId, deal.id)

    claimSuccess.value = true
    // Short delay so user sees the success state before banner disappears
    setTimeout(() => {
      flashDealStore.clearDeal()
    }, 1500)

  } catch (error) {
    if (error instanceof Error) {
      claimError.value = error.message
    } else {
      claimError.value = 'Eroare la revendicarea ofertei. Încearcă din nou.'
    }
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
            {{ flashDealStore.activeDeal.menuItem.name }}
          </p>
          <div class="mt-1 flex items-center justify-center md:justify-start gap-3">
            <span class="text-2xl font-bold">{{ flashDealStore.activeDeal.discountedPrice }} RON</span>
            <span class="line-through text-brand-light opacity-80 text-sm">
              {{ flashDealStore.activeDeal.menuItem.price }} RON
            </span>
          </div>
        </div>

        <div class="flex flex-col items-center gap-2 w-full md:w-auto">
          <div class="flex items-center gap-4">
            <div class="bg-black/20 px-4 py-2 rounded-lg font-mono text-2xl font-bold min-w-[90px] text-center shadow-inner">
              {{ formattedTime }}
            </div>

            <!-- Success state -->
            <div
              v-if="claimSuccess"
              class="flex-1 md:flex-none bg-green-400 text-green-900 font-black py-3 px-6 rounded-xl shadow-lg flex items-center gap-2"
            >
              <span>✓</span> Adăugat!
            </div>

            <!-- Normal claim button -->
            <button
              v-else
              @click="claimDeal"
              :disabled="isClaiming"
              class="flex-1 md:flex-none bg-yellow-400 text-red-900 font-black py-3 px-6 rounded-xl shadow-lg hover:bg-yellow-300 active:scale-95 transition-all disabled:opacity-70 disabled:cursor-not-allowed"
            >
              {{ isClaiming ? 'Se adaugă...' : 'Revendică' }}
            </button>
          </div>

          <!-- Error message -->
          <p v-if="claimError" class="text-xs text-yellow-200 bg-black/20 px-3 py-1 rounded-lg">
            {{ claimError }}
          </p>
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

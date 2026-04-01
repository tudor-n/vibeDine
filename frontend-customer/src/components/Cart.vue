<!-- eslint-disable vue/multi-word-component-names -->
<script setup lang="ts">
import { ref } from 'vue'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { apiClient } from '@/services/api-client'

const cartStore = useCartStore()
const sessionStore = useSessionStore()

const isSubmitting = ref(false)
const submitError = ref<string | null>(null)
const submitSuccess = ref(false)

async function submitOrder() {
  if (!sessionStore.orderId) {
    submitError.value = 'Eroare: Sesiunea nu este validă (lipsește orderId).'
    return
  }

  if (cartStore.items.length === 0) {
    submitError.value = 'Coșul este gol.'
    return
  }

  try {
    isSubmitting.value = true
    submitError.value = null


    await apiClient.submitOrder(sessionStore.orderId)

    submitSuccess.value = true
    cartStore.clearCart()


    sessionStorage.removeItem('orderId')
    sessionStore.orderId = null

  } catch (error) {
    if (error instanceof Error) {
      submitError.value = error.message
    } else {
      submitError.value = 'A apărut o eroare la trimiterea comenzii.'
    }
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <div class="p-4 flex flex-col h-full">
    <div v-if="submitSuccess" class="text-center py-10">
      <div class="text-green-500 text-5xl mb-4">✓</div>
      <h3 class="text-2xl font-bold mb-2">Comanda a fost trimisă!</h3>
      <p class="text-gray-600">Bucătarii noștri o pregătesc chiar acum.</p>
    </div>

    <div v-else-if="cartStore.items.length === 0" class="text-center py-10 text-gray-500">
      Coșul tău este gol. Adaugă câteva preparate delicioase din meniu!
    </div>

    <div v-else class="flex-1 flex flex-col">
      <ul class="flex-1 overflow-y-auto divide-y">
        <li
          v-for="item in cartStore.items"
          :key="item.id"
          class="py-4 flex justify-between items-center"
        >
          <div class="flex-1">
            <h4 class="font-bold">{{ item.name }}</h4>
            <span class="text-brand font-semibold">{{ item.price }} RON</span>
          </div>

          <div class="flex items-center gap-3 bg-gray-100 rounded-lg p-1">
            <button
              @click="cartStore.removeItem(item.id)"
              class="w-8 h-8 flex items-center justify-center bg-white rounded shadow-sm text-gray-700 hover:text-red-500 font-bold"
            >
              -
            </button>
            <span class="w-4 text-center font-bold">{{ item.quantity }}</span>
            <button
              @click="cartStore.addItem(item)"
              class="w-8 h-8 flex items-center justify-center bg-white rounded shadow-sm text-brand font-bold"
            >
              +
            </button>
          </div>
        </li>
      </ul>

      <div v-if="submitError" class="mt-4 p-3 bg-red-100 text-red-700 rounded-lg text-sm text-center">
        {{ submitError }}
      </div>

      <div class="mt-6 border-t pt-4">
        <div class="flex justify-between items-center mb-4 text-lg">
          <span class="font-semibold text-gray-700">Total estimat:</span>
          <span class="font-bold text-2xl text-brand">{{ cartStore.total }} RON</span>
        </div>

        <button
          @click="submitOrder"
          :disabled="isSubmitting"
          class="w-full bg-brand text-white font-bold py-4 rounded-xl shadow-lg hover:bg-brand-light active:scale-95 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex justify-center items-center"
        >
          <span v-if="isSubmitting" class="animate-pulse">Se trimite...</span>
          <span v-else>Trimite Comanda</span>
        </button>
      </div>
    </div>
  </div>
</template>

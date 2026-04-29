<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useWebSocket } from '@/composables/useWebSocket'
import { useTheme } from '@/composables/useTheme'

import WelcomeView from '@/components/screens/WelcomeView.vue'
import PebblesView from '@/components/screens/PebblesView.vue'
import MenuView from '@/components/screens/MenuView.vue'
import CartView from '@/components/screens/CartView.vue'
import FlashDealView from '@/components/screens/FlashDealView.vue'
import OrderSentView from '@/components/screens/OrderSentView.vue'

type Screen = 'welcome' | 'pebbles' | 'menu' | 'cart' | 'sent'

const route = useRoute()
const cartStore = useCartStore()
const sessionStore = useSessionStore()
const flashDealStore = useFlashDealStore()
const { theme, tokens: T, toggle: toggleTheme } = useTheme()

useWebSocket()

const screen = ref<Screen>('welcome')
const activeCategoryId = ref<string | null>(null)
const lastTotal = ref(0)

onMounted(async () => {
  const tokenFromUrl = (route.params.qrToken as string) || 'aaaaaaaa-0000-0000-0000-000000000001'
  try {
    await sessionStore.initializeSession(tokenFromUrl)
  } catch (e) {
    console.error('Nu am putut inițializa sesiunea', e)
  }
})

function openMenu() { screen.value = 'pebbles' }
function openCategory(id: string) {
  activeCategoryId.value = id
  screen.value = 'menu'
}
function backToPebbles() { screen.value = 'pebbles' }
function openCart() { screen.value = 'cart' }
function backFromCart() { screen.value = 'menu' }

async function handleSubmitted() {
  lastTotal.value = cartStore.total
  cartStore.clearCart()
  screen.value = 'sent'
  // Refresh the draft order so the next round of ordering has a fresh id.
  if (sessionStore.qrToken) {
    sessionStore.initializeSession(sessionStore.qrToken).catch(() => undefined)
  }
}

function orderAgain() {
  activeCategoryId.value = null
  screen.value = 'pebbles'
}

const flashOverlayActive = computed(() => Boolean(flashDealStore.activeDeal))
</script>

<template>
  <div class="stage" :style="{ background: T.paper }">
    <div class="phone-frame">
      <Transition name="fade" mode="out-in">
        <WelcomeView
          v-if="screen === 'welcome'"
          key="welcome"
          @open-menu="openMenu"
        />
        <PebblesView
          v-else-if="screen === 'pebbles'"
          key="pebbles"
          @open-category="openCategory"
          @back="screen = 'welcome'"
        />
        <MenuView
          v-else-if="screen === 'menu' && activeCategoryId"
          :key="`menu-${activeCategoryId}`"
          :category-id="activeCategoryId"
          @back="backToPebbles"
          @open-cart="openCart"
        />
        <CartView
          v-else-if="screen === 'cart'"
          key="cart"
          @close="backFromCart"
          @submitted="handleSubmitted"
        />
        <OrderSentView
          v-else-if="screen === 'sent'"
          key="sent"
          :total="lastTotal"
          @order-again="orderAgain"
          @call-waiter="() => undefined"
        />
      </Transition>

      <Transition name="overlay">
        <div v-if="flashOverlayActive" class="overlay">
          <FlashDealView />
        </div>
      </Transition>

      <button
        class="theme-toggle"
        :style="{
          background: T.card,
          color: T.ink,
          border: `1px solid ${T.ink12}`,
        }"
        :aria-label="theme === 'dark' ? 'Comutați la tema deschisă' : 'Comutați la tema închisă'"
        :aria-pressed="theme === 'dark'"
        @click="toggleTheme"
      >
        <svg
          v-if="theme === 'dark'"
          viewBox="0 0 24 24"
          width="22" height="22"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
        </svg>
        <svg
          v-else
          viewBox="0 0 24 24"
          width="22" height="22"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
        </svg>
      </button>
    </div>
  </div>
</template>

<style scoped>
.stage {
  width: 100%;
  min-height: 100vh;
  display: flex;
  align-items: stretch;
  justify-content: center;
}
.phone-frame {
  width: 100%;
  max-width: 390px;
  min-height: 100vh;
  height: 100dvh;
  position: relative;
  overflow: hidden;
  box-shadow: 0 30px 60px -20px rgba(21, 20, 15, 0.18);
}
@media (min-width: 480px) {
  .stage {
    padding: 24px 0;
    align-items: center;
  }
  .phone-frame {
    height: 844px;
    min-height: 0;
    border-radius: 44px;
  }
}

.overlay {
  position: absolute;
  inset: 0;
  z-index: 50;
}

.theme-toggle {
  position: absolute;
  top: 14px;
  right: 14px;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  cursor: pointer;
  z-index: 60;
  font-family: inherit;
  transition: transform 0.18s ease, box-shadow 0.18s ease;
  box-shadow: 0 2px 8px -4px rgba(0, 0, 0, 0.25);
}
.theme-toggle:active {
  transform: scale(0.94);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.22s ease, transform 0.22s ease;
}
.fade-enter-from {
  opacity: 0;
  transform: translateY(8px);
}
.fade-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}

.overlay-enter-active,
.overlay-leave-active {
  transition: opacity 0.22s ease, transform 0.28s cubic-bezier(0.32, 0.72, 0, 1);
}
.overlay-enter-from,
.overlay-leave-to {
  opacity: 0;
  transform: translateY(20px);
}
</style>

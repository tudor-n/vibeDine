<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useWebSocket } from '@/composables/useWebSocket'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'

import LanguageSelectView from '@/components/screens/LanguageSelectView.vue'
import PebblesView from '@/components/screens/PebblesView.vue'
import MenuView from '@/components/screens/MenuView.vue'
import CartView from '@/components/screens/CartView.vue'
import FlashDealView from '@/components/screens/FlashDealView.vue'
import OrderSentView from '@/components/screens/OrderSentView.vue'
import FlashDealIcon from '@/components/FlashDealIcon.vue'
import CallWaiterButton from '@/components/CallWaiterButton.vue'

type Screen = 'language' | 'pebbles' | 'menu' | 'cart' | 'sent'

const route = useRoute()
const cartStore = useCartStore()
const sessionStore = useSessionStore()
const flashDealStore = useFlashDealStore()
const { theme, tokens: T, toggle: toggleTheme } = useTheme()
const { t } = useLocale()

useWebSocket()

const screen = ref<Screen>('language')
const activeCategoryId = ref<string | null>(null)
const lastTotal = ref(0)
const sessionError = ref<null | 'missing' | 'invalid'>(null)
// First time a deal arrives we pop the full-screen offer; once the user
// dismisses it the icon takes over and stays visible for the remaining 2 min.
const showFlashDealModal = ref(false)
let autoOpenedDealId: string | null = null

onMounted(async () => {
  const tokenFromUrl = (route.params.qrToken as string) || ''
  if (!tokenFromUrl) {
    sessionError.value = 'missing'
    return
  }
  try {
    await sessionStore.initializeSession(tokenFromUrl)
    if (sessionStore.restaurantName) document.title = sessionStore.restaurantName
  } catch (e) {
    console.error('Could not initialize session', e)
    sessionError.value = 'invalid'
  }
})

function onLanguageSelected() {
  screen.value = 'pebbles'
}
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

const flashIconVisible = computed(() => Boolean(flashDealStore.activeDeal))
const flashOverlayActive = computed(
  () => Boolean(flashDealStore.activeDeal) && showFlashDealModal.value,
)

function openFlashDeal() {
  showFlashDealModal.value = true
}
function closeFlashDeal() {
  showFlashDealModal.value = false
}

watch(
  () => flashDealStore.activeDeal,
  (deal) => {
    if (!deal) {
      showFlashDealModal.value = false
      autoOpenedDealId = null
      return
    }
    // Auto-open once per deal so a fresh offer pops up; later dismissals just
    // collapse to the icon without re-opening.
    if (deal.id !== autoOpenedDealId) {
      autoOpenedDealId = deal.id
      showFlashDealModal.value = true
    }
  },
  { immediate: true },
)

// Language picker takes priority over the session-error overlay so the user
// can localize before seeing the error message.
const showSessionError = computed(() => sessionError.value !== null && screen.value !== 'language')
</script>

<template>
  <div class="stage" :style="{ background: T.paper }">
    <div class="phone-frame">
      <div
        v-if="showSessionError"
        class="session-error"
        :style="{ background: T.paper, color: T.ink }"
      >
        <div class="session-error-card" :style="{ background: T.card, border: `1px solid ${T.ink12}` }">
          <div class="session-error-mark" :style="{ color: T.accent }">⚠</div>
          <h1 class="session-error-title">{{ t('session.invalidTitle') }}</h1>
          <p class="session-error-text" :style="{ color: T.ink70 }">
            {{ sessionError === 'missing'
              ? t('session.missingMessage')
              : t('session.invalidMessage') }}
          </p>
        </div>
      </div>

      <Transition v-if="!showSessionError" name="fade" mode="out-in">
        <LanguageSelectView
          v-if="screen === 'language'"
          key="language"
          @select="onLanguageSelected"
        />
        <PebblesView
          v-else-if="screen === 'pebbles'"
          key="pebbles"
          @open-category="openCategory"
          @back="screen = 'language'"
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
        />
      </Transition>

      <Transition name="overlay">
        <div v-if="flashOverlayActive" class="overlay">
          <FlashDealView @close="closeFlashDeal" />
        </div>
      </Transition>

      <FlashDealIcon
        v-if="flashIconVisible && !flashOverlayActive"
        @open="openFlashDeal"
      />

      <CallWaiterButton
        v-if="screen !== 'language' && !showSessionError && !flashOverlayActive"
      />

      <button
        class="theme-toggle"
        :style="{
          background: T.card,
          color: T.ink,
          border: `1px solid ${T.ink12}`,
        }"
        :aria-label="theme === 'dark' ? t('theme.toLight') : t('theme.toDark')"
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

.session-error {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 28px;
  z-index: 40;
}
.session-error-card {
  width: 100%;
  max-width: 320px;
  border-radius: 28px;
  padding: 32px 26px;
  text-align: center;
  box-shadow: 0 20px 40px -22px rgba(21, 20, 15, 0.25);
}
.session-error-mark {
  font-size: 40px;
  line-height: 1;
  margin-bottom: 14px;
}
.session-error-title {
  font-size: 22px;
  font-weight: 700;
  margin-bottom: 10px;
}
.session-error-text {
  font-size: 15px;
  line-height: 1.5;
}
</style>

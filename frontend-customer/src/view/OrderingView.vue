<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { useGuestStore } from '@/stores/useGuestStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useHappyHourStore } from '@/stores/useHappyHourStore'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import { useWebSocket } from '@/composables/useWebSocket'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'

import LanguageSelectView from '@/components/screens/LanguageSelectView.vue'
import WelcomeView from '@/components/screens/WelcomeView.vue'
import PebblesView from '@/components/screens/PebblesView.vue'
import MenuView from '@/components/screens/MenuView.vue'
import CartView from '@/components/screens/CartView.vue'
import FlashDealView from '@/components/screens/FlashDealView.vue'
import OrderSentView from '@/components/screens/OrderSentView.vue'
import MergedView from '@/components/screens/MergedView.vue'
import FlashDealIcon from '@/components/FlashDealIcon.vue'
import CallWaiterButton from '@/components/CallWaiterButton.vue'

type Screen = 'language' | 'welcome' | 'pebbles' | 'menu' | 'cart' | 'sent' | 'merged'

const route = useRoute()
const cartStore = useCartStore()
const sessionStore = useSessionStore()
const guestStore = useGuestStore()
const flashDealStore = useFlashDealStore()
const happyHourStore = useHappyHourStore()
const orderStatusStore = useOrderStatusStore()
const { theme, tokens: T, toggle: toggleTheme } = useTheme()
const { t } = useLocale()

useWebSocket()

// Navigation state is persisted so an accidental tab refresh keeps the diner on
// the screen they were on instead of bouncing them back to the language picker
// (their cart already survives via restoreFromServerIfEmpty). sessionStorage is
// per-tab and clears when the tab closes, so a genuinely fresh QR scan still
// starts at the language screen — we only restore within the same tab session.
const SCREEN_KEY = 'screen'
const CATEGORY_KEY = 'activeCategoryId'
const CART_ORIGIN_KEY = 'cartOrigin'
const SCREENS: Screen[] = ['language', 'welcome', 'pebbles', 'menu', 'cart', 'sent', 'merged']

function readScreen(key: string, fallback: Screen): Screen {
  const stored = sessionStorage.getItem(key)
  return stored && SCREENS.includes(stored as Screen) ? (stored as Screen) : fallback
}

const restoredCategory = sessionStorage.getItem(CATEGORY_KEY) || null
const restoredScreen = readScreen(SCREEN_KEY, 'language')
// A fresh session (no key written yet) is what makes a branded-with-photo
// table land on the welcome screen; a mid-session refresh (key present, even
// if its value fell back to the default) resumes wherever it was instead.
const hadStoredScreen = sessionStorage.getItem(SCREEN_KEY) !== null
// The menu screen can't render without a category, so fall back to the category
// list if the restored category is gone.
const screen = ref<Screen>(
  restoredScreen === 'menu' && !restoredCategory ? 'pebbles' : restoredScreen,
)
const activeCategoryId = ref<string | null>(restoredCategory)
// Screen the cart was opened from, so closing it returns there (the menu, or
// the categories page when the cart was opened from its "Coșul meu" bar).
const cartOrigin = ref<Screen>(readScreen(CART_ORIGIN_KEY, 'menu'))

watch(screen, (v) => sessionStorage.setItem(SCREEN_KEY, v))
watch(activeCategoryId, (v) => {
  if (v) sessionStorage.setItem(CATEGORY_KEY, v)
  else sessionStorage.removeItem(CATEGORY_KEY)
})
watch(cartOrigin, (v) => sessionStorage.setItem(CART_ORIGIN_KEY, v))
// lastTotal + orderInProgress are persisted so the "view order status" button
// (and the total it shows) survive an accidental tab refresh while an order is
// live — see useOrderStatusStore for the matching timeline persistence.
const lastTotal = ref(Number(sessionStorage.getItem('lastTotal')) || 0)
// True once the diner has sent at least one order this session. Keeps the
// top "view progress" button available after they leave the progress screen.
const orderInProgress = ref(sessionStorage.getItem('orderInProgress') === 'true')
watch(lastTotal, (v) => sessionStorage.setItem('lastTotal', String(v)))
watch(orderInProgress, (v) => sessionStorage.setItem('orderInProgress', v ? 'true' : 'false'))
// The order is "archived to the order log" when its status flips to completed:
// the kitchen's 45s ready-grace elapses, the order leaves the admin's active
// list, and the customer gets that completed event (stamped as servedAt). We
// keep the re-open button for 30s past that, then retire it for the round.
const PROGRESS_DISMISS_MS = 30_000
const progressDismissed = ref(false)
let progressDismissTimer: ReturnType<typeof setTimeout> | null = null

// Derive the button's retirement from the persisted archive timestamp rather
// than a fired-once timer, so it stays correct across a refresh (e.g. the order
// was already archived 10s before reload → hide it 20s after reload).
function evaluateProgressDismissal() {
  if (progressDismissTimer) {
    clearTimeout(progressDismissTimer)
    progressDismissTimer = null
  }
  const archivedAt = orderStatusStore.servedAt
  if (!archivedAt) {
    progressDismissed.value = false
    return
  }
  const remaining = PROGRESS_DISMISS_MS - (Date.now() - archivedAt.getTime())
  if (remaining <= 0) {
    progressDismissed.value = true
    return
  }
  progressDismissed.value = false
  progressDismissTimer = setTimeout(() => {
    progressDismissed.value = true
    progressDismissTimer = null
  }, remaining)
}
const sessionError = ref<null | 'missing' | 'invalid'>(null)
// First time a deal arrives we pop the full-screen offer; once the user
// dismisses it the icon takes over and stays visible for the remaining 2 min.
const showFlashDealModal = ref(false)
let autoOpenedDealId: string | null = null

// Presence heartbeat + live panel: poll /party every 15s. The call carries our
// guestId, which keeps our seat "fresh" on the server; phones that stop polling
// (tab closed, customer left) fall out of everyone's party list within ~1 min.
const PARTY_POLL_MS = 15_000
let partyPoll: ReturnType<typeof setInterval> | null = null

function onVisibilityChange() {
  // Coming back from the background: beat immediately so this phone reappears
  // in the party without waiting for the next interval tick.
  if (!document.hidden) guestStore.refreshParty(sessionStore.qrToken)
}

onMounted(async () => {
  const tokenFromUrl = (route.params.qrToken as string) || ''
  if (!tokenFromUrl) {
    sessionError.value = 'missing'
    return
  }
  try {
    const session = await sessionStore.initializeSession(tokenFromUrl)
    // Refresh recovery: if this load lost its sessionStorage cart (a phone
    // re-scanning the QR opens a fresh browser context), rebuild it from the
    // backend's persisted draft so the diner keeps the order they were building.
    cartStore.restoreFromServerIfEmpty(session.activeOrder.items)
    if (sessionStore.restaurantName) document.title = sessionStore.restaurantName
    // A fresh session (no restored screen) on a table branded with a welcome
    // photo starts on the branded welcome screen instead of the language
    // picker — the welcome screen subsumes language selection. Unbranded (or
    // no photo) tables, and any mid-session refresh, are unaffected.
    if (!hadStoredScreen && sessionStore.branding?.enabled && sessionStore.branding?.photoUrl) {
      screen.value = 'welcome'
    }
    // Poll the live happy hour app-wide (not just on the menu screen) so the bar
    // shows on the categories page and stays visible after an order is sent.
    happyHourStore.startPolling()
  } catch (e) {
    console.error('Could not initialize session', e)
    sessionError.value = 'invalid'
    return
  }
  partyPoll = setInterval(() => guestStore.refreshParty(sessionStore.qrToken), PARTY_POLL_MS)
  document.addEventListener('visibilitychange', onVisibilityChange)
  // After a refresh the order may already be archived (or partway through its
  // 30s grace) — reconcile the button's retirement against the restored state.
  evaluateProgressDismissal()
})

onUnmounted(() => {
  if (partyPoll) clearInterval(partyPoll)
  if (progressDismissTimer) clearTimeout(progressDismissTimer)
  happyHourStore.stopPolling()
  document.removeEventListener('visibilitychange', onVisibilityChange)
})

function onLanguageSelected() {
  screen.value = 'pebbles'
}
function openCategory(id: string) {
  activeCategoryId.value = id
  screen.value = 'menu'
}
function backToPebbles() { screen.value = 'pebbles' }
function openCart() {
  // Remember where we came from (menu or pebbles) so close returns there.
  if (screen.value === 'menu' || screen.value === 'pebbles') cartOrigin.value = screen.value
  screen.value = 'cart'
}
function backFromCart() { screen.value = cartOrigin.value }

function handleSubmitted() {
  lastTotal.value = cartStore.total
  cartStore.clearCart()
  // Drop the previous round's status so the timeline starts fresh instead of
  // instantly showing the last order's "completed" stage.
  orderStatusStore.clear()
  // New round → revive the progress button and cancel any pending auto-hide
  // (clear() nulled servedAt, so this resets dismissal to "not archived yet").
  evaluateProgressDismissal()
  orderInProgress.value = true
  screen.value = 'sent'
  // Deliberately NO re-join here: the backend freed this guest's seat when the
  // order went to the kitchen. Re-joining now would put the guest straight back
  // into everyone's party panel; we re-join only when they start a new round.
}

// Starting the next round re-joins: the old seat is gone, so this claims a
// fresh "Guest N" slot with a fresh draft.
async function orderAgain() {
  activeCategoryId.value = null
  if (sessionStore.qrToken) {
    try {
      await sessionStore.initializeSession(sessionStore.qrToken)
    } catch (e) {
      console.error('Could not start a new order', e)
    }
  }
  screen.value = 'pebbles'
}

// This phone's draft was merged into another guest's order (flagged by the
// PartyMerged websocket handler). Force the merged screen so the user can't
// keep editing a draft that no longer exists.
watch(
  () => guestStore.mergedInto,
  (merged) => {
    if (merged) screen.value = 'merged'
  },
)

// From the merged screen: abandon the merged draft and open a fresh, separate
// order. Re-joining returns a brand-new draft (the old one is "merged").
async function startSeparateOrder() {
  cartStore.resetForNewOrder()
  guestStore.clearMerged()
  activeCategoryId.value = null
  if (sessionStore.qrToken) {
    try {
      await sessionStore.initializeSession(sessionStore.qrToken)
    } catch (e) {
      console.error('Could not start a new order', e)
    }
  }
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

// Re-open the progress screen for the order that's already on its way.
function viewProgress() {
  screen.value = 'sent'
}

// Once an order is live, offer a top button to jump back to its progress from
// any screen except the progress screen itself.
const showProgressButton = computed(
  () =>
    orderInProgress.value &&
    !progressDismissed.value &&
    screen.value !== 'sent' &&
    screen.value !== 'language' &&
    !showSessionError.value &&
    !flashOverlayActive.value,
)

// Re-arm the retirement whenever the archive timestamp changes (set on the
// completed event, nulled on a new round). evaluateProgressDismissal reads the
// timestamp itself, so it handles both fresh archival and post-refresh revival.
watch(() => orderStatusStore.servedAt, evaluateProgressDismissal)
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
        <WelcomeView
          v-else-if="screen === 'welcome'"
          key="welcome"
          @continue="screen = 'pebbles'"
        />
        <PebblesView
          v-else-if="screen === 'pebbles'"
          key="pebbles"
          @open-category="openCategory"
          @open-cart="openCart"
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
        <MergedView
          v-else-if="screen === 'merged'"
          key="merged"
          @order-again="startSeparateOrder"
        />
      </Transition>

      <Transition name="overlay">
        <div v-if="flashOverlayActive" class="overlay">
          <FlashDealView @close="closeFlashDeal" />
        </div>
      </Transition>

      <!-- "Someone joined your table" toast — auto-dismissed by the guest store. -->
      <Transition name="toast" mode="out-in">
        <div
          v-if="guestStore.joinNotice"
          :key="guestStore.joinNotice.key"
          class="join-toast"
          role="status"
          :style="{ background: T.ink, color: T.paper }"
        >
          <span class="join-toast-title">
            {{ t('party.joinedToast', { name: guestStore.joinNotice.name }) }}
          </span>
          <span class="join-toast-hint">{{ t('party.joinedToastHint') }}</span>
        </div>
      </Transition>

      <FlashDealIcon
        v-if="flashIconVisible && !flashOverlayActive"
        @open="openFlashDeal"
      />

      <CallWaiterButton
        v-if="screen !== 'language' && !showSessionError && !flashOverlayActive"
      />

      <!-- Re-open the live order's progress timeline. Appears once an order has
           been sent and the diner has navigated away from the progress screen. -->
      <button
        v-if="showProgressButton"
        class="progress-toggle"
        :style="{ background: T.lime, color: '#000' }"
        :aria-label="t('sent.viewProgress')"
        @click="viewProgress"
      >
        <svg
          viewBox="0 0 24 24"
          width="22" height="22"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M6 3h12v18l-3-2-3 2-3-2-3 2V3Z" />
          <path d="M9 8h6M9 12h4" />
        </svg>
      </button>

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
  min-height: 100dvh;
  display: flex;
  align-items: stretch;
  justify-content: center;
}
/* On phones the frame IS the screen: full width, full dynamic height (dvh
   tracks the browser URL bar collapsing). The framed 390px preview only
   applies on larger screens. */
.phone-frame {
  width: 100%;
  height: 100vh;
  height: 100dvh;
  position: relative;
  overflow: hidden;
}
@media (min-width: 480px) {
  .stage {
    padding: 24px 0;
    align-items: center;
  }
  .phone-frame {
    max-width: 390px;
    height: 844px;
    max-height: calc(100vh - 48px);
    border-radius: 44px;
    box-shadow: 0 30px 60px -20px rgba(21, 20, 15, 0.18);
  }
}

.overlay {
  position: absolute;
  inset: 0;
  z-index: 50;
}

.join-toast {
  position: absolute;
  top: 64px;
  left: 20px;
  right: 20px;
  z-index: 70;
  border-radius: 16px;
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  text-align: center;
  box-shadow: 0 14px 30px -14px rgba(0, 0, 0, 0.45);
  pointer-events: none;
}
.join-toast-title {
  font-size: 15px;
  font-weight: 700;
  letter-spacing: -0.01em;
}
.join-toast-hint {
  font-size: 13px;
  opacity: 0.75;
}

.toast-enter-active,
.toast-leave-active {
  transition: opacity 0.25s ease, transform 0.25s ease;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(-12px);
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

/* "View progress" button — sits just left of the theme toggle, same size, so
   the two read as a tidy top-right cluster. Lime to echo the order-sent
   "Confirmed" pill the diner saw when they placed the order. */
.progress-toggle {
  position: absolute;
  top: 14px;
  right: 62px;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  border: none;
  display: grid;
  place-items: center;
  cursor: pointer;
  z-index: 60;
  font-family: inherit;
  -webkit-tap-highlight-color: transparent;
  transition: transform 0.18s ease, box-shadow 0.18s ease;
  box-shadow: 0 2px 8px -4px rgba(0, 0, 0, 0.25);
}
.progress-toggle:active {
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

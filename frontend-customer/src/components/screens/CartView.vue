<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { toneFromKey } from '@/design/tokens'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useCartStore } from '@/stores/useCartStore'
import { useSessionStore } from '@/stores/useSessionStore'
import { useGuestStore } from '@/stores/useGuestStore'
import { apiClient } from '@/services/api-client'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'
import FoodTile from '@/components/ui/FoodTile.vue'
import PartyPanel from '@/components/PartyPanel.vue'

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'submitted'): void
}>()

const cartStore = useCartStore()
const sessionStore = useSessionStore()
const guestStore = useGuestStore()
const { tokens: T, theme } = useTheme()
const { t } = useLocale()

// Refresh the party panel when the cart opens so each phone sees the others'
// current carts (also kept live via SignalR PartyMemberJoined / PartyMerged).
onMounted(() => guestStore.refreshParty(sessionStore.qrToken))

// Initiator path: after merging, the backend order already holds everyone's
// items, so mirror the consolidated order back into this cart to review/submit.
async function handleMerge() {
  try {
    const res = await guestStore.merge(sessionStore.qrToken)
    if (res) cartStore.replaceFromServer(res.activeOrder.items)
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : t('cart.errorSubmit')
  }
}

// Defensive against HMR: the store may not yet expose `wasRecommended`
// after a hot reload until Pinia re-evaluates the setup. Wrap the call so
// a stale store can never break the render.
function isRecommended(id: string): boolean {
  const fn = (cartStore as { wasRecommended?: (id: string) => boolean }).wasRecommended
  return typeof fn === 'function' ? fn(id) : false
}

const isSubmitting = ref(false)
const submitError = ref<string | null>(null)

const tableLabel = computed(() => {
  const number = sessionStore.tableNumber
  if (!number) return t('common.table')
  return `${t('common.table')} ${number}`
})

const itemCount = computed(() =>
  cartStore.items.reduce((sum, i) => sum + i.quantity, 0),
)

const prepEstimate = computed(() => {
  // Rough heuristic: 8 minutes base + 2 min per item.
  return Math.max(12, 8 + itemCount.value * 2)
})

async function submit() {
  if (!sessionStore.orderId) {
    submitError.value = t('cart.errorInvalidSession')
    return
  }
  if (cartStore.items.length === 0) {
    submitError.value = t('cart.errorEmpty')
    return
  }
  try {
    isSubmitting.value = true
    submitError.value = null
    await apiClient.submitOrder(sessionStore.orderId)
    emit('submitted')
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : t('cart.errorSubmit')
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <PhoneScreen :theme="theme">
    <div
      class="bloom bloom-accent"
      :style="{ background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)` }"
    />
    <div
      class="bloom bloom-lime"
      :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
    />

    <div class="header">
      <div class="status-table" :style="{ color: T.ink70 }">{{ tableLabel }}</div>
      <div class="title">{{ t('cart.title') }}</div>
      <button
        class="close-btn"
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink }"
        :aria-label="t('common.close')"
        @click="emit('close')"
      >✕</button>
    </div>

    <div class="scroll">
    <p class="lede" :style="{ color: T.ink70 }">
      <template v-if="cartStore.items.length > 0">
        {{ itemCount === 1 ? t('cart.selectedOne', { count: itemCount }) : t('cart.selectedMany', { count: itemCount }) }}{{ t('cart.prepEstimate', { minutes: prepEstimate }) }}
      </template>
      <template v-else>
        {{ t('cart.empty') }}
      </template>
    </p>

    <div v-if="cartStore.items.length > 0" class="items">
      <div
        v-for="it in cartStore.items"
        :key="it.id"
        class="item"
        :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
      >
        <FoodTile
          :tone="toneFromKey(it.id)"
          :image="it.imageUrl"
          :radius="14"
          class="item-tile"
        />
        <div class="item-body">
          <div class="item-name">{{ it.name }}</div>
          <div
            v-if="isRecommended(it.id)"
            class="ai-pill"
            :style="{ background: T.lime, color: T.ink }"
          >{{ t('cart.aiSuggested') }}</div>
          <div class="item-row">
            <div class="item-price">
              {{ it.price * it.quantity }}
              <span class="item-price-unit" :style="{ color: T.ink70 }">{{ t('common.ron') }}</span>
            </div>
            <div
              class="stepper"
              :style="{ background: T.paper, border: `1px solid ${T.ink12}` }"
            >
              <button
                class="stepper-btn"
                :style="{ color: T.ink70 }"
                :aria-label="t('cart.decreaseQty')"
                @click="cartStore.removeItem(it.id)"
              >−</button>
              <span class="qty">{{ it.quantity }}</span>
              <button
                class="stepper-btn"
                :style="{ color: T.accent }"
                :aria-label="t('cart.increaseQty')"
                @click="cartStore.addItem(it)"
              >+</button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div v-if="cartStore.items.length > 0" class="summary-wrap">
      <div class="summary" :style="{ background: T.card, border: `1px solid ${T.ink12}` }">
        <div class="row" :style="{ color: T.ink70 }">
          <span>{{ t('cart.subtotal') }}</span><span>{{ cartStore.total }} {{ t('common.ron') }}</span>
        </div>
        <div class="row" :style="{ color: T.ink70 }">
          <span>{{ t('cart.service') }}</span><span>{{ t('cart.serviceIncluded') }}</span>
        </div>
        <div class="total" :style="{ borderTop: `1px solid ${T.ink12}` }">
          <span class="total-label">{{ t('cart.total') }}</span>
          <span class="total-value">
            {{ cartStore.total }}
            <span class="total-unit" :style="{ color: T.ink70 }">{{ t('common.ron') }}</span>
          </span>
        </div>
      </div>
    </div>

    <PartyPanel @merge="handleMerge" />

    <div
      v-if="submitError"
      class="error"
      :style="{ background: `${T.accent}1F`, color: T.accentDk }"
    >{{ submitError }}</div>
    </div>

    <div v-if="cartStore.items.length > 0" class="cta">
      <BigButton :theme="theme" :disabled="isSubmitting" @click="submit">
        <span>{{ isSubmitting ? t('cart.submitting') : t('cart.submit') }}</span>
      </BigButton>
    </div>
  </PhoneScreen>
</template>

<style scoped>
.bloom {
  position: absolute;
  border-radius: 50%;
  pointer-events: none;
  z-index: 0;
}
.bloom-accent {
  top: -50px;
  right: -60px;
  width: 240px;
  height: 240px;
  filter: blur(45px);
  opacity: 0.20;
}
.bloom-lime {
  bottom: 140px;
  left: -50px;
  width: 200px;
  height: 200px;
  filter: blur(40px);
  opacity: 0.18;
}
.header {
  position: relative;
  z-index: 1;
  padding: 10px 22px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.status-table {
  font-size: 15px;
  font-weight: 600;
  flex: 1;
}
.title {
  font-size: 22px;
  font-weight: 700;
  letter-spacing: -0.02em;
  text-align: center;
}
.close-btn {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 18px;
  cursor: pointer;
  flex: 1;
  font-family: inherit;
  max-width: 44px;
}
.scroll {
  position: relative;
  z-index: 1;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  padding-bottom: calc(120px + var(--safe-bottom, 0px));
}
.lede {
  font-size: clamp(14px, 4.1vw, 16px);
  margin: 0 clamp(18px, 5.6vw, 22px) 16px;
  line-height: 1.45;
}
.items {
  padding: 0 clamp(12px, 4vw, 16px);
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.item {
  border-radius: 22px;
  padding: 14px 16px;
  display: grid;
  grid-template-columns: 68px 1fr;
  gap: 14px;
}
.item-tile {
  width: 68px;
  height: 68px;
}
.item-name {
  font-size: clamp(16px, 4.6vw, 18px);
  font-weight: 700;
  letter-spacing: -0.01em;
  line-height: 1.2;
  margin-bottom: 4px;
}
.ai-pill {
  display: inline-block;
  font-size: 13px;
  font-weight: 700;
  padding: 3px 10px;
  border-radius: 999px;
  margin-bottom: 6px;
  letter-spacing: 0.02em;
}
.item-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 8px;
}
.item-price {
  font-size: clamp(19px, 5.6vw, 22px);
  font-weight: 700;
}
.item-price-unit {
  font-size: 14px;
  font-weight: 500;
}
.stepper {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
}
.stepper-btn {
  width: 40px;
  height: 40px;
  display: grid;
  place-items: center;
  font-size: 22px;
  background: none;
  border: none;
  cursor: pointer;
  font-family: inherit;
}
.qty {
  width: 28px;
  text-align: center;
  font-size: 18px;
  font-weight: 700;
}
.summary-wrap {
  padding: 16px clamp(16px, 5.6vw, 22px) 0;
}
.summary {
  border-radius: 20px;
  padding: 16px 18px;
}
.row {
  display: flex;
  justify-content: space-between;
  font-size: 16px;
  margin-bottom: 8px;
}
.row + .row {
  margin-bottom: 12px;
}
.total {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  padding-top: 12px;
}
.total-label {
  font-size: 18px;
  font-weight: 600;
}
.total-value {
  font-size: clamp(26px, 8.2vw, 32px);
  font-weight: 800;
  letter-spacing: -0.03em;
}
.total-unit {
  font-size: 16px;
  font-weight: 600;
}
.error {
  margin: 12px 22px 0;
  padding: 12px 14px;
  border-radius: 14px;
  font-size: 15px;
  font-weight: 600;
  text-align: center;
}
.cta {
  position: absolute;
  z-index: 2;
  bottom: calc(16px + var(--safe-bottom, 0px));
  left: clamp(14px, 4.6vw, 18px);
  right: clamp(14px, 4.6vw, 18px);
}
</style>

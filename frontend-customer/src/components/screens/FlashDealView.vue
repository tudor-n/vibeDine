<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { toneFromKey } from '@/design/tokens'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { apiClient } from '@/services/api-client'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'
import FoodTile from '@/components/ui/FoodTile.vue'

const store = useFlashDealStore()
const { tokens: T, theme } = useTheme()
const { t } = useLocale()

const emit = defineEmits<{
  (e: 'close'): void
}>()

const remaining = ref(0)
const totalSeconds = ref(0)
let timer: ReturnType<typeof setInterval> | null = null

const isClaiming = ref(false)
const claimError = ref<string | null>(null)
const claimSuccess = ref(false)

function tick() {
  const deal = store.activeDeal
  if (!deal) return
  const expires = new Date(deal.expiresAt).getTime()
  const diff = Math.max(0, Math.floor((expires - Date.now()) / 1000))
  remaining.value = diff
  if (diff <= 0) {
    if (timer) clearInterval(timer)
    timer = null
    store.clearDeal()
  }
}

function startTimer() {
  if (timer) clearInterval(timer)
  const deal = store.activeDeal
  if (!deal) return
  totalSeconds.value = Math.max(deal.countdownSeconds || 0, 1)
  tick()
  timer = setInterval(tick, 1000)
}

watch(
  () => store.activeDeal?.id,
  (id) => {
    claimError.value = null
    claimSuccess.value = false
    if (id) startTimer()
    else if (timer) {
      clearInterval(timer)
      timer = null
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  if (timer) clearInterval(timer)
})

const minutes = computed(() =>
  Math.floor(remaining.value / 60).toString().padStart(2, '0'),
)
const seconds = computed(() =>
  Math.floor(remaining.value % 60).toString().padStart(2, '0'),
)
const progressPct = computed(() => {
  if (!totalSeconds.value) return 0
  return Math.max(0, Math.min(100, (remaining.value / totalSeconds.value) * 100))
})

const discountPercent = computed(() => store.activeDeal?.discountPercent ?? 0)
const itemTone = computed(() => {
  const id = store.activeDeal?.menuItem.id ?? 'flash'
  return toneFromKey(id)
})

// One discounted portion per guest whose cart is on this bill: 1 for a solo /
// pay-separately order, N for a bill that merged N guests.
const quantity = computed(() => Math.max(1, store.activeDealQuantity ?? 1))
const isMulti = computed(() => quantity.value > 1)
const unitNow = computed(() => store.activeDeal?.discountedPrice ?? 0)
const unitWas = computed(() => store.activeDeal?.menuItem.price ?? 0)
// The discount is baked into the unit price, so the headline total is simply
// unit × guests — e.g. a 20-lei item at 20% off, for 3 guests, shows 3 × 16 = 48
// (equivalently, 20% off the 60-lei full total). round2 guards float drift.
const round2 = (n: number) => Math.round(n * 100) / 100
const totalNow = computed(() => round2(unitNow.value * quantity.value))
const totalWas = computed(() => round2(unitWas.value * quantity.value))

async function claim() {
  const deal = store.activeDeal
  const orderId = store.activeDealOrderId
  if (!deal || !orderId) {
    claimError.value = t('flash.errorInvalidSession')
    return
  }
  try {
    isClaiming.value = true
    claimError.value = null
    await apiClient.claimFlashDeal(orderId, deal.id)
    claimSuccess.value = true
    setTimeout(() => store.clearDeal(), 1200)
  } catch (e) {
    claimError.value = e instanceof Error ? e.message : t('flash.errorClaim')
  } finally {
    isClaiming.value = false
  }
}

// Dismiss only collapses the modal back to the floating icon — the deal stays
// active until it's claimed or expires server-side. Clearing the store here
// would also drop the icon, which is not what the user asked for.
function dismiss() {
  emit('close')
}
</script>

<template>
  <PhoneScreen v-if="store.activeDeal" :theme="theme">
    <!-- Lime-dominant blooms per the design (the deal screen is the only
         lime-led surface in the app). -->
    <div
      class="bloom bloom-lime"
      :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
    />
    <div
      class="bloom bloom-lime-soft"
      :style="{ background: `radial-gradient(circle, #9DC41A 0%, transparent 70%)` }"
    />

    <div class="header">
      <div
        class="badge"
        :style="{ background: T.lime, color: T.onLime }"
      >
        <span class="dot" :style="{ background: T.onLime }" />
        {{ t('flash.badge') }}
      </div>
      <button
        class="dismiss"
        type="button"
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink70 }"
        :aria-label="t('flash.dismiss')"
        @click="dismiss"
      >✕</button>
    </div>

    <div class="scroll">
    <div class="hero">
      <h1 class="title">
        {{ t('flash.title1') }}<br /><span
          class="hero-mark"
          :style="{ background: T.lime, color: T.onLime }"
        >{{ t('flash.titleMark') }}</span><br />{{ t('flash.title3') }}
      </h1>
      <p class="lede" :style="{ color: T.ink70 }">
        {{ store.activeDeal.reason || t('flash.defaultReason') }}
      </p>

      <div
        class="deal-card"
        :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
      >
        <FoodTile
          :tone="itemTone"
          :image="store.activeDeal.menuItem.imageUrl"
          :radius="16"
          class="deal-tile"
        />
        <div class="deal-body">
          <div
            v-if="discountPercent"
            class="discount"
            :style="{ background: T.lime, color: T.onLime }"
          >{{ t('flash.discount', { percent: discountPercent }) }}</div>
          <div class="deal-name">
            {{ store.activeDeal.menuItem.name }}
            <span
              v-if="isMulti"
              class="qty-chip"
              :style="{ background: T.ink, color: T.paper }"
            >× {{ quantity }}</span>
          </div>
          <div class="deal-meta" :style="{ color: T.ink70 }">
            {{ isMulti ? t('flash.forGuests', { count: quantity }) : t('flash.meta') }}
          </div>
          <div class="prices">
            <!-- Dark green per the handoff (#3E7B10) on warm; on dark theme
                 we keep the brand lime for contrast. Prices are the bill total
                 (unit × guests); for a solo order that's just the unit price. -->
            <span
              class="price-now"
              :style="{ color: theme === 'dark' ? T.lime : '#3E7B10' }"
            >
              {{ totalNow }}
              <span class="price-unit" :style="{ color: T.ink70 }">{{ t('common.ron') }}</span>
            </span>
            <span
              v-if="totalWas !== totalNow"
              class="price-was"
              :style="{ color: T.ink40 }"
            >{{ totalWas }} {{ t('common.ron') }}</span>
          </div>
          <div
            v-if="isMulti"
            class="unit-note"
            :style="{ color: T.ink70 }"
          >{{ quantity }} × {{ unitNow }} {{ t('common.ron') }}</div>
        </div>
      </div>

      <div class="countdown" :style="{ background: T.card, border: `1px solid ${T.ink12}` }">
        <div class="countdown-label" :style="{ color: T.ink70 }">{{ t('flash.timeLeft') }}</div>
        <div class="countdown-row">
          <span class="num">{{ minutes }}</span>
          <span class="colon" :style="{ color: theme === 'dark' ? T.lime : '#3E7B10' }">:</span>
          <span class="num">{{ seconds }}</span>
          <span class="unit" :style="{ color: T.ink70 }">{{ t('flash.minutes') }}</span>
        </div>
        <div class="bar" :style="{ background: T.paper, border: `1px solid ${T.ink12}` }">
          <div
            class="fill"
            :style="{
              width: `${progressPct}%`,
              background: `linear-gradient(90deg, ${T.lime}, #9DC41A)`,
            }"
          />
        </div>
      </div>
    </div>
    </div>

    <div class="cta">
      <!-- Lime CTA with an ink arrow circle filled lime, per the handoff. -->
      <BigButton
        :theme="theme"
        :bg="T.lime"
        :color="T.onLime"
        :arrow-bg="T.onLime"
        :arrow-color="T.lime"
        :disabled="isClaiming || claimSuccess"
        @click="claim"
      >
        <span>{{
          claimSuccess
            ? t('flash.added')
            : isClaiming
              ? t('flash.adding')
              : t('flash.addToOrder')
        }}</span>
      </BigButton>
      <button
        class="reject"
        type="button"
        :style="{ color: T.ink70 }"
        @click="dismiss"
      >{{ t('flash.noThanks') }}</button>
      <p v-if="claimError" class="claim-error" :style="{ color: T.accentDk }">{{ claimError }}</p>
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
.bloom-lime {
  top: -60px;
  right: -80px;
  width: 320px;
  height: 320px;
  filter: blur(50px);
  opacity: 0.6;
}
.bloom-lime-soft {
  bottom: 200px;
  left: -80px;
  width: 260px;
  height: 260px;
  filter: blur(55px);
  opacity: 0.35;
}
.header {
  position: relative;
  z-index: 1;
  padding: 10px 24px 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.badge {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  border-radius: 999px;
  font-size: 15px;
  font-weight: 700;
}
.dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  display: inline-block;
}
.dismiss {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 18px;
  cursor: pointer;
  font-family: inherit;
}
.scroll {
  position: relative;
  z-index: 1;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  padding-bottom: calc(160px + var(--safe-bottom, 0px));
}
.hero {
  position: relative;
  padding: clamp(18px, 3.5vh, 28px) clamp(18px, 6vw, 24px) 0;
}
.title {
  font-weight: 700;
  font-size: clamp(33px, 11.8vw, 46px);
  line-height: 0.95;
  letter-spacing: -0.03em;
  margin: 0 0 16px;
}
.hero-mark {
  display: inline-block;
  border-radius: 8px;
  padding: 0 10px 2px;
}
.lede {
  font-size: clamp(15px, 4.6vw, 18px);
  line-height: 1.55;
  margin: 0 0 22px;
}
.deal-card {
  border-radius: 24px;
  padding: clamp(12px, 4vw, 16px) clamp(14px, 4.6vw, 18px);
  margin-bottom: 16px;
  display: grid;
  grid-template-columns: clamp(78px, 23vw, 96px) 1fr;
  gap: clamp(12px, 4vw, 16px);
}
.deal-tile {
  width: clamp(78px, 23vw, 96px);
  height: clamp(92px, 27vw, 110px);
}
.deal-body {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
}
.discount {
  display: inline-block;
  padding: 4px 12px;
  border-radius: 999px;
  font-size: 14px;
  font-weight: 800;
  margin-bottom: 8px;
  align-self: flex-start;
}
.deal-name {
  font-size: 20px;
  font-weight: 700;
  line-height: 1.2;
}
.qty-chip {
  display: inline-block;
  margin-left: 8px;
  padding: 1px 10px;
  border-radius: 999px;
  font-size: 14px;
  font-weight: 800;
  vertical-align: middle;
}
.deal-meta {
  font-size: 14px;
  margin-top: 4px;
}
.unit-note {
  font-size: 13px;
  font-weight: 600;
  margin-top: 6px;
}
.prices {
  display: flex;
  align-items: baseline;
  gap: 12px;
  margin-top: 10px;
}
.price-now {
  font-size: clamp(26px, 8.2vw, 32px);
  font-weight: 800;
}
.price-unit {
  font-size: 16px;
  font-weight: 600;
}
.price-was {
  font-size: 16px;
  text-decoration: line-through;
}
.countdown {
  border-radius: 22px;
  padding: 16px 18px;
}
.countdown-label {
  font-size: 15px;
  font-weight: 600;
  margin-bottom: 8px;
}
.countdown-row {
  display: flex;
  align-items: baseline;
  gap: 4px;
}
.num {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: clamp(40px, 14.3vw, 56px);
  font-weight: 600;
  letter-spacing: -0.04em;
  line-height: 1;
}
.colon {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: clamp(40px, 14.3vw, 56px);
  line-height: 1;
}
.unit {
  font-size: 16px;
  margin-left: 8px;
  font-weight: 500;
}
.bar {
  height: 8px;
  margin-top: 14px;
  border-radius: 999px;
  overflow: hidden;
}
.fill {
  height: 100%;
  border-radius: 999px;
  transition: width 1s linear;
}
.cta {
  position: absolute;
  z-index: 2;
  bottom: calc(16px + var(--safe-bottom, 0px));
  left: clamp(14px, 4.6vw, 18px);
  right: clamp(14px, 4.6vw, 18px);
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.reject {
  border: none;
  background: none;
  text-align: center;
  font-size: 15px;
  font-weight: 500;
  cursor: pointer;
  padding: 12px;
  font-family: inherit;
}
.claim-error {
  margin: 0;
  text-align: center;
  font-size: 14px;
  font-weight: 600;
}
</style>

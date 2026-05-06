<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { toneFromKey } from '@/design/tokens'
import { useTheme } from '@/composables/useTheme'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { apiClient } from '@/services/api-client'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'
import FoodTile from '@/components/ui/FoodTile.vue'

const store = useFlashDealStore()
const { tokens: T, theme } = useTheme()

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

async function claim() {
  const deal = store.activeDeal
  const orderId = store.activeDealOrderId
  if (!deal || !orderId) {
    claimError.value = 'Sesiune invalidă. Reîncărcați pagina.'
    return
  }
  try {
    isClaiming.value = true
    claimError.value = null
    await apiClient.claimFlashDeal(orderId, deal.id)
    claimSuccess.value = true
    setTimeout(() => store.clearDeal(), 1200)
  } catch (e) {
    claimError.value = e instanceof Error ? e.message : 'Eroare la revendicare.'
  } finally {
    isClaiming.value = false
  }
}

function dismiss() {
  store.clearDeal()
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
        :style="{ background: T.lime, color: '#000' }"
      >
        <span class="dot" :style="{ background: '#000' }" />
        Ofertă specială
      </div>
      <button
        class="dismiss"
        type="button"
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink70 }"
        aria-label="Închide oferta"
        @click="dismiss"
      >✕</button>
    </div>

    <div class="scroll">
    <div class="hero">
      <h1 class="title">
        Ofertă<br /><span
          class="hero-mark"
          :style="{ background: T.lime, color: '#000' }"
        >limitată</span><br />de chef.
      </h1>
      <p class="lede" :style="{ color: T.ink70 }">
        {{ store.activeDeal.reason || 'O porție specială la preț redus, doar câteva minute.' }}
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
            :style="{ background: T.lime, color: T.ink }"
          >−{{ discountPercent }}% reducere</div>
          <div class="deal-name">{{ store.activeDeal.menuItem.name }}</div>
          <div class="deal-meta" :style="{ color: T.ink70 }">Chef's cut · porție limitată</div>
          <div class="prices">
            <!-- Dark green per the handoff (#3E7B10) on warm; on dark theme
                 we keep the brand lime for contrast. -->
            <span
              class="price-now"
              :style="{ color: theme === 'dark' ? T.lime : '#3E7B10' }"
            >
              {{ store.activeDeal.discountedPrice }}
              <span class="price-unit" :style="{ color: T.ink70 }">RON</span>
            </span>
            <span
              v-if="store.activeDeal.menuItem.price !== store.activeDeal.discountedPrice"
              class="price-was"
              :style="{ color: T.ink40 }"
            >{{ store.activeDeal.menuItem.price }} RON</span>
          </div>
        </div>
      </div>

      <div class="countdown" :style="{ background: T.card, border: `1px solid ${T.ink12}` }">
        <div class="countdown-label" :style="{ color: T.ink70 }">Timp rămas</div>
        <div class="countdown-row">
          <span class="num">{{ minutes }}</span>
          <span class="colon" :style="{ color: theme === 'dark' ? T.lime : '#3E7B10' }">:</span>
          <span class="num">{{ seconds }}</span>
          <span class="unit" :style="{ color: T.ink70 }">minute</span>
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
        :color="T.ink"
        :arrow-bg="T.ink"
        :arrow-color="T.lime"
        :disabled="isClaiming || claimSuccess"
        @click="claim"
      >
        <span>{{
          claimSuccess
            ? 'Adăugat ✓'
            : isClaiming
              ? 'Se adaugă...'
              : 'Adaugă în comandă'
        }}</span>
      </BigButton>
      <button
        class="reject"
        type="button"
        :style="{ color: T.ink70 }"
        @click="dismiss"
      >Nu mulțumesc</button>
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
  padding-bottom: 170px;
}
.hero {
  position: relative;
  padding: 28px 24px 0;
}
.title {
  font-weight: 700;
  font-size: 46px;
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
  font-size: 18px;
  line-height: 1.55;
  margin: 0 0 22px;
}
.deal-card {
  border-radius: 24px;
  padding: 16px 18px;
  margin-bottom: 16px;
  display: grid;
  grid-template-columns: 96px 1fr;
  gap: 16px;
}
.deal-tile {
  width: 96px;
  height: 110px;
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
.deal-meta {
  font-size: 14px;
  margin-top: 4px;
}
.prices {
  display: flex;
  align-items: baseline;
  gap: 12px;
  margin-top: 10px;
}
.price-now {
  font-size: 32px;
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
  font-size: 56px;
  font-weight: 600;
  letter-spacing: -0.04em;
  line-height: 1;
}
.colon {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 56px;
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
  bottom: 28px;
  left: 18px;
  right: 18px;
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

<script setup lang="ts">
import { computed } from 'vue'
import { useSessionStore } from '@/stores/useSessionStore'
import { useTheme } from '@/composables/useTheme'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'
import FoodTile from '@/components/ui/FoodTile.vue'

const emit = defineEmits<{ (e: 'open-menu'): void }>()

const sessionStore = useSessionStore()
const { tokens: T, theme } = useTheme()

const chefCardStyle = computed(() => {
  if (theme.value === 'dark') {
    return {
      background: T.value.paperHi,
      color: T.value.ink,
      border: `1px solid ${T.value.ink12}`,
    }
  }
  return {
    background: T.value.ink,
    color: T.value.paperHi,
    border: 'none',
  }
})

const chefMetaColor = computed(() =>
  theme.value === 'dark' ? T.value.ink70 : 'rgba(251,247,236,0.65)',
)

const tableLabel = computed(() => {
  const n = sessionStore.tableNumber
  return n ? `Masa ${n}` : 'Masa'
})

const orgEyebrow = computed(() => sessionStore.organizationName || 'Bun venit')

const locationName = computed(() => sessionStore.restaurantName || 'Restaurant')

const clockLabel = computed(() => {
  const d = new Date()
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
})

const orderShortId = computed(() => {
  const id = sessionStore.orderId
  if (!id) return '#----'
  return `#${id.slice(-4).toUpperCase()}`
})

const infoCells = computed<[string, string][]>(() => [
  ['Masă', sessionStore.tableNumber || '—'],
  ['Locație', locationName.value],
  ['Adresă', sessionStore.restaurantAddress || sessionStore.organizationName || '—'],
  ['Comanda', orderShortId.value],
])
</script>

<template>
  <PhoneScreen :theme="theme">
    <div
      class="halo halo-accent"
      :style="{ background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)` }"
    />
    <div
      class="halo halo-lime"
      :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
    />

    <div class="status-row">
      <div class="status-table" :style="{ color: T.ink70 }">
        <span
          class="dot"
          :style="{ background: T.accent, boxShadow: `0 0 8px ${T.accent}` }"
        />
        {{ tableLabel }}
      </div>
      <div class="clock" :style="{ color: T.ink70 }">{{ clockLabel }}</div>
    </div>

    <div class="scroll">
    <div class="hero">
      <div class="eyebrow" :style="{ color: T.accent }">{{ orgEyebrow }}</div>
      <h1 class="title">
        Bun venit la<br />
        <span :style="{ color: T.accent }">{{ locationName }}.</span>
      </h1>
      <p class="lede" :style="{ color: T.ink70 }">
        Meniuri pentru {{ tableLabel.toLowerCase() }}. Comandați direct din aplicație — bucătăria pregătește imediat.
      </p>

      <div
        class="info-grid"
        :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
      >
        <div v-for="[k, v] in infoCells" :key="k">
          <div class="info-label" :style="{ color: T.ink40 }">{{ k }}</div>
          <div class="info-value" :style="{ color: T.ink }">{{ v }}</div>
        </div>
      </div>

      <div class="chefs-pick" :style="chefCardStyle">
        <FoodTile :tone="theme === 'dark' ? 'pink' : 'terra'" label="AZI" :radius="14" class="chefs-tile" />
        <div style="flex:1">
          <div class="pick-eyebrow" :style="{ color: T.lime }">RECOMANDAREA ZILEI</div>
          <div class="pick-name">Mușchi de vită Oltenia</div>
          <div class="pick-meta" :style="{ color: chefMetaColor }">Maturat 28 zile · 128 RON</div>
        </div>
      </div>
    </div>
    </div>

    <div class="cta">
      <BigButton :theme="theme" @click="emit('open-menu')">Deschide meniul</BigButton>
    </div>
  </PhoneScreen>
</template>

<style scoped>
.halo {
  position: absolute;
  border-radius: 50%;
  pointer-events: none;
}
.halo-accent {
  top: -60px;
  right: -80px;
  width: 300px;
  height: 300px;
  filter: blur(40px);
  opacity: 0.45;
}
.halo-lime {
  top: 200px;
  left: -80px;
  width: 240px;
  height: 240px;
  filter: blur(50px);
  opacity: 0.3;
}
.status-row {
  position: relative;
  padding: 12px 24px 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.status-table {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  font-weight: 600;
}
.dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  display: inline-block;
}
.clock {
  font-size: 14px;
  font-weight: 500;
}
.scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-bottom: 130px;
  -webkit-overflow-scrolling: touch;
}
.hero {
  position: relative;
  padding: 52px 24px 0;
}
.eyebrow {
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  margin-bottom: 14px;
}
.title {
  font-weight: 600;
  font-size: 60px;
  line-height: 0.92;
  letter-spacing: -0.04em;
  margin: 0 0 24px;
}
.lede {
  font-size: 18px;
  line-height: 1.55;
  margin: 0 0 32px;
}
.info-grid {
  border-radius: 20px;
  padding: 16px 20px;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px 0;
  margin-bottom: 24px;
}
.info-label {
  font-size: 12px;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  margin-bottom: 2px;
}
.info-value {
  font-size: 17px;
  font-weight: 600;
}
.chefs-pick {
  border-radius: 22px;
  padding: 18px 20px;
  display: flex;
  gap: 16px;
  align-items: center;
  box-shadow: 0 16px 36px -16px rgba(21, 20, 15, 0.45);
}
.chefs-tile {
  width: 72px;
  height: 88px;
  flex-shrink: 0;
}
.pick-eyebrow {
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.06em;
  margin-bottom: 6px;
}
.pick-name {
  font-size: 18px;
  font-weight: 600;
  line-height: 1.2;
  margin-bottom: 4px;
}
.pick-meta {
  font-size: 15px;
}
.cta {
  position: absolute;
  bottom: 32px;
  left: 20px;
  right: 20px;
}
</style>

<script setup lang="ts">
import { computed, ref, onMounted, watch } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useSessionStore } from '@/stores/useSessionStore'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'

const props = defineProps<{ total: number }>()
const emit = defineEmits<{
  (e: 'order-again'): void
}>()

const sessionStore = useSessionStore()
const orderStatusStore = useOrderStatusStore()
const { tokens: T, theme } = useTheme()
const { t } = useLocale()

const totalCardStyle = computed(() => {
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

const totalSubtleColor = computed(() =>
  theme.value === 'dark' ? T.value.ink70 : 'rgba(251,247,236,0.55)',
)
const totalUnitColor = computed(() =>
  theme.value === 'dark' ? T.value.ink70 : 'rgba(251,247,236,0.6)',
)
const totalHintColor = computed(() =>
  theme.value === 'dark' ? T.value.ink40 : 'rgba(251,247,236,0.45)',
)

const submittedAt = ref(new Date())
const inPrepAt = ref<Date | null>(null)
const servedAt = ref<Date | null>(null)

onMounted(() => {
  submittedAt.value = new Date()
})

function fmt(d: Date) {
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
}

const tableLabel = computed(() => {
  const id = sessionStore.tableId
  if (!id) return t('common.table')
  const tail = id.slice(-2).replace(/^0+/, '') || id.slice(-2)
  return `${t('common.table')} ${tail}`
})

// Live status arrives via SignalR. We only stamp a timestamp on the *first*
// transition into each stage so the displayed time matches when the kitchen
// actually moved the order, not when the customer re-rendered.
const currentStatus = computed(() => orderStatusStore.status)
const isPreparing = computed(() =>
  currentStatus.value === 'preparing' || currentStatus.value === 'ready' || currentStatus.value === 'completed',
)
const isServed = computed(() => currentStatus.value === 'completed')

if (isPreparing.value && !inPrepAt.value) inPrepAt.value = new Date()
if (isServed.value && !servedAt.value) servedAt.value = new Date()

watch(currentStatus, (s) => {
  if ((s === 'preparing' || s === 'ready' || s === 'completed') && !inPrepAt.value) {
    inPrepAt.value = new Date()
  }
  if (s === 'completed' && !servedAt.value) {
    servedAt.value = new Date()
  }
})

const steps = computed(() => {
  const submitted = submittedAt.value
  const inPrep = inPrepAt.value
  const served = servedAt.value

  return [
    {
      t: fmt(submitted),
      label: t('sent.stepSubmitted'),
      done: true,
      active: !isPreparing.value,
    },
    {
      t: inPrep ? fmt(inPrep) : '—',
      label: t('sent.stepPreparing'),
      done: isPreparing.value,
      active: isPreparing.value && !isServed.value,
    },
    {
      t: served ? fmt(served) : '—',
      label: t('sent.stepServed'),
      done: isServed.value,
      active: isServed.value,
    },
  ]
})
</script>

<template>
  <PhoneScreen :theme="theme">
    <div
      class="bloom bloom-lime"
      :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
    />
    <div
      class="bloom bloom-accent"
      :style="{ background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)` }"
    />

    <div class="header">
      <div class="status-table" :style="{ color: T.ink70 }">{{ tableLabel }}</div>
      <div
        class="confirm-pill"
        :style="{ background: T.lime, color: '#000' }"
      >
        {{ t('sent.confirmed') }}
      </div>
    </div>

    <div class="scroll">
    <div class="hero">
      <h1 class="title">
        {{ t('sent.title1') }}<br /><span :style="{ color: T.accent }">{{ t('sent.title2') }}</span><br />{{ t('sent.title3') }}
      </h1>
      <p class="lede" :style="{ color: T.ink70 }">
        {{ t('sent.lede') }}
      </p>

      <div
        class="timeline"
        :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
      >
        <div
          v-for="(s, i) in steps"
          :key="i"
          class="step"
          :style="{ borderBottom: i < steps.length - 1 ? `1px solid ${T.ink06}` : 'none' }"
        >
          <span
            class="dot"
            :style="{
              background: s.done ? (s.active ? T.accent : T.ink) : 'transparent',
              border: s.done ? 'none' : `2px solid ${T.ink40}`,
              boxShadow: s.active ? `0 0 12px ${T.accent}` : 'none',
            }"
          />
          <div class="step-body">
            <div
              class="step-label"
              :style="{
                color: s.done ? T.ink : T.ink40,
                fontWeight: s.active ? 700 : 500,
              }"
            >{{ s.label }}</div>
            <div class="step-time" :style="{ color: T.ink40 }">{{ s.t }}</div>
          </div>
          <div
            v-if="s.active"
            class="now-pill"
            :style="{
              color: T.accent,
              background: `${T.accent}1A`,
            }"
          >{{ t('sent.now') }}</div>
        </div>
      </div>

      <div class="total" :style="totalCardStyle">
        <div class="total-label" :style="{ color: totalSubtleColor }">{{ t('sent.totalLabel') }}</div>
        <div class="total-value">
          {{ props.total }}
          <span class="total-unit" :style="{ color: totalUnitColor }">{{ t('common.ron') }}</span>
        </div>
        <div class="total-hint" :style="{ color: totalHintColor }">{{ t('sent.payHint') }}</div>
      </div>
    </div>
    </div>

    <div class="cta">
      <BigButton :theme="theme" :font-size="16" @click="emit('order-again')">{{ t('sent.orderAgain') }}</BigButton>
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
  top: 0;
  right: -80px;
  width: 280px;
  height: 280px;
  filter: blur(50px);
  opacity: 0.35;
}
.bloom-accent {
  bottom: 100px;
  left: -60px;
  width: 200px;
  height: 200px;
  filter: blur(40px);
  opacity: 0.18;
}
.header {
  position: relative;
  z-index: 1;
  padding: 10px 24px 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.status-table {
  font-size: 16px;
  font-weight: 500;
}
.confirm-pill {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border-radius: 999px;
  font-size: 16px;
  font-weight: 700;
}
.scroll {
  position: relative;
  z-index: 1;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  padding-bottom: 130px;
}
.hero {
  position: relative;
  padding: 32px 24px 0;
}
.title {
  font-weight: 700;
  font-size: 48px;
  line-height: 0.95;
  letter-spacing: -0.03em;
  margin: 0 0 16px;
}
.lede {
  font-size: 18px;
  line-height: 1.55;
  margin: 0 0 22px;
}
.timeline {
  border-radius: 22px;
  padding: 6px 18px;
  margin-bottom: 16px;
}
.step {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 18px 0;
}
.dot {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  flex-shrink: 0;
}
.step-body {
  flex: 1;
}
.step-label {
  font-size: 18px;
  line-height: 1.2;
}
.step-time {
  font-size: 14px;
  margin-top: 2px;
}
.now-pill {
  font-size: 13px;
  font-weight: 700;
  padding: 6px 12px;
  border-radius: 999px;
}
.total {
  border-radius: 22px;
  padding: 18px 20px;
}
.total-label {
  font-size: 15px;
  margin-bottom: 4px;
  font-weight: 500;
}
.total-value {
  font-size: 36px;
  font-weight: 800;
  letter-spacing: -0.03em;
}
.total-unit {
  font-size: 18px;
  font-weight: 600;
  margin-left: 4px;
}
.total-hint {
  font-size: 14px;
  margin-top: 8px;
}
.cta {
  position: absolute;
  z-index: 2;
  bottom: 28px;
  left: 18px;
  right: 18px;
  display: grid;
  grid-template-columns: 1fr;
  gap: 10px;
}
</style>

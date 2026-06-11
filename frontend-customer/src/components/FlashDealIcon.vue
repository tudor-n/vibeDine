<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useFlashDealStore } from '@/stores/useFlashDealStore'

const store = useFlashDealStore()
const { tokens: T } = useTheme()
const { t } = useLocale()

const emit = defineEmits<{ (e: 'open'): void }>()

// Prevent the browser from focusing the button on mousedown — that's what was
// leaving the rectangular focus outline behind after the modal closed. Click
// still fires, so the open action works as before.
function preventFocus(event: MouseEvent) {
  event.preventDefault()
}

// Safety net: if anything else assigns focus (e.g. modal close redirecting
// focus to the next interactive element), drop it on the next tick so the
// outline never has a chance to render.
function dropFocus(event: FocusEvent) {
  ;(event.currentTarget as HTMLElement | null)?.blur()
}

function handleClick() {
  emit('open')
}

const remaining = ref(0)
const totalSeconds = ref(0)
let timer: ReturnType<typeof setInterval> | null = null

function tick() {
  const deal = store.activeDeal
  if (!deal) {
    remaining.value = 0
    return
  }
  const expires = new Date(deal.expiresAt).getTime()
  const diff = Math.max(0, Math.floor((expires - Date.now()) / 1000))
  remaining.value = diff
  if (diff <= 0 && timer) {
    clearInterval(timer)
    timer = null
  }
}

watch(
  () => store.activeDeal?.id,
  (id) => {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
    if (!id) return
    totalSeconds.value = Math.max(store.activeDeal?.countdownSeconds || 0, 1)
    tick()
    timer = setInterval(tick, 1000)
  },
  { immediate: true },
)

onUnmounted(() => {
  if (timer) clearInterval(timer)
})

const minutes = computed(() =>
  Math.floor(remaining.value / 60).toString().padStart(1, '0'),
)
const seconds = computed(() =>
  Math.floor(remaining.value % 60).toString().padStart(2, '0'),
)

// SVG ring: progress shrinks as the timer runs out. Matches the button size
// so the ring sits flush against the circle edge.
const SIZE = 56
const STROKE = 4
const RADIUS = (SIZE - STROKE) / 2
const CIRCUMFERENCE = 2 * Math.PI * RADIUS
const dashOffset = computed(() => {
  if (!totalSeconds.value) return CIRCUMFERENCE
  const pct = Math.max(0, Math.min(1, remaining.value / totalSeconds.value))
  return CIRCUMFERENCE * (1 - pct)
})
</script>

<template>
  <button
    v-if="store.activeDeal"
    type="button"
    tabindex="-1"
    class="flash-icon"
    :aria-label="t('flash.iconLabel')"
    :style="{ background: T.lime, color: '#000' }"
    @mousedown="preventFocus"
    @focus="dropFocus"
    @click="handleClick"
  >
    <svg
      class="ring"
      :width="SIZE"
      :height="SIZE"
      :viewBox="`0 0 ${SIZE} ${SIZE}`"
      aria-hidden="true"
    >
      <circle
        :cx="SIZE / 2"
        :cy="SIZE / 2"
        :r="RADIUS"
        fill="none"
        stroke="rgba(0,0,0,0.18)"
        :stroke-width="STROKE"
      />
      <circle
        :cx="SIZE / 2"
        :cy="SIZE / 2"
        :r="RADIUS"
        fill="none"
        stroke="#000"
        :stroke-width="STROKE"
        stroke-linecap="round"
        :stroke-dasharray="CIRCUMFERENCE"
        :stroke-dashoffset="dashOffset"
        :transform="`rotate(-90 ${SIZE / 2} ${SIZE / 2})`"
      />
    </svg>
    <svg
      class="bolt"
      viewBox="0 0 24 24"
      width="18"
      height="18"
      aria-hidden="true"
    >
      <path d="M13 2 L4 14 h7 l-2 8 l9 -12 h-7 l2 -8 Z" fill="#000" />
    </svg>
    <span class="countdown">{{ minutes }}:{{ seconds }}</span>
  </button>
</template>

<style scoped>
.flash-icon {
  /* Floating action button — anchored bottom-right with enough clearance to
     sit clearly above the screen's CTA bar (which lives ~28-90px from bottom).
     Stays visible across every screen without colliding with header content
     or bottom buttons. */
  position: absolute;
  bottom: 120px;
  right: 16px;
  width: 56px;
  height: 56px;
  border-radius: 50%;
  border: none;
  outline: none !important;
  -webkit-tap-highlight-color: transparent;
  /* Kill the tap/selection "blue box": no long-press callout, no text selection
     of the bolt/countdown, and neutralise an inherited Tailwind ring. */
  -webkit-touch-callout: none;
  user-select: none;
  -webkit-user-select: none;
  --tw-ring-shadow: 0 0 #0000;
  --tw-ring-offset-shadow: 0 0 #0000;
  display: grid;
  place-items: center;
  cursor: pointer;
  z-index: 55;
  font-family: inherit;
  box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45);
  animation: pulse 1.6s ease-in-out infinite;
}
.flash-icon:focus,
.flash-icon:focus-visible,
.flash-icon:focus-within {
  outline: none !important;
  outline-width: 0 !important;
  outline-style: none !important;
  outline-color: transparent !important;
  box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45) !important;
}
/* Firefox draws an inner dotted border on focused buttons — kill it. */
.flash-icon::-moz-focus-inner {
  border: 0 !important;
  outline: 0 !important;
  padding: 0 !important;
}
.flash-icon:active {
  transform: scale(0.94);
}
.ring {
  position: absolute;
  inset: 0;
  transition: stroke-dashoffset 1s linear;
}
.bolt {
  position: absolute;
  top: 8px;
  pointer-events: none;
}
.countdown {
  position: absolute;
  bottom: 8px;
  font-size: 11px;
  font-weight: 700;
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  letter-spacing: -0.02em;
  pointer-events: none;
}
@keyframes pulse {
  0%,
  100% {
    box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45),
      0 0 0 0 rgba(199, 235, 67, 0.7);
  }
  50% {
    box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45),
      0 0 0 12px rgba(199, 235, 67, 0);
  }
}
</style>

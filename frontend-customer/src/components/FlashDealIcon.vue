<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useFlashDealStore } from '@/stores/useFlashDealStore'

const store = useFlashDealStore()
const { tokens: T } = useTheme()
const { t } = useLocale()

const emit = defineEmits<{ (e: 'open'): void }>()

// Mirror CallWaiterButton's focus handling: stop the browser focusing the
// button on tap so no rectangular outline lingers on touch devices.
function preventFocus(event: MouseEvent) {
  event.preventDefault()
}
function dropFocus(event: FocusEvent) {
  ;(event.currentTarget as HTMLElement | null)?.blur()
}

function handleClick() {
  emit('open')
}

const remaining = ref(0)
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

const label = computed(() => t('flash.iconLabel'))
</script>

<template>
  <button
    v-if="store.activeDeal"
    type="button"
    tabindex="-1"
    class="flash-pill"
    :aria-label="label"
    :style="{ background: T.lime, color: '#000' }"
    @mousedown="preventFocus"
    @focus="dropFocus"
    @click="handleClick"
  >
    <svg
      class="pill-ic"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      aria-hidden="true"
    >
      <path d="M13 2 L4 14 h7 l-2 8 l9 -12 h-7 l2 -8 Z" fill="currentColor" />
    </svg>
    <span class="pill-label">{{ minutes }}:{{ seconds }}</span>
  </button>
</template>

<style scoped>
/* Flash-deal pill — same shape, size and behaviour as the call-waiter pill,
   and tinted lime to keep its "limited offer" identity. Vertically centred on
   the screen header row so it sits between the back button on the left and the
   table name on the right (rather than up in the top status strip). The 74px
   top lines up the 40px pill with the 48px back button, which starts just below
   the PhoneScreen status-clear (58px) + header padding. */
.flash-pill {
  position: absolute;
  top: 74px;
  left: 50%;
  transform: translateX(-50%);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 40px;
  padding: 0 16px;
  max-width: min(44%, 160px);
  border-radius: 999px;
  border: none;
  outline: none !important;
  -webkit-tap-highlight-color: transparent;
  -webkit-touch-callout: none;
  user-select: none;
  -webkit-user-select: none;
  --tw-ring-shadow: 0 0 #0000;
  --tw-ring-offset-shadow: 0 0 #0000;
  cursor: pointer;
  z-index: 57;
  font-family: inherit;
  font-size: 14px;
  font-weight: 700;
  line-height: 1;
  white-space: nowrap;
  box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45);
  transition: opacity 0.2s ease, transform 0.15s ease;
}
.flash-pill:focus,
.flash-pill:focus-visible,
.flash-pill:focus-within {
  outline: none !important;
  outline-width: 0 !important;
  outline-style: none !important;
  outline-color: transparent !important;
  box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45) !important;
}
.flash-pill::-moz-focus-inner {
  border: 0 !important;
  outline: 0 !important;
  padding: 0 !important;
}
.flash-pill:active {
  transform: translateX(-50%) scale(0.96);
}
.pill-ic {
  flex-shrink: 0;
}
.pill-label {
  overflow: hidden;
  text-overflow: ellipsis;
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  letter-spacing: -0.02em;
}
</style>

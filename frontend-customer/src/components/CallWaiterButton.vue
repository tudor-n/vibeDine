<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useHelpCallStore, COOLDOWN_TOTAL_SECONDS } from '@/stores/useHelpCallStore'

const store = useHelpCallStore()
const { tokens: T } = useTheme()
const { t } = useLocale()

// Mirrors FlashDealIcon focus handling: prevent the lingering rectangular
// outline after tap on touch devices.
function preventFocus(event: MouseEvent) {
  event.preventDefault()
}
function dropFocus(event: FocusEvent) {
  ;(event.currentTarget as HTMLElement | null)?.blur()
}

function handleClick() {
  if (store.isCoolingDown || store.isPending) return
  store.callWaiter()
}

const SIZE = 56
const STROKE = 4
const RADIUS = (SIZE - STROKE) / 2
const CIRCUMFERENCE = 2 * Math.PI * RADIUS

const dashOffset = computed(() => {
  if (!store.isCoolingDown) return CIRCUMFERENCE
  const pct = Math.max(0, Math.min(1, store.cooldownRemaining / COOLDOWN_TOTAL_SECONDS))
  return CIRCUMFERENCE * (1 - pct)
})

const minutes = computed(() =>
  Math.floor(store.cooldownRemaining / 60).toString().padStart(1, '0'),
)
const seconds = computed(() =>
  Math.floor(store.cooldownRemaining % 60).toString().padStart(2, '0'),
)

const ariaLabel = computed(() => t('waiter.iconLabel'))

// Toast shown right after a tap so the action is unmistakable. Success and the
// rate-limited case both mean "a waiter has the alert"; only a real failure is
// styled as an error.
const toastText = computed(() =>
  store.feedback === 'error' ? t('waiter.errorGeneric') : t('waiter.onTheWay'),
)
const isErrorToast = computed(() => store.feedback === 'error')
const toastStyle = computed(() =>
  isErrorToast.value
    ? { background: T.value.accent, color: '#fff' }
    : { background: T.value.lime, color: T.value.ink },
)
</script>

<template>
  <button
    type="button"
    tabindex="-1"
    class="call-waiter-icon"
    :class="{ 'is-cooling': store.isCoolingDown, 'is-pending': store.isPending }"
    :aria-label="ariaLabel"
    :aria-disabled="store.isCoolingDown || store.isPending"
    :disabled="store.isCoolingDown || store.isPending"
    :style="{ background: T.accent, color: '#fff' }"
    @mousedown="preventFocus"
    @focus="dropFocus"
    @click="handleClick"
  >
    <svg
      v-if="store.isCoolingDown"
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
        stroke="rgba(255,255,255,0.25)"
        :stroke-width="STROKE"
      />
      <circle
        :cx="SIZE / 2"
        :cy="SIZE / 2"
        :r="RADIUS"
        fill="none"
        stroke="#fff"
        :stroke-width="STROKE"
        stroke-linecap="round"
        :stroke-dasharray="CIRCUMFERENCE"
        :stroke-dashoffset="dashOffset"
        :transform="`rotate(-90 ${SIZE / 2} ${SIZE / 2})`"
      />
    </svg>

    <svg
      v-if="!store.isPending"
      class="bell"
      viewBox="0 0 24 24"
      width="22"
      height="22"
      aria-hidden="true"
    >
      <path
        d="M12 3a5 5 0 0 0-5 5v3.5L5.3 14.4a1 1 0 0 0 .85 1.6h11.7a1 1 0 0 0 .85-1.6L17 11.5V8a5 5 0 0 0-5-5Zm-2 15a2 2 0 0 0 4 0Z"
        fill="currentColor"
      />
    </svg>
    <svg
      v-else
      class="spinner"
      viewBox="0 0 24 24"
      width="22"
      height="22"
      aria-hidden="true"
    >
      <circle
        cx="12" cy="12" r="9"
        fill="none"
        stroke="rgba(255,255,255,0.3)"
        stroke-width="3"
      />
      <path
        d="M12 3 a9 9 0 0 1 9 9"
        fill="none"
        stroke="#fff"
        stroke-width="3"
        stroke-linecap="round"
      />
    </svg>

    <span v-if="store.isCoolingDown" class="countdown">{{ minutes }}:{{ seconds }}</span>
  </button>

  <Transition name="cw-toast">
    <div
      v-if="store.feedback"
      class="cw-toast"
      :style="toastStyle"
      role="status"
      aria-live="polite"
      @click="store.clearFeedback()"
    >
      <svg
        v-if="!isErrorToast"
        viewBox="0 0 24 24"
        width="18"
        height="18"
        aria-hidden="true"
      >
        <path
          d="M20 6 9 17l-5-5"
          fill="none"
          stroke="currentColor"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
      <svg
        v-else
        viewBox="0 0 24 24"
        width="18"
        height="18"
        aria-hidden="true"
      >
        <path
          d="M12 8v5m0 3.5h.01M10.3 3.9 2.4 17.5A1.9 1.9 0 0 0 4 20.4h16a1.9 1.9 0 0 0 1.6-2.9L13.7 3.9a1.9 1.9 0 0 0-3.4 0Z"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
      <span>{{ toastText }}</span>
    </div>
  </Transition>
</template>

<style scoped>
.call-waiter-icon {
  /* Pinned bottom-left so it coexists with FlashDealIcon (bottom-right).
     Same 56px footprint and 120px vertical clearance keep them aligned. */
  position: absolute;
  bottom: 120px;
  left: 16px;
  width: 56px;
  height: 56px;
  border-radius: 50%;
  border: none;
  outline: none !important;
  -webkit-tap-highlight-color: transparent;
  /* Kill the tap/selection "blue box": stop the long-press callout and any text
     selection of the icon/countdown, and neutralise a Tailwind ring if one is
     ever inherited. The drop shadow is reasserted so focus never restyles it. */
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
  transition: opacity 0.2s ease;
}
.call-waiter-icon.is-cooling,
.call-waiter-icon.is-pending {
  cursor: default;
  animation: none;
}
.call-waiter-icon:focus,
.call-waiter-icon:focus-visible,
.call-waiter-icon:focus-within {
  outline: none !important;
  outline-width: 0 !important;
  outline-style: none !important;
  outline-color: transparent !important;
  box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45) !important;
}
.call-waiter-icon::-moz-focus-inner {
  border: 0 !important;
  outline: 0 !important;
  padding: 0 !important;
}
.call-waiter-icon:not(:disabled):active {
  transform: scale(0.94);
}
.ring {
  position: absolute;
  inset: 0;
  transition: stroke-dashoffset 1s linear;
}
.bell {
  position: absolute;
  top: 8px;
  pointer-events: none;
}
.spinner {
  position: absolute;
  top: 8px;
  pointer-events: none;
  animation: spin 0.9s linear infinite;
}
.countdown {
  position: absolute;
  bottom: 8px;
  font-size: 11px;
  font-weight: 700;
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  letter-spacing: -0.02em;
  pointer-events: none;
  color: #fff;
}
@keyframes pulse {
  0%,
  100% {
    box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45),
      0 0 0 0 rgba(255, 74, 28, 0.55);
  }
  50% {
    box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45),
      0 0 0 12px rgba(255, 74, 28, 0);
  }
}
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Confirmation toast — pinned to the top of the phone frame so the customer
   gets immediate, unmistakable feedback that the tap registered. */
.cw-toast {
  position: absolute;
  top: 16px;
  left: 50%;
  transform: translateX(-50%);
  max-width: calc(100% - 32px);
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 11px 18px;
  border-radius: 999px;
  font-size: 15px;
  font-weight: 700;
  line-height: 1.2;
  white-space: nowrap;
  z-index: 56;
  cursor: pointer;
  box-shadow: 0 12px 28px -10px rgba(21, 20, 15, 0.5);
  -webkit-tap-highlight-color: transparent;
  user-select: none;
}
.cw-toast svg {
  flex-shrink: 0;
}
.cw-toast-enter-active,
.cw-toast-leave-active {
  transition: opacity 0.25s ease, transform 0.3s cubic-bezier(0.32, 0.72, 0, 1);
}
.cw-toast-enter-from,
.cw-toast-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(-16px);
}
</style>

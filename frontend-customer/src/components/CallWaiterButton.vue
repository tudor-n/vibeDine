<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useHelpCallStore } from '@/stores/useHelpCallStore'

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

const minutes = computed(() =>
  Math.floor(store.cooldownRemaining / 60).toString().padStart(1, '0'),
)
const seconds = computed(() =>
  Math.floor(store.cooldownRemaining % 60).toString().padStart(2, '0'),
)

const label = computed(() => t('waiter.iconLabel'))

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
    : { background: T.value.lime, color: T.value.onLime },
)
</script>

<template>
  <button
    type="button"
    tabindex="-1"
    class="call-waiter-pill"
    :class="{ 'is-cooling': store.isCoolingDown, 'is-pending': store.isPending }"
    :aria-label="label"
    :aria-disabled="store.isCoolingDown || store.isPending"
    :disabled="store.isCoolingDown || store.isPending"
    :style="{ background: T.accent, color: '#fff' }"
    @mousedown="preventFocus"
    @focus="dropFocus"
    @click="handleClick"
  >
    <svg
      v-if="!store.isPending"
      class="pill-ic"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      aria-hidden="true"
    >
      <path
        d="M12 3a5 5 0 0 0-5 5v3.5L5.3 14.4a1 1 0 0 0 .85 1.6h11.7a1 1 0 0 0 .85-1.6L17 11.5V8a5 5 0 0 0-5-5Zm-2 15a2 2 0 0 0 4 0Z"
        fill="currentColor"
      />
    </svg>
    <svg
      v-else
      class="pill-ic spinner"
      viewBox="0 0 24 24"
      width="20"
      height="20"
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

    <span class="pill-label">
      <template v-if="store.isPending">{{ t('waiter.calling') }}</template>
      <template v-else-if="store.isCoolingDown">{{ minutes }}:{{ seconds }}</template>
      <template v-else>{{ label }}</template>
    </span>
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
/* Floating "call waiter" pill, pinned top-left of the phone frame. It stays
   narrow so it never underlaps the top-right theme toggle / progress button. */
.call-waiter-pill {
  position: absolute;
  top: calc(14px + var(--safe-top, 0px));
  left: calc(14px + var(--safe-left, 0px));
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 40px;
  padding: 0 16px;
  max-width: min(68%, 240px);
  border-radius: 999px;
  border: none;
  outline: none !important;
  -webkit-tap-highlight-color: transparent;
  /* Kill the tap/selection "blue box": stop the long-press callout and any text
     selection, and neutralise a Tailwind ring if one is ever inherited. */
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
.call-waiter-pill.is-cooling,
.call-waiter-pill.is-pending {
  cursor: default;
  opacity: 0.92;
}
.call-waiter-pill:focus,
.call-waiter-pill:focus-visible,
.call-waiter-pill:focus-within {
  outline: none !important;
  outline-width: 0 !important;
  outline-style: none !important;
  outline-color: transparent !important;
  box-shadow: 0 10px 24px -8px rgba(21, 20, 15, 0.45) !important;
}
.call-waiter-pill::-moz-focus-inner {
  border: 0 !important;
  outline: 0 !important;
  padding: 0 !important;
}
.call-waiter-pill:not(:disabled):active {
  transform: scale(0.96);
}
.pill-ic {
  flex-shrink: 0;
}
.pill-label {
  overflow: hidden;
  text-overflow: ellipsis;
}
.spinner {
  animation: spin 0.9s linear infinite;
}
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Confirmation toast — sits just below the pill so the customer gets immediate,
   unmistakable feedback without overlapping the call button itself. */
.cw-toast {
  position: absolute;
  top: 64px;
  left: calc(14px + var(--safe-left, 0px));
  max-width: calc(100% - 28px);
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
  transform: translateY(-16px);
}
</style>

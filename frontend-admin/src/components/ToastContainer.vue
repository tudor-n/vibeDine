<script setup lang="ts">
import { useToast, type Toast } from '@/composables/useToast'

const { toasts, dismiss } = useToast()

function variantIcon(t: Toast): string {
  switch (t.variant) {
    case 'success': return '✓'
    case 'error':   return '!'
    case 'info':    return 'i'
  }
}
</script>

<template>
  <div class="toast-stack" aria-live="polite" aria-atomic="false">
    <TransitionGroup name="toast" tag="div" class="toast-stack-inner">
      <div
        v-for="t in toasts"
        :key="t.id"
        class="toast"
        :class="`toast-${t.variant}`"
        :role="t.variant === 'error' ? 'alert' : 'status'"
      >
        <span class="toast-stripe" aria-hidden="true"></span>

        <span class="toast-icon" aria-hidden="true">{{ variantIcon(t) }}</span>

        <div class="toast-body">
          <div class="toast-title">{{ t.title }}</div>
          <p v-if="t.message" class="toast-message">{{ t.message }}</p>
        </div>

        <button
          type="button"
          class="toast-close"
          aria-label="Închide notificarea"
          @click="dismiss(t.id)"
        >
          ×
        </button>

        <span
          class="toast-progress"
          :style="{ animationDuration: `${t.duration}ms` }"
          @animationend="dismiss(t.id)"
          aria-hidden="true"
        ></span>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toast-stack {
  position: fixed;
  top: 76px;
  right: 18px;
  /* Above the floor-plan modals (z-index 80), which now teleport to <body>, so
     error toasts fired from inside a modal stay visible. */
  z-index: 100;
  pointer-events: none;
  max-width: calc(100vw - 36px);
  width: 380px;
}
.toast-stack-inner {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.toast {
  position: relative;
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: start;
  gap: 12px;
  padding: 14px 16px 16px 18px;
  background: var(--card);
  border: 1px solid var(--ink12);
  border-radius: 18px;
  box-shadow: var(--shadow-pop);
  overflow: hidden;
  pointer-events: auto;
  font-family: 'Inter Tight', -apple-system, system-ui, sans-serif;
  color: var(--ink);
}

.toast:hover .toast-progress {
  animation-play-state: paused;
}

/* Left accent stripe — variant colour */
.toast-stripe {
  position: absolute;
  top: 0;
  bottom: 0;
  left: 0;
  width: 4px;
  border-radius: 18px 0 0 18px;
  background: var(--ink12);
}
.toast-success .toast-stripe { background: var(--lime); }
.toast-error   .toast-stripe { background: var(--accent); }
.toast-info    .toast-stripe { background: var(--ink40); }

/* Icon disc */
.toast-icon {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-weight: 800;
  font-size: 14px;
  line-height: 1;
  flex-shrink: 0;
  margin-top: 2px;
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
}
.toast-success .toast-icon {
  background: var(--lime);
  color: var(--onLime);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--lime) 18%, transparent);
}
.toast-error .toast-icon {
  background: var(--accent);
  color: var(--onAccent);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 22%, transparent);
}
.toast-info .toast-icon {
  background: var(--ink);
  color: var(--paperHi);
}

.toast-body { min-width: 0; }

.toast-title {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--ink70);
  margin-bottom: 4px;
}
.toast-success .toast-title { color: var(--ink); }
.toast-error   .toast-title { color: var(--accent); }

.toast-message {
  font-size: 14px;
  line-height: 1.4;
  font-weight: 500;
  color: var(--ink);
  margin: 0;
  word-wrap: break-word;
}

/* Close button */
.toast-close {
  flex-shrink: 0;
  background: transparent;
  border: none;
  color: var(--ink40);
  font-size: 22px;
  line-height: 1;
  width: 26px;
  height: 26px;
  border-radius: 50%;
  cursor: pointer;
  display: grid;
  place-items: center;
  transition: background 0.15s ease, color 0.15s ease;
  font-family: inherit;
}
.toast-close:hover {
  background: var(--ink06);
  color: var(--ink);
}

/* Auto-dismiss progress bar */
.toast-progress {
  position: absolute;
  left: 0;
  bottom: 0;
  height: 2px;
  background: var(--ink12);
  width: 100%;
  transform-origin: left center;
  animation: toast-shrink linear forwards;
}
.toast-success .toast-progress { background: var(--lime); }
.toast-error   .toast-progress { background: var(--accent); }
.toast-info    .toast-progress { background: var(--ink40); }

@keyframes toast-shrink {
  from { transform: scaleX(1); }
  to   { transform: scaleX(0); }
}

/* Slide / fade transitions for stacking */
.toast-enter-from {
  opacity: 0;
  transform: translateX(24px) scale(0.96);
}
.toast-enter-active {
  transition: opacity 0.28s ease, transform 0.32s cubic-bezier(0.34, 1.4, 0.64, 1);
}
.toast-leave-to {
  opacity: 0;
  transform: translateX(24px) scale(0.96);
}
.toast-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
  position: absolute;
  width: 100%;
}
.toast-move {
  transition: transform 0.28s cubic-bezier(0.34, 1.4, 0.64, 1);
}

@media (max-width: 640px) {
  .toast-stack {
    top: auto;
    bottom: 16px;
    right: 16px;
    left: 16px;
    width: auto;
  }
  .toast-enter-from,
  .toast-leave-to {
    transform: translateY(20px) scale(0.96);
  }
}

@media (prefers-reduced-motion: reduce) {
  .toast-enter-active,
  .toast-leave-active,
  .toast-move {
    transition: opacity 0.15s ease;
    transform: none !important;
  }
  .toast-progress { animation-duration: 0ms; display: none; }
}
</style>

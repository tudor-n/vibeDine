<script setup lang="ts">
import { useHappyHourStore } from '@/stores/useHappyHourStore'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'

// The "return to happy hour" banner (handoff state B): a non-intrusive, always
// re-openable entry point pinned at the very top of the menu once the guest has
// dismissed the offer sheet. Tapping it reopens the sheet. Colours come from the
// app's own light/dark tokens so it matches the surrounding menu in both themes.
const hh = useHappyHourStore()
const { tokens: T } = useTheme()
const { t } = useLocale()
</script>

<template>
  <button
    v-if="hh.active && !hh.open"
    class="hh-banner"
    type="button"
    :style="{ background: T.paperHi, border: `1px solid ${T.ink12}`, color: T.ink }"
    :aria-label="t('hh.reopenAria')"
    @click="hh.reopen()"
  >
    <span class="hh-tile" :style="{ background: T.lime, color: T.ink }" aria-hidden="true">
      <svg
        viewBox="0 0 24 24" width="18" height="18" fill="none"
        stroke="currentColor" stroke-width="2"
        stroke-linecap="round" stroke-linejoin="round"
      >
        <circle cx="12" cy="12" r="9" />
        <path d="M8 14s1.5 2 4 2 4-2 4-2" />
        <path d="M9 9h.01M15 9h.01" />
      </svg>
    </span>

    <span class="hh-text">
      <span class="hh-title">{{ t('hh.reopenTitle') }}</span>
      <span class="hh-meta" :style="{ color: T.ink70 }">
        {{ hh.active.productName }} ·
        {{ t('hh.reopenMeta', { percent: hh.active.discountPercent, time: hh.active.endTime }) }}
      </span>
    </span>

    <span class="hh-chevron" :style="{ color: T.ink40 }" aria-hidden="true">
      <svg
        viewBox="0 0 24 24" width="20" height="20" fill="none"
        stroke="currentColor" stroke-width="2.2"
        stroke-linecap="round" stroke-linejoin="round"
      >
        <path d="M9 6l6 6-6 6" />
      </svg>
    </span>
  </button>
</template>

<style scoped>
.hh-banner {
  position: relative;
  z-index: 2;
  flex-shrink: 0;
  width: auto;
  display: flex;
  align-items: center;
  gap: 12px;
  border-radius: 16px;
  padding: 11px 12px;
  margin: 8px 12px 12px;
  cursor: pointer;
  font-family: inherit;
  text-align: left;
  -webkit-tap-highlight-color: transparent;
  animation: hh-drop 0.3s cubic-bezier(0.2, 0.8, 0.3, 1);
  transition: transform 0.15s ease, box-shadow 0.2s ease, border-color 0.2s ease;
}
.hh-banner:hover {
  box-shadow: 0 8px 22px -14px rgba(0, 0, 0, 0.45);
}
.hh-banner:active {
  transform: scale(0.985);
}
.hh-tile {
  flex-shrink: 0;
  width: 34px;
  height: 34px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  /* Low-key "come back" nudge — a slow, small breathe. */
  animation: hh-tile-breathe 2.6s ease-in-out infinite;
}
.hh-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
  gap: 2px;
}
.hh-title {
  font-size: 14.5px;
  font-weight: 700;
  letter-spacing: -0.01em;
}
.hh-meta {
  font-size: 12px;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.hh-chevron {
  flex-shrink: 0;
  display: grid;
  place-items: center;
  transition: transform 0.2s ease;
}
.hh-banner:hover .hh-chevron {
  transform: translateX(3px);
}

@keyframes hh-drop {
  from {
    opacity: 0;
    transform: translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
@keyframes hh-tile-breathe {
  0%,
  100% {
    transform: scale(1);
  }
  50% {
    transform: scale(1.07);
  }
}
@media (prefers-reduced-motion: reduce) {
  .hh-banner {
    animation: none;
  }
  .hh-tile {
    animation: none;
  }
  .hh-banner:hover .hh-chevron {
    transform: none;
  }
}
</style>

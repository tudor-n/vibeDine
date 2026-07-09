<script setup lang="ts">
// Always-visible cart card: Vibey's header + speech bubble + the reveal pill
// that is the ONLY UI path allowed to trigger `store.openSheet()` (the cost
// invariant — see docs/superpowers/specs/2026-07-09-vibey-recommendations-design.md).
import { computed } from 'vue'
import { useVibeyStore } from '@/stores/useVibeyStore'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { bestOn } from '@/design/contrast'
import VibeyAvatar from './VibeyAvatar.vue'

const store = useVibeyStore()
const { tokens: T } = useTheme()
const { t } = useLocale()

const onSec = computed(() => bestOn(T.value.lime))
const revealLabel = computed(() => (store.sheetOpen ? t('vibey.revealHide') : t('vibey.revealShow')))
const thinkDelays = [0, 0.16, 0.32]

function toggleSheet() {
  if (store.sheetOpen) store.closeSheet()
  else store.openSheet()
}
</script>

<template>
  <div class="vibey-block" :style="{ background: T.paperHi, border: `1px solid ${T.ink12}` }">
    <div class="vibey-header">
      <VibeyAvatar :size="44" :tokens="T" />
      <div class="vibey-header-text">
        <div class="vibey-name-row">
          <span class="vibey-name">Vibey</span>
          <span
            class="vibey-status-dot"
            :style="{ background: T.accent, boxShadow: `0 0 8px ${T.accent}` }"
          />
        </div>
        <div class="vibey-subtitle" :style="{ color: T.ink70 }">{{ t('vibey.subtitle') }}</div>
      </div>
    </div>

    <div class="vibey-bubble" :style="{ background: T.card, border: `1px solid ${T.ink12}` }">
      <div v-if="store.thinking" class="vibey-thinking">
        <span
          v-for="(delay, i) in thinkDelays"
          :key="i"
          class="vibey-think-dot"
          :style="{ background: T.accent, animationDelay: `${delay}s` }"
        />
        <span class="vibey-thinking-text" :style="{ color: T.ink40 }">{{ t('vibey.thinking') }}</span>
      </div>
      <p v-else class="vibey-msg" :style="{ color: T.ink }">{{ store.botMsg }}</p>
    </div>

    <button
      v-if="!store.allAdded && !store.noRecs"
      type="button"
      class="vibey-reveal"
      :style="{
        background: T.lime,
        color: onSec,
        boxShadow: `0 12px 26px -10px color-mix(in srgb, ${T.lime} 72%, transparent)`,
      }"
      @click="toggleSheet"
    >
      <span class="vibey-reveal-label">{{ revealLabel }}</span>
      <span
        class="vibey-caret"
        :class="{ 'vibey-caret-open': store.sheetOpen }"
        :style="{ background: onSec, color: T.lime }"
      >▾</span>
    </button>

    <div v-else-if="store.allAdded" class="vibey-all-added" :style="{ background: T.card, border: `1px dashed ${T.ink12}` }">
      <span class="vibey-all-added-check" :style="{ background: T.lime, color: onSec }">✓</span>
      <span class="vibey-all-added-text" :style="{ color: T.ink70 }">{{ t('vibey.allAdded') }}</span>
    </div>
  </div>
</template>

<style scoped>
.vibey-block {
  margin: 18px 16px 0;
  border-radius: 22px;
  padding: 16px 16px 18px;
  position: relative;
  overflow: hidden;
}
.vibey-header {
  display: flex;
  align-items: center;
  gap: 11px;
}
.vibey-header-text {
  flex: 1;
  min-width: 0;
}
.vibey-name-row {
  display: flex;
  align-items: center;
  gap: 7px;
}
.vibey-name {
  font-size: 16px;
  font-weight: 700;
}
.vibey-status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
}
.vibey-subtitle {
  font-size: 12.5px;
  margin-top: 1px;
}
.vibey-bubble {
  margin: 12px 0 0 4px;
  border-radius: 16px;
  border-top-left-radius: 5px;
  padding: 12px 15px;
  box-shadow: 0 8px 20px -16px rgba(0, 0, 0, 0.4);
  min-height: 24px;
}
.vibey-thinking {
  display: flex;
  gap: 5px;
  align-items: center;
  height: 20px;
}
.vibey-think-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  animation: vibey-think 1s ease-in-out infinite;
}
.vibey-thinking-text {
  font-size: 13px;
  margin-left: 6px;
}
.vibey-msg {
  margin: 0;
  font-size: 14.5px;
  line-height: 1.5;
}
.vibey-reveal {
  width: 100%;
  margin-top: 14px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border: none;
  border-radius: 999px;
  padding: 15px 16px 15px 22px;
  min-height: 56px;
  font-family: inherit;
  font-size: 17px;
  font-weight: 700;
  letter-spacing: -0.01em;
  cursor: pointer;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}
.vibey-reveal:active {
  transform: scale(0.985);
}
.vibey-reveal-label {
  flex: 1;
  text-align: left;
}
.vibey-caret {
  width: 38px;
  height: 38px;
  border-radius: 50%;
  flex-shrink: 0;
  display: grid;
  place-items: center;
  font-size: 18px;
  transform: none;
  animation: vibey-caretbob 1.6s ease-in-out infinite;
  transition: transform 0.28s cubic-bezier(0.34, 1.4, 0.64, 1);
}
.vibey-caret-open {
  transform: rotate(180deg);
  animation: none;
}
.vibey-all-added {
  margin-top: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 9px;
  border-radius: 16px;
  padding: 14px;
}
.vibey-all-added-check {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 13px;
  font-weight: 800;
}
.vibey-all-added-text {
  font-size: 14px;
  font-weight: 600;
}

@keyframes vibey-think {
  0%, 80%, 100% { opacity: 0.3; transform: translateY(0); }
  40% { opacity: 1; transform: translateY(-3px); }
}
@keyframes vibey-caretbob {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(3px); }
}

@media (prefers-reduced-motion: reduce) {
  .vibey-think-dot,
  .vibey-caret {
    animation: none;
  }
}
</style>

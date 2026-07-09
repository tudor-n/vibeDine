<script setup lang="ts">
// Bottom sheet showing Vibey's recommendations. Purely a renderer over the
// store: it never fetches — `store.openSheet()` (called from VibeyBlock's
// reveal pill) is the only fetch trigger, guarded there by `recsLoaded`.
import { computed } from 'vue'
import { useVibeyStore } from '@/stores/useVibeyStore'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { toneFromKey } from '@/design/tokens'
import { formatMoney } from '@/utils/money'
import FoodTile from '@/components/ui/FoodTile.vue'
import VibeyAvatar from './VibeyAvatar.vue'

const store = useVibeyStore()
const { tokens: T } = useTheme()
const { t } = useLocale()

const thinkDelays = [0, 0.16, 0.32]
// At most 2 recs shown at a time per the design handoff, even if the API
// returns 3.
const visibleRecs = computed(() => store.recs.slice(0, 2))
</script>

<template>
  <div v-if="store.sheetOpen" class="vibey-overlay">
    <div class="vibey-scrim" @click="store.closeSheet()" />
    <div class="vibey-sheet" :style="{ background: T.paperHi, color: T.ink }">
      <div class="vibey-grab-wrap">
        <span class="vibey-grab" :style="{ background: T.ink12 }" />
      </div>

      <div class="vibey-sheet-header">
        <VibeyAvatar :size="40" :tokens="T" />
        <div class="vibey-sheet-header-text">
          <div class="vibey-sheet-title">{{ t('vibey.sheetTitle') }}</div>
          <div class="vibey-sheet-subtitle" :style="{ color: T.ink70 }">{{ t('vibey.sheetSubtitle') }}</div>
        </div>
        <button
          type="button"
          class="vibey-sheet-close"
          :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink }"
          :aria-label="t('common.close')"
          @click="store.closeSheet()"
        >✕</button>
      </div>

      <div class="vibey-sheet-status">
        <div v-if="store.thinking" class="vibey-thinking">
          <span
            v-for="(delay, i) in thinkDelays"
            :key="i"
            class="vibey-think-dot"
            :style="{ background: T.accent, animationDelay: `${delay}s` }"
          />
          <span class="vibey-thinking-text" :style="{ color: T.ink40 }">{{ t('vibey.thinking') }}</span>
        </div>
        <p v-else-if="store.summary" class="vibey-summary" :style="{ color: T.ink70 }">{{ store.summary }}</p>
      </div>

      <div class="vibey-recs">
        <div
          v-for="rec in visibleRecs"
          :key="rec.id"
          class="vibey-rec-card"
          :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
        >
          <div class="vibey-rec-top">
            <FoodTile :tone="toneFromKey(rec.id)" :radius="13" class="vibey-rec-tile" />
            <div class="vibey-rec-info">
              <span
                v-if="rec.category"
                class="vibey-rec-tag"
                :style="{ color: T.ink70, background: T.ink06, fontFamily: T.mono }"
              >{{ rec.category }}</span>
              <div class="vibey-rec-name">{{ rec.name }}</div>
            </div>
            <div class="vibey-rec-side">
              <div class="vibey-rec-price">
                {{ formatMoney(rec.priceRon) }}
                <span class="vibey-rec-price-unit" :style="{ color: T.ink70 }">{{ t('common.ron') }}</span>
              </div>
              <button
                type="button"
                class="vibey-rec-add"
                :style="{
                  background: T.accent,
                  color: T.onAccent,
                  boxShadow: `0 8px 18px -12px ${T.accent}`,
                }"
                @click="store.addRec(rec.id)"
              >{{ t('vibey.add') }}</button>
            </div>
          </div>
          <div class="vibey-rec-reason" :style="{ borderTop: `1px solid ${T.ink06}` }">
            <span class="vibey-rec-glyph" :style="{ background: T.accent }">
              <span class="vibey-rec-glyph-dot" :style="{ background: T.onAccent }" />
            </span>
            <p :style="{ color: T.ink70 }"><strong :style="{ color: T.ink }">Vibey:</strong> {{ rec.reason }}</p>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.vibey-overlay {
  position: absolute;
  inset: 0;
  z-index: 20;
}
.vibey-scrim {
  position: absolute;
  inset: 0;
  background: rgba(11, 11, 13, 0.45);
  backdrop-filter: blur(3px);
  -webkit-backdrop-filter: blur(3px);
  animation: vibey-scrimin 0.22s ease both;
}
.vibey-sheet {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  display: flex;
  flex-direction: column;
  max-height: 80%;
  border-top-left-radius: 28px;
  border-top-right-radius: 28px;
  box-shadow: 0 -24px 54px -20px rgba(11, 11, 13, 0.55);
  animation: vibey-sheetup 0.36s cubic-bezier(0.22, 1, 0.36, 1) both;
}
.vibey-grab-wrap {
  padding: 12px 0 2px;
  display: flex;
  justify-content: center;
}
.vibey-grab {
  width: 42px;
  height: 5px;
  border-radius: 999px;
}
.vibey-sheet-header {
  padding: 8px 18px 10px;
  display: flex;
  align-items: center;
  gap: 11px;
}
.vibey-sheet-header-text {
  flex: 1;
  min-width: 0;
}
.vibey-sheet-title {
  font-size: 18px;
  font-weight: 700;
  line-height: 1;
  letter-spacing: -0.01em;
}
.vibey-sheet-subtitle {
  font-size: 12.5px;
  margin-top: 3px;
}
.vibey-sheet-close {
  width: 38px;
  height: 38px;
  border-radius: 50%;
  font-size: 15px;
  cursor: pointer;
  flex-shrink: 0;
  display: grid;
  place-items: center;
  font-family: inherit;
}
.vibey-sheet-status {
  padding: 0 18px 6px;
  min-height: 20px;
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
.vibey-summary {
  margin: 0;
  font-size: 14px;
  line-height: 1.5;
}
.vibey-recs {
  padding: 10px 16px calc(22px + var(--safe-bottom, 0px));
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.vibey-rec-card {
  border-radius: 18px;
  padding: 13px;
  animation: vibey-pop 0.3s ease both;
}
.vibey-rec-top {
  display: grid;
  grid-template-columns: 56px 1fr auto;
  gap: 12px;
  align-items: flex-start;
}
.vibey-rec-tile {
  width: 56px;
  height: 56px;
}
.vibey-rec-info {
  min-width: 0;
}
.vibey-rec-tag {
  display: inline-block;
  font-size: 9.5px;
  font-weight: 700;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  padding: 2px 8px;
  border-radius: 999px;
}
.vibey-rec-name {
  font-size: 17px;
  font-weight: 700;
  letter-spacing: -0.01em;
  margin-top: 5px;
  line-height: 1.15;
}
.vibey-rec-side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
}
.vibey-rec-price {
  font-size: 16px;
  font-weight: 700;
  white-space: nowrap;
}
.vibey-rec-price-unit {
  font-size: 11px;
  font-weight: 500;
}
.vibey-rec-add {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  border: none;
  border-radius: 999px;
  padding: 9px 15px;
  font-family: inherit;
  font-size: 13.5px;
  font-weight: 700;
  cursor: pointer;
  white-space: nowrap;
}
.vibey-rec-reason {
  display: flex;
  gap: 9px;
  align-items: flex-start;
  margin-top: 11px;
  padding-top: 11px;
}
.vibey-rec-reason p {
  margin: 0;
  font-size: 13.5px;
  line-height: 1.5;
}
.vibey-rec-glyph {
  width: 19px;
  height: 19px;
  border-radius: 6px;
  flex-shrink: 0;
  display: grid;
  place-items: center;
}
.vibey-rec-glyph-dot {
  width: 3.5px;
  height: 3.5px;
  border-radius: 50%;
}

@keyframes vibey-think {
  0%, 80%, 100% { opacity: 0.3; transform: translateY(0); }
  40% { opacity: 1; transform: translateY(-3px); }
}
@keyframes vibey-scrimin {
  from { opacity: 0; }
  to { opacity: 1; }
}
@keyframes vibey-sheetup {
  from { transform: translateY(100%); }
  to { transform: translateY(0); }
}
@keyframes vibey-pop {
  from { opacity: 0; transform: translateY(8px) scale(0.97); }
  to { opacity: 1; transform: none; }
}

@media (prefers-reduced-motion: reduce) {
  .vibey-scrim,
  .vibey-sheet,
  .vibey-rec-card,
  .vibey-think-dot {
    animation: none;
  }
}
</style>

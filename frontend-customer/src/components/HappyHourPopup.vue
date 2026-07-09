<script setup lang="ts">
import { computed } from 'vue'
import { useHappyHourStore } from '@/stores/useHappyHourStore'
import { useCartStore } from '@/stores/useCartStore'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { FOOD_PALETTES, toneFromKey } from '@/design/tokens'
import type { Product } from '@shared/types/models'

// The happy-hour offer sheet (handoff state A) and the "added" toast (state C).
// Recreated with the app's own theme tokens rather than the handoff's fixed
// palette, so it reads correctly in both the light and dark menu themes. The
// caller gates `enabled` to the browsing screens; visibility of the sheet vs.
// toast is driven by the shared store so it survives navigating between screens.
// The sheet is anchored to the bottom of the phone and slides up.
const props = defineProps<{ enabled: boolean }>()

const hh = useHappyHourStore()
const cart = useCartStore()
const { tokens: T, theme } = useTheme()
const { t } = useLocale()

const showSheet = computed(() => props.enabled && hh.open && Boolean(hh.active))
const showToast = computed(() => props.enabled && hh.added && Boolean(hh.active))

const countdown = computed(() => {
  const total = Math.max(0, hh.secs)
  const h = Math.floor(total / 3600)
  const mm = String(Math.floor((total % 3600) / 60)).padStart(2, '0')
  const ss = String(total % 60).padStart(2, '0')
  // Only surface the hours segment once the window is an hour or more out, so a
  // long window reads "1:20:20" instead of "80:20"; shorter ones stay "MM:SS".
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`
})
// A gentle urgency pulse on the timer in the final minute — subtle, not flashy.
const urgent = computed(() => hh.secs > 0 && hh.secs <= 60)

// A stable gradient stands in for the product photo (no image on the deal), tinted
// from the app's food palette by product id — same trick the menu tiles use.
const ringStyle = computed(() => {
  const deal = hh.active
  if (!deal) return {}
  const p = FOOD_PALETTES[toneFromKey(deal.productId)]
  return {
    background: `radial-gradient(circle at 38% 30%, ${p.b}, ${p.a})`,
    border: `2px solid ${T.value.lime}`,
  }
})

// Modal scrim: a translucent wash over the menu, darker in dark mode.
const scrimColor = computed(() =>
  theme.value === 'dark' ? 'rgba(7, 6, 5, 0.72)' : 'rgba(28, 18, 12, 0.42)',
)

function addToCart() {
  const deal = hh.active
  if (!deal) return
  // Store the FULL price: the cart applies the live happy-hour discount reactively
  // via effectivePrice(), so storing the already-discounted price here would
  // double-discount the line. This mirrors how the menu adds the same product.
  const product: Product = {
    id: deal.productId,
    categoryId: '',
    name: deal.productName,
    description: '',
    price: deal.originalPrice,
    imageUrl: null,
    isAvailable: true,
  }
  cart.addItem(product)
  hh.notifyAdded()
  // Collapse to the reopen banner so the guest returns to browsing; they can pop
  // the offer open again any time during the window.
  hh.dismiss()
}
</script>

<template>
  <!-- State A: offer sheet, pinned to the bottom of the phone -->
  <Transition name="hh-scrim">
    <div
      v-if="showSheet"
      class="hh-scrim"
      :style="{ background: scrimColor }"
      @click.self="hh.dismiss()"
    >
      <Transition name="hh-sheet" appear>
        <div
          v-if="showSheet && hh.active"
          class="hh-sheet"
          :style="{ background: T.card, borderTop: `1px solid ${T.ink12}`, color: T.ink }"
          role="dialog"
          aria-modal="true"
        >
          <div class="hh-grab" :style="{ background: T.ink12 }" aria-hidden="true" />

          <button
            class="hh-close"
            type="button"
            :style="{ background: T.paperHi, color: T.ink70 }"
            :aria-label="t('hh.dismissAria')"
            @click="hh.dismiss()"
          >
            <svg
              viewBox="0 0 24 24" width="16" height="16" fill="none"
              stroke="currentColor" stroke-width="2.6"
              stroke-linecap="round" stroke-linejoin="round"
            >
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>

          <!-- Eyebrow: badge chip + time window -->
          <div class="hh-eyebrow">
            <span class="hh-chip" :style="{ background: T.lime, color: T.ink }">
              <svg
                viewBox="0 0 24 24" width="14" height="14" fill="none"
                stroke="currentColor" stroke-width="2"
                stroke-linecap="round" stroke-linejoin="round"
              >
                <circle cx="12" cy="12" r="9" />
                <path d="M8 14s1.5 2 4 2 4-2 4-2" />
                <path d="M9 9h.01M15 9h.01" />
              </svg>
              {{ t('hh.badge') }}
            </span>
            <span class="hh-time" :style="{ color: T.ink70 }">
              <svg
                viewBox="0 0 24 24" width="14" height="14" fill="none"
                stroke="currentColor" stroke-width="2"
                stroke-linecap="round" stroke-linejoin="round"
              >
                <circle cx="12" cy="12" r="9" />
                <path d="M12 7v5l3 2" />
              </svg>
              {{ hh.active.startTime }} – {{ hh.active.endTime }}
            </span>
          </div>

          <!-- Hero: product placeholder + title -->
          <div class="hh-hero">
            <div class="hh-ring" :style="ringStyle" aria-hidden="true" />
            <div class="hh-hero-text">
              <div class="hh-title" :style="{ color: T.ink }">{{ hh.active.productName }}</div>
              <div class="hh-sub" :style="{ color: T.ink70 }">{{ t('hh.popupSubtitle') }}</div>
            </div>
          </div>

          <!-- Price + countdown -->
          <div class="hh-priceRow" :style="{ borderTop: `1px solid ${T.ink12}` }">
            <div class="hh-price">
              <span class="hh-now">{{ hh.active.discountedPrice }} {{ t('common.ron') }}</span>
              <span class="hh-was" :style="{ color: T.ink40 }">{{ hh.active.originalPrice }}</span>
              <span class="hh-tag" :style="{ background: T.accent, color: T.onAccent }"
                >−{{ hh.active.discountPercent }}%</span
              >
            </div>
            <div class="hh-count">
              <span class="hh-endsLabel" :style="{ color: T.ink70 }">{{ t('hh.endsIn') }}</span>
              <span
                class="hh-timer"
                :class="{ urgent }"
                :style="{ color: T.accent }"
                >{{ countdown }}</span
              >
            </div>
          </div>

          <!-- Primary CTA -->
          <button
            class="hh-cta"
            type="button"
            :style="{ background: T.accent, color: T.onAccent }"
            @click="addToCart"
          >
            <svg
              viewBox="0 0 24 24" width="20" height="20" fill="none"
              stroke="currentColor" stroke-width="2"
              stroke-linecap="round" stroke-linejoin="round"
            >
              <circle cx="9" cy="20" r="1.4" />
              <circle cx="18" cy="20" r="1.4" />
              <path d="M2 3h3l2.4 12.5a1.5 1.5 0 0 0 1.5 1.2h8.2a1.5 1.5 0 0 0 1.5-1.2L21.5 7H6" />
            </svg>
            {{ t('hh.addFor') }} {{ hh.active.discountedPrice }} {{ t('common.ron') }}
          </button>

          <!-- Secondary -->
          <button
            class="hh-later"
            type="button"
            :style="{ color: T.ink70 }"
            @click="hh.dismiss()"
          >
            {{ t('hh.maybeLater') }}
          </button>
        </div>
      </Transition>
    </div>
  </Transition>

  <!-- State C: confirmation toast (fixed success colour in both themes) -->
  <Transition name="hh-toast-t">
    <div v-if="showToast && hh.active" class="hh-toast" role="status">
      <svg
        viewBox="0 0 24 24" width="18" height="18" fill="none"
        stroke="#8ff0b4" stroke-width="2.4"
        stroke-linecap="round" stroke-linejoin="round"
      >
        <path d="M20 6L9 17l-5-5" />
      </svg>
      {{ t('hh.toastAdded', { product: hh.active.productName }) }}
    </div>
  </Transition>
</template>

<style scoped>
.hh-scrim {
  position: absolute;
  inset: 0;
  z-index: 55;
  backdrop-filter: blur(2px);
  -webkit-backdrop-filter: blur(2px);
}
.hh-sheet {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  border-radius: 26px 26px 0 0;
  padding: 10px 22px calc(20px + var(--safe-bottom, 0px));
  box-shadow: 0 -18px 50px rgba(0, 0, 0, 0.35);
}
.hh-grab {
  width: 40px;
  height: 4px;
  border-radius: 999px;
  margin: 0 auto 12px;
}
.hh-close {
  position: absolute;
  top: 16px;
  right: 16px;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 50%;
  display: grid;
  place-items: center;
  cursor: pointer;
  font-family: inherit;
  transition: transform 0.15s ease;
}
.hh-close:active {
  transform: scale(0.9);
}
.hh-eyebrow {
  display: flex;
  align-items: center;
  gap: 9px;
  padding-right: 40px;
}
.hh-chip {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0.08em;
  padding: 7px 12px 7px 9px;
  border-radius: 999px;
}
.hh-time {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 13px;
  font-weight: 600;
}
.hh-hero {
  display: flex;
  align-items: center;
  gap: 18px;
  margin-top: 18px;
}
.hh-ring {
  flex-shrink: 0;
  width: 92px;
  height: 92px;
  border-radius: 50%;
}
.hh-hero-text {
  min-width: 0;
}
.hh-title {
  font-size: 26px;
  font-weight: 800;
  letter-spacing: -0.02em;
  line-height: 1.05;
}
.hh-sub {
  font-size: 13.5px;
  line-height: 1.35;
  margin-top: 6px;
}
.hh-priceRow {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
  margin-top: 20px;
  padding-top: 16px;
}
.hh-price {
  display: flex;
  align-items: baseline;
  gap: 10px;
  flex-wrap: wrap;
}
.hh-now {
  font-size: 29px;
  font-weight: 800;
  letter-spacing: -0.03em;
  white-space: nowrap;
}
.hh-was {
  font-size: 16px;
  font-weight: 600;
  text-decoration: line-through;
}
.hh-tag {
  font-size: 12px;
  font-weight: 800;
  padding: 3px 8px;
  border-radius: 20px;
}
.hh-count {
  text-align: right;
  flex-shrink: 0;
}
.hh-endsLabel {
  display: block;
  font-size: 10.5px;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}
.hh-timer {
  font-size: 19px;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.hh-timer.urgent {
  animation: hh-timer-pulse 1s ease-in-out infinite;
}
.hh-cta {
  width: 100%;
  margin-top: 18px;
  border: none;
  border-radius: 16px;
  padding: 16px;
  font-size: 16.5px;
  font-weight: 800;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 9px;
  font-family: inherit;
  transition: transform 0.15s ease, filter 0.15s ease;
}
.hh-cta:hover {
  filter: brightness(1.06);
}
.hh-cta:active {
  transform: scale(0.985);
}
.hh-cta svg {
  transition: transform 0.2s ease;
}
.hh-cta:hover svg {
  transform: translateX(2px);
}
.hh-later {
  width: 100%;
  margin-top: 6px;
  border: none;
  background: transparent;
  padding: 12px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  font-family: inherit;
}
.hh-toast {
  position: absolute;
  left: 18px;
  right: 18px;
  bottom: calc(26px + var(--safe-bottom, 0px));
  z-index: 65;
  display: flex;
  align-items: center;
  gap: 10px;
  background: #1f6b3a;
  color: #eafff1;
  border-radius: 16px;
  padding: 15px 18px;
  font-size: 15px;
  font-weight: 700;
  box-shadow: 0 16px 40px rgba(0, 0, 0, 0.35);
}

/* hhScrim — scrim fade */
.hh-scrim-enter-active,
.hh-scrim-leave-active {
  transition: opacity 0.32s ease;
}
.hh-scrim-enter-from,
.hh-scrim-leave-to {
  opacity: 0;
}

/* Sheet — slide up from the bottom edge */
.hh-sheet-enter-active {
  transition: transform 0.42s cubic-bezier(0.2, 0.8, 0.3, 1), opacity 0.3s ease;
}
.hh-sheet-leave-active {
  transition: transform 0.28s ease, opacity 0.28s ease;
}
.hh-sheet-enter-from,
.hh-sheet-leave-to {
  transform: translateY(100%);
  opacity: 0.6;
}

/* Toast — slide up + fade */
.hh-toast-t-enter-active {
  transition: transform 0.35s cubic-bezier(0.2, 0.8, 0.3, 1), opacity 0.35s ease;
}
.hh-toast-t-leave-active {
  transition: transform 0.25s ease, opacity 0.25s ease;
}
.hh-toast-t-enter-from,
.hh-toast-t-leave-to {
  transform: translateY(16px);
  opacity: 0;
}

@keyframes hh-timer-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.5;
  }
}
@media (prefers-reduced-motion: reduce) {
  .hh-sheet-enter-active,
  .hh-sheet-leave-active,
  .hh-scrim-enter-active,
  .hh-scrim-leave-active,
  .hh-toast-t-enter-active,
  .hh-toast-t-leave-active {
    transition: opacity 0.2s ease;
  }
  .hh-sheet-enter-from,
  .hh-sheet-leave-to {
    transform: none;
  }
  .hh-timer.urgent {
    animation: none;
  }
}
</style>

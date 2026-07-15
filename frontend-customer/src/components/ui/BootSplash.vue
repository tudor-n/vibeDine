<script setup lang="ts">
import { BLOB_PATHS } from '@/design/tokens'
import { useLocale } from '@/composables/useLocale'

// Shown only while the restaurant's palette is still unknown (a first-ever scan,
// before /join answers). It is therefore deliberately NOT themed: reading `tokens`
// here would paint the default light palette — precisely the wrong-theme flash this
// screen exists to hide. The values below are vibeDine's own dark-mode tokens
// (TD.paper / TD.ink from design/tokens.ts), hard-coded so no restaurant's brand,
// and no theme toggle, can reach them.
const BG = '#1B1814'
const INK = '#F5F0E8'

// The same pebble the whole app is built from (categories, lockup, admin logo).
// Monochrome here: with no accent to lean on, the mark is the identity.
const PEBBLE_PATH = BLOB_PATHS[0]

const { t } = useLocale()
</script>

<template>
  <div class="splash" :style="{ background: BG, color: INK }" role="status" :aria-label="t('splash.loading')">
    <div class="mark">
      <!-- Sized so the blob's irregularity actually reads: below ~90px it
           flattens into a generic loading dot and stops being the app's mark. -->
      <svg class="pebble" viewBox="0 0 100 100" width="104" height="104" aria-hidden="true">
        <path :d="PEBBLE_PATH" :fill="INK" />
      </svg>
      <!-- Colour normally separates "vibe" from "Dine"; with the palette unknown,
           weight and opacity carry that split instead. -->
      <span class="wordmark">
        <span class="wordmark-vibe">vibe</span><span class="wordmark-dine">Dine</span>
      </span>
    </div>
  </div>
</template>

<style scoped>
.splash {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  /* Above every floating control in OrderingView (max 70): the splash is a cover,
     and anything showing through it defeats the point. Controls that could appear
     this early are also v-if'd off while booting, so nothing sits in the tab order
     behind it — this is the backstop for anything added later. */
  z-index: 90;
}

.mark {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 18px;
}

/* Breathing stands in for a spinner: the pebble is already the app's motif, so the
   wait reads as vibeDine rather than as generic chrome. */
.pebble {
  animation: breathe 1.9s ease-in-out infinite;
}

.wordmark {
  font-family: 'Inter Tight', -apple-system, system-ui, sans-serif;
  font-size: 24px;
  line-height: 1;
  letter-spacing: -0.03em;
  user-select: none;
}
.wordmark-vibe {
  font-weight: 400;
  opacity: 0.62;
}
.wordmark-dine {
  font-weight: 600;
}

@keyframes breathe {
  0%, 100% { transform: scale(1); opacity: 0.92; }
  50%      { transform: scale(1.06); opacity: 1; }
}

@media (prefers-reduced-motion: reduce) {
  .pebble {
    animation: none;
  }
}
</style>

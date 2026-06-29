<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'
import { useHappyHourStore } from '@/stores/useHappyHourStore'
import { useCartStore } from '@/stores/useCartStore'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import type { Product } from '@shared/types/models'

const hh = useHappyHourStore()
const cart = useCartStore()
const { tokens: T } = useTheme()
const { t } = useLocale()

// Brief "Added ✓" confirmation after tapping add, then back to the normal label.
const justAdded = ref(false)
let addedTimer: ReturnType<typeof setTimeout> | null = null

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
  justAdded.value = true
  if (addedTimer) clearTimeout(addedTimer)
  addedTimer = setTimeout(() => {
    justAdded.value = false
  }, 1600)
}

onBeforeUnmount(() => {
  if (addedTimer) clearTimeout(addedTimer)
})
</script>

<template>
  <div
    v-if="hh.active"
    class="hh-banner"
    :style="{ background: T.lime, color: T.ink, border: `1px solid ${T.ink12}` }"
    role="status"
  >
    <div class="hh-top">
      <span class="hh-badge" :style="{ background: T.ink, color: T.lime }">{{ t('hh.badge') }}</span>
      <div class="hh-prices">
        <span class="hh-now">{{ hh.active.discountedPrice }} {{ t('common.ron') }}</span>
        <span class="hh-was" :style="{ color: T.ink40 }">{{ hh.active.originalPrice }}</span>
      </div>
    </div>
    <div class="hh-bottom">
      <div class="hh-text">
        <span class="hh-name">{{ hh.active.productName }}</span>
        <span class="hh-meta" :style="{ color: T.ink70 }">
          −{{ hh.active.discountPercent }}% · {{ hh.active.startTime }}–{{ hh.active.endTime }}
        </span>
      </div>
      <button
        class="hh-add"
        type="button"
        :style="{ background: T.accent, color: T.onAccent }"
        :aria-label="t('hh.addAria')"
        @click="addToCart"
      >
        <template v-if="justAdded">{{ t('hh.added') }}</template>
        <template v-else><span class="plus">+</span> {{ t('hh.add') }}</template>
      </button>
    </div>
  </div>
</template>

<style scoped>
.hh-banner {
  display: flex;
  flex-direction: column;
  gap: 10px;
  border-radius: 18px;
  padding: 12px 14px;
  margin: 0 0 14px;
}
.hh-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.hh-badge {
  flex-shrink: 0;
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.06em;
  padding: 5px 9px;
  border-radius: 999px;
}
.hh-prices {
  display: flex;
  align-items: baseline;
  gap: 8px;
  flex-shrink: 0;
}
.hh-now {
  font-size: 18px;
  font-weight: 800;
  white-space: nowrap;
}
.hh-was {
  font-size: 13px;
  text-decoration: line-through;
}
.hh-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.hh-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.hh-name {
  font-size: 15px;
  font-weight: 700;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.hh-meta {
  font-size: 12.5px;
  font-weight: 600;
}
.hh-add {
  flex-shrink: 0;
  border: none;
  height: 40px;
  padding: 0 16px;
  border-radius: 999px;
  font-size: 15px;
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 6px;
  font-family: inherit;
  white-space: nowrap;
}
.plus {
  font-size: 20px;
  line-height: 1;
}
</style>

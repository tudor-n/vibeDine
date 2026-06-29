<script setup lang="ts">
import { useHappyHourStore } from '@/stores/useHappyHourStore'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'

const hh = useHappyHourStore()
const { tokens: T } = useTheme()
const { t } = useLocale()
</script>

<template>
  <div
    v-if="hh.active"
    class="hh-banner"
    :style="{ background: T.lime, color: T.ink, border: `1px solid ${T.ink12}` }"
    role="status"
  >
    <div class="hh-main">
      <span class="hh-badge" :style="{ background: T.ink, color: T.lime }">HAPPY HOUR</span>
      <div class="hh-text">
        <span class="hh-name">{{ hh.active.productName }}</span>
        <span class="hh-meta" :style="{ color: T.ink70 }">
          −{{ hh.active.discountPercent }}% · {{ hh.active.startTime }}–{{ hh.active.endTime }}
        </span>
      </div>
    </div>
    <div class="hh-prices">
      <span class="hh-now">{{ hh.active.discountedPrice }} {{ t('common.ron') }}</span>
      <span class="hh-was" :style="{ color: T.ink40 }">{{ hh.active.originalPrice }}</span>
    </div>
  </div>
</template>

<style scoped>
.hh-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-radius: 18px;
  padding: 12px 14px;
  margin: 0 0 14px;
}
.hh-main {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
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
</style>

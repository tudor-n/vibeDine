<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useGuestStore } from '@/stores/useGuestStore'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'

const emit = defineEmits<{ (e: 'order-again'): void }>()

const guestStore = useGuestStore()
const { tokens: T, theme } = useTheme()
const { t } = useLocale()

const initiatorName = computed(() => guestStore.mergedInto?.initiatorDisplayName ?? 'Guest 1')
const body = computed(() => t('merged.body', { name: initiatorName.value }))
</script>

<template>
  <PhoneScreen :theme="theme">
    <div
      class="bloom bloom-lime"
      :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
    />

    <div class="wrap">
      <div class="mark" :style="{ background: T.lime, color: T.onLime }">
        <svg viewBox="0 0 24 24" width="34" height="34" aria-hidden="true">
          <path
            d="M20 6 9 17l-5-5"
            fill="none"
            stroke="currentColor"
            stroke-width="2.5"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </div>
      <h1 class="title">{{ t('merged.title') }}</h1>
      <p class="body" :style="{ color: T.ink70 }">{{ body }}</p>
    </div>

    <div class="cta">
      <BigButton :theme="theme" :font-size="16" @click="emit('order-again')">{{ t('merged.newOrder') }}</BigButton>
    </div>
  </PhoneScreen>
</template>

<style scoped>
.bloom {
  position: absolute;
  border-radius: 50%;
  pointer-events: none;
  z-index: 0;
  top: -40px;
  right: -70px;
  width: 280px;
  height: 280px;
  filter: blur(50px);
  opacity: 0.35;
}
.wrap {
  position: relative;
  z-index: 1;
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 32px 28px;
}
.mark {
  width: 76px;
  height: 76px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  margin-bottom: 24px;
  box-shadow: 0 16px 30px -14px rgba(21, 20, 15, 0.4);
}
.title {
  font-size: clamp(25px, 7.7vw, 30px);
  font-weight: 800;
  letter-spacing: -0.02em;
  margin: 0 0 14px;
}
.body {
  font-size: clamp(15px, 4.4vw, 17px);
  line-height: 1.55;
  margin: 0;
  max-width: 300px;
}
.cta {
  position: absolute;
  z-index: 2;
  bottom: calc(16px + var(--safe-bottom, 0px));
  left: clamp(14px, 4.6vw, 18px);
  right: clamp(14px, 4.6vw, 18px);
}
</style>

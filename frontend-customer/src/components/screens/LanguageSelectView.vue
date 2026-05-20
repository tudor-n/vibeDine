<script setup lang="ts">
import { ref, computed } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale, type Locale } from '@/composables/useLocale'
import { useSessionStore } from '@/stores/useSessionStore'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'

const emit = defineEmits<{ (e: 'select', locale: Locale): void }>()

const { tokens: T, theme } = useTheme()
const { locale, setLocale, t } = useLocale()
const sessionStore = useSessionStore()

const choice = ref<Locale>(locale.value)

const locationName = computed(() => sessionStore.restaurantName || t('welcome.fallbackLocation'))

function pick(l: Locale) {
  choice.value = l
  setLocale(l)
}

function confirm() {
  setLocale(choice.value)
  emit('select', choice.value)
}

const options = computed<Array<{ key: Locale; flag: string; label: string }>>(() => [
  { key: 'ro', flag: '🇷🇴', label: t('lang.ro') },
  { key: 'en', flag: '🇬🇧', label: t('lang.en') },
])
</script>

<template>
  <PhoneScreen :theme="theme">
    <div
      class="bloom bloom-accent"
      :style="{ background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)` }"
    />
    <div
      class="bloom bloom-lime"
      :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
    />

    <div class="hero">
      <div class="eyebrow" :style="{ color: T.accent }">{{ t('lang.choose.welcome') }}</div>
      <h1 class="title">
        {{ t('welcome.line1') }}<br />
        <span :style="{ color: T.accent }">{{ locationName }}.</span>
      </h1>
      <p class="lede" :style="{ color: T.ink70 }">{{ t('lang.choose.subtitle') }}</p>
    </div>

    <div class="options">
      <button
        v-for="opt in options"
        :key="opt.key"
        type="button"
        class="opt"
        :aria-pressed="choice === opt.key"
        :style="choice === opt.key
          ? { background: T.ink, color: T.paperHi, border: `2px solid ${T.accent}` }
          : { background: T.card, color: T.ink, border: `1px solid ${T.ink12}` }"
        @click="pick(opt.key)"
      >
        <span class="flag" aria-hidden="true">{{ opt.flag }}</span>
        <span class="label">{{ opt.label }}</span>
        <span
          class="check"
          :style="{
            background: choice === opt.key ? T.accent : 'transparent',
            border: choice === opt.key ? 'none' : `2px solid ${T.ink40}`,
            color: T.onAccent,
          }"
          aria-hidden="true"
        >{{ choice === opt.key ? '✓' : '' }}</span>
      </button>
    </div>

    <div class="cta">
      <BigButton :theme="theme" @click="confirm">{{ t('lang.continue') }}</BigButton>
    </div>
  </PhoneScreen>
</template>

<style scoped>
.bloom {
  position: absolute;
  border-radius: 50%;
  pointer-events: none;
  z-index: 0;
}
.bloom-accent {
  top: -60px;
  right: -80px;
  width: 300px;
  height: 300px;
  filter: blur(40px);
  opacity: 0.4;
}
.bloom-lime {
  bottom: 180px;
  left: -80px;
  width: 240px;
  height: 240px;
  filter: blur(50px);
  opacity: 0.28;
}
.hero {
  position: relative;
  z-index: 1;
  padding: 80px 24px 28px;
}
.eyebrow {
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  margin-bottom: 12px;
}
.title {
  font-weight: 600;
  font-size: 44px;
  line-height: 1;
  letter-spacing: -0.03em;
  margin: 0 0 16px;
}
.lede {
  font-size: 17px;
  line-height: 1.5;
  margin: 0;
}
.options {
  position: relative;
  z-index: 1;
  flex: 1;
  padding: 12px 20px 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.opt {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 18px 20px;
  border-radius: 22px;
  cursor: pointer;
  font-family: inherit;
  font-size: 18px;
  font-weight: 700;
  text-align: left;
  transition: transform 0.18s ease, background 0.18s ease, color 0.18s ease;
  min-height: 72px;
}
.opt:active {
  transform: scale(0.985);
}
.flag {
  font-size: 32px;
  line-height: 1;
  flex-shrink: 0;
}
.label {
  flex: 1;
}
.check {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 16px;
  font-weight: 700;
  flex-shrink: 0;
}
.cta {
  position: relative;
  z-index: 1;
  padding: 16px 20px 32px;
}
</style>

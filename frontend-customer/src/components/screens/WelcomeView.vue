<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale, type Locale } from '@/composables/useLocale'
import { useSessionStore } from '@/stores/useSessionStore'
import { resolveMediaUrl } from '@/services/mediaUrl'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import BigButton from '@/components/ui/BigButton.vue'
import VibeDineLockup from '@/components/ui/VibeDineLockup.vue'

defineEmits<{ (e: 'continue'): void }>()

const { tokens } = useTheme()
const { t, locale, setLocale } = useLocale()
const session = useSessionStore()

const photoUrl = computed(() => resolveMediaUrl(session.branding?.photoUrl))
const logoUrl = computed(() => resolveMediaUrl(session.branding?.logoUrl))

const bgStyle = computed(() => ({
  backgroundImage:
    `linear-gradient(180deg, rgba(0,0,0,.25), rgba(0,0,0,.28) 40%, rgba(0,0,0,.86)), url('${photoUrl.value}')`,
}))

const options = computed<Array<{ key: Locale; flag: string; label: string }>>(() => [
  { key: 'ro', flag: '🇷🇴', label: t('lang.ro') },
  { key: 'en', flag: '🇬🇧', label: t('lang.en') },
])
</script>

<template>
  <PhoneScreen flush :bg="'#000'">
    <div class="welcome" :style="bgStyle">
      <div class="center">
        <div class="eyebrow" :style="{ fontFamily: tokens.mono }">{{ t('welcome.line1') }}</div>

        <div v-if="logoUrl" class="logo-plate">
          <img :src="logoUrl" :alt="session.restaurantName || ''" class="logo-img" />
        </div>

        <VibeDineLockup class="lockup" on-dark />
      </div>

      <div class="bottom">
        <BigButton :bg="tokens.accent" :color="tokens.onAccent" @click="$emit('continue')">
          {{ t('welcome.cta') }}
        </BigButton>

        <div class="lang-switch" role="group" :aria-label="`${t('lang.ro')} / ${t('lang.en')}`">
          <button
            v-for="opt in options"
            :key="opt.key"
            type="button"
            class="lang-pill"
            :aria-pressed="locale === opt.key"
            :style="locale === opt.key
              ? { borderColor: tokens.accent, color: '#fff', background: 'rgba(255,255,255,.16)' }
              : { borderColor: 'rgba(255,255,255,.32)', color: 'rgba(255,255,255,.72)', background: 'rgba(255,255,255,.06)' }"
            @click="setLocale(opt.key)"
          >
            <span aria-hidden="true">{{ opt.flag }}</span>
            <span>{{ opt.label }}</span>
          </button>
        </div>
      </div>
    </div>
  </PhoneScreen>
</template>

<style scoped>
.welcome {
  position: relative;
  flex: 1;
  height: 100%;
  display: flex;
  flex-direction: column;
  background-size: cover;
  background-position: center;
}

.center {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 26px;
  padding: 40px 28px 0;
}

.eyebrow {
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.24em;
  text-transform: uppercase;
  color: rgba(255, 255, 255, 0.92);
}

.logo-plate {
  background: rgba(255, 255, 255, 0.95);
  border-radius: 26px;
  padding: 30px 34px;
  box-shadow: 0 30px 60px -30px rgba(0, 0, 0, 0.7);
  backdrop-filter: blur(10px);
  display: grid;
  place-items: center;
  max-width: 260px;
}

.logo-img {
  max-height: 110px;
  max-width: 100%;
  object-fit: contain;
}

.bottom {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 22px 24px calc(28px + var(--safe-bottom, 0px));
}

.lang-switch {
  display: flex;
  justify-content: center;
  gap: 10px;
}

.lang-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 40px;
  padding: 8px 18px;
  border-radius: 999px;
  border-width: 2px;
  border-style: solid;
  font-family: inherit;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: transform 0.18s ease, background 0.18s ease, border-color 0.18s ease;
}
.lang-pill:active {
  transform: scale(0.96);
}
</style>

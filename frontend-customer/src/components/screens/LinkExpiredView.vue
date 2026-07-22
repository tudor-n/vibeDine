<script setup lang="ts">
// A full-screen, top-layer overlay shown once the diner's device-session link
// has expired (see useLinkExpiry). Deliberately offers no button: the only
// way forward is re-scanning the table QR, which is a fresh page load — a
// fresh timer and a restored session. Styled after the invalid-QR overlay in
// OrderingView.vue so an expired link reads as the same family of message.
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'

const { tokens: T } = useTheme()
const { t } = useLocale()
</script>

<template>
  <div class="link-expired" :style="{ background: T.paper, color: T.ink }" role="alert">
    <div class="link-expired-card" :style="{ background: T.card, border: `1px solid ${T.ink12}` }">
      <div class="link-expired-mark" :style="{ color: T.accent }">⏱</div>
      <h1 class="link-expired-title">{{ t('expired.title') }}</h1>
      <p class="link-expired-text" :style="{ color: T.ink70 }">{{ t('expired.message') }}</p>
    </div>
  </div>
</template>

<style scoped>
.link-expired {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 28px;
  /* Above every other layer in OrderingView (splash 90, flash-deal overlay 50,
     join toast 70, theme/progress toggles 60) — expiry must cover the whole
     app tree, including any modal that happened to be open. */
  z-index: 100;
}
.link-expired-card {
  width: 100%;
  max-width: 320px;
  border-radius: 28px;
  padding: 32px 26px;
  text-align: center;
  box-shadow: 0 20px 40px -22px rgba(21, 20, 15, 0.25);
}
.link-expired-mark {
  font-size: 40px;
  line-height: 1;
  margin-bottom: 14px;
}
.link-expired-title {
  font-size: 22px;
  font-weight: 700;
  margin-bottom: 10px;
}
.link-expired-text {
  font-size: 15px;
  line-height: 1.5;
}
</style>

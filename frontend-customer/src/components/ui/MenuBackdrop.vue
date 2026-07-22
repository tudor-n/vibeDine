<script setup lang="ts">
import { computed } from 'vue'
import { useSessionStore } from '@/stores/useSessionStore'
import { useTheme } from '@/composables/useTheme'
import { resolveMediaUrl } from '@/services/mediaUrl'
import { menuBackgroundScrimOpacity } from '@shared/branding/menuBackground'

// Full-bleed branded background for the ordering screens (categories + menu),
// the counterpart to the welcome screen's presentation photo. Renders nothing
// unless the restaurant has uploaded a menu background.
const session = useSessionStore()
const { tokens: T } = useTheme()

// The manager chooses how prominently the photo shows (visible/medium/subtle);
// that maps to the opacity of the paper wash painted over it.
const scrimOpacity = computed(() => menuBackgroundScrimOpacity(session.branding?.menuBackgroundStyle))

// Sanitize the branding URL before dropping it into a CSS url(): allow only
// absolute http(s) or same-origin ('/…') URLs, then percent-encode so a stray
// quote/paren/newline can't terminate the quoted url('…') context. The value is
// the tenant's own branding, so this is defense-in-depth. Mirrors WelcomeView.
const safeUrl = computed(() => {
  const raw = resolveMediaUrl(session.branding?.menuBackgroundUrl)
  if (!raw) return ''
  const isHttp = /^https?:\/\//i.test(raw)
  const isRelative = raw.startsWith('/') && !raw.startsWith('//')
  if (!isHttp && !isRelative) return ''
  return encodeURI(raw).replace(/['()]/g, (c) => `%${c.charCodeAt(0).toString(16)}`)
})
</script>

<template>
  <div v-if="safeUrl" class="menu-backdrop" aria-hidden="true">
    <div class="menu-backdrop-img" :style="{ backgroundImage: `url('${safeUrl}')` }" />
    <!-- Paper-tinted wash so the diner's dark menu text and cards stay legible
         over any photo. Element opacity works whether the (possibly branded)
         paper token is a hex or an rgba value; the manager controls how strong
         it is via the menu-background visibility setting. -->
    <div class="menu-backdrop-scrim" :style="{ background: T.paper, opacity: scrimOpacity }" />
  </div>
</template>

<style scoped>
.menu-backdrop {
  position: absolute;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  overflow: hidden;
}
.menu-backdrop-img {
  position: absolute;
  inset: 0;
  background-size: cover;
  background-position: center;
}
.menu-backdrop-scrim {
  position: absolute;
  inset: 0;
  /* opacity is set inline from the restaurant's visibility setting */
}
</style>

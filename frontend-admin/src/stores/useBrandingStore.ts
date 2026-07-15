import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { brandingApi } from '@/services/api-client'
import type { RestaurantBranding } from '@shared/types/models'

// Uploads always overwrite a stable filename (logo.png / photo.jpg), so the
// backend returns the SAME path when an image is replaced. An unchanged <img src>
// never re-fetches, leaving the old image on screen until a manual refresh. Append
// a per-upload token so the URL changes and the browser reloads the new bytes.
// This token is display-only: it's stripped for the filename label and is never
// sent back on save(), so the DB keeps the clean path.
let uploadSeq = 0
function cacheBust(url: string): string {
  const sep = url.includes('?') ? '&' : '?'
  return `${url}${sep}v=${Date.now()}-${++uploadSeq}`
}

export const useBrandingStore = defineStore('branding', () => {
  const branding = ref<RestaurantBranding | null>(null)
  const isEnabled = computed(() => !!branding.value?.enabled)

  async function load() {
    branding.value = await brandingApi.get()
  }

  async function save(draft: {
    enabled: boolean
    primary: string | null
    secondary: string | null
    background: string | null
    presetKey: string | null
    logoBackground: string | null
    logoIsDark: boolean | null
  }) {
    branding.value = await brandingApi.update(draft)
    return branding.value
  }

  async function uploadLogo(file: File) {
    const res = await brandingApi.uploadLogo(file)
    const url = res.logoUrl ? cacheBust(res.logoUrl) : null
    if (branding.value && url) branding.value.logoUrl = url
    return url
  }

  async function uploadPhoto(file: File) {
    const res = await brandingApi.uploadPhoto(file)
    const url = res.photoUrl ? cacheBust(res.photoUrl) : null
    if (branding.value && url) branding.value.photoUrl = url
    return url
  }

  async function disable() {
    const b = branding.value
    return save({
      enabled: false,
      primary: b?.primary ?? null,
      secondary: b?.secondary ?? null,
      background: b?.background ?? null,
      presetKey: b?.presetKey ?? null,
      logoBackground: b?.logoBackground ?? null,
      logoIsDark: b?.logoIsDark ?? null,
    })
  }

  return { branding, isEnabled, load, save, uploadLogo, uploadPhoto, disable }
})

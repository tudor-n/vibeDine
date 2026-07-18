import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { brandingApi } from '@/services/api-client'
import type { BrandingAssetKind } from '@/services/brandingUpload'
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
    menuBackgroundStyle: string | null
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

  async function uploadMenuBackground(file: File) {
    const res = await brandingApi.uploadMenuBackground(file)
    const url = res.menuBackgroundUrl ? cacheBust(res.menuBackgroundUrl) : null
    if (branding.value && url) branding.value.menuBackgroundUrl = url
    return url
  }

  // Delete a stored asset and clear its URL locally so the dropzone empties.
  async function removeAsset(kind: BrandingAssetKind) {
    await brandingApi.remove(kind)
    if (branding.value) {
      if (kind === 'logo') branding.value.logoUrl = null
      else if (kind === 'photo') branding.value.photoUrl = null
      else if (kind === 'menu-background') branding.value.menuBackgroundUrl = null
    }
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
      menuBackgroundStyle: b?.menuBackgroundStyle ?? null,
    })
  }

  return { branding, isEnabled, load, save, uploadLogo, uploadPhoto, uploadMenuBackground, removeAsset, disable }
})

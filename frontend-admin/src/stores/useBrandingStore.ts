import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { brandingApi } from '@/services/api-client'
import type { RestaurantBranding } from '@shared/types/models'

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
    if (branding.value && res.logoUrl) branding.value.logoUrl = res.logoUrl
    return res.logoUrl ?? null
  }

  async function uploadPhoto(file: File) {
    const res = await brandingApi.uploadPhoto(file)
    if (branding.value && res.photoUrl) branding.value.photoUrl = res.photoUrl
    return res.photoUrl ?? null
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

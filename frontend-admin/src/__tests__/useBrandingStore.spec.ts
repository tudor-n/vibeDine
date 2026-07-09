// @vitest-environment node
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('@/services/api-client', () => ({
  brandingApi: {
    get: vi.fn(),
    update: vi.fn(),
    uploadLogo: vi.fn(),
    uploadPhoto: vi.fn(),
  },
}))

import { brandingApi } from '@/services/api-client'
import { useBrandingStore } from '@/stores/useBrandingStore'

describe('useBrandingStore', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('loads branding and exposes isEnabled', async () => {
    ;(brandingApi.get as any).mockResolvedValue({
      enabled: true, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: 'supporter',
    })
    const store = useBrandingStore()
    await store.load()
    expect(store.isEnabled).toBe(true)
    expect(store.branding?.primary).toBe('#E51F4E')
  })

  it('disable() saves enabled:false', async () => {
    ;(brandingApi.get as any).mockResolvedValue({
      enabled: true, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: null,
    })
    ;(brandingApi.update as any).mockResolvedValue({
      enabled: false, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: null,
    })
    const store = useBrandingStore()
    await store.load()
    await store.disable()
    expect(brandingApi.update).toHaveBeenCalledWith(expect.objectContaining({ enabled: false }))
    expect(store.isEnabled).toBe(false)
  })
})

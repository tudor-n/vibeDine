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

  it('uploadLogo cache-busts the URL so a replaced file (same path) still reloads', async () => {
    ;(brandingApi.get as any).mockResolvedValue({
      enabled: true, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: null,
    })
    ;(brandingApi.uploadLogo as any).mockResolvedValue({ logoUrl: '/uploads/branding/abc/logo.png' })
    const store = useBrandingStore()
    await store.load()

    const first = await store.uploadLogo({} as unknown as File)
    expect(first).toMatch(/^\/uploads\/branding\/abc\/logo\.png\?/)
    expect(store.branding?.logoUrl).toBe(first)

    // Backend returns the SAME path on replace; the store must still hand back a
    // distinct URL so the <img src> changes and the browser re-fetches.
    const second = await store.uploadLogo({} as unknown as File)
    expect(second).not.toBe(first)
  })

  it('uploadPhoto cache-busts the URL the same way', async () => {
    ;(brandingApi.get as any).mockResolvedValue({
      enabled: true, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: null,
    })
    ;(brandingApi.uploadPhoto as any).mockResolvedValue({ photoUrl: '/uploads/branding/abc/photo.jpg' })
    const store = useBrandingStore()
    await store.load()

    const first = await store.uploadPhoto({} as unknown as File)
    const second = await store.uploadPhoto({} as unknown as File)
    expect(first).toMatch(/^\/uploads\/branding\/abc\/photo\.jpg\?/)
    expect(second).not.toBe(first)
    expect(store.branding?.photoUrl).toBe(second)
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

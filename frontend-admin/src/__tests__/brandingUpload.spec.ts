import { describe, it, expect } from 'vitest'
import { assertBrandingAssetSize } from '@/services/brandingUpload'
import { BRANDING_LOGO_MAX_BYTES, BRANDING_PHOTO_MAX_BYTES } from '@shared/branding/limits'

describe('assertBrandingAssetSize', () => {
  it('rejects a logo over the cap with a clear Romanian message', () => {
    expect(() => assertBrandingAssetSize('logo', { size: BRANDING_LOGO_MAX_BYTES + 1 }))
      .toThrow(/Logo-ul depășește 5 MB/)
  })

  it('rejects a photo over the cap with a clear Romanian message', () => {
    expect(() => assertBrandingAssetSize('photo', { size: BRANDING_PHOTO_MAX_BYTES + 1 }))
      .toThrow(/Fotografia depășește 10 MB/)
  })

  it('accepts a file exactly at the cap', () => {
    expect(() => assertBrandingAssetSize('logo', { size: BRANDING_LOGO_MAX_BYTES })).not.toThrow()
    expect(() => assertBrandingAssetSize('photo', { size: BRANDING_PHOTO_MAX_BYTES })).not.toThrow()
  })
})

import { describe, it, expect } from 'vitest'
import { resolveLogoBackground } from '@shared/branding/derive'

describe('resolveLogoBackground', () => {
  it('transparent → no plate', () => {
    expect(resolveLogoBackground('transparent', null)).toEqual({ background: 'transparent', plate: false })
  })
  it('explicit hex → plate with that colour', () => {
    expect(resolveLogoBackground('#123456', true)).toEqual({ background: '#123456', plate: true })
  })
  it('auto + dark logo → white plate', () => {
    expect(resolveLogoBackground(null, true)).toEqual({ background: '#FFFFFF', plate: true })
  })
  it('auto + light logo → transparent (reads on dark surfaces)', () => {
    expect(resolveLogoBackground(null, false)).toEqual({ background: 'transparent', plate: false })
  })
  it('auto + unknown lightness → white plate (safe default)', () => {
    expect(resolveLogoBackground(null, null)).toEqual({ background: '#FFFFFF', plate: true })
  })
})

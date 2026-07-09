import { describe, it, expect } from 'vitest'
import {
  deriveBrandPalette, contrastRatio, bestOnColor, readableText,
} from '@shared/branding/derive'

const PRESETS = {
  supporter: { primary: '#E51F4E', secondary: '#E8A33B', background: '#F5EEE3' },
  classic:   { primary: '#FF4A1C', secondary: '#D9F54A', background: '#F4EEE2' },
  oliva:     { primary: '#5E7A4C', secondary: '#C96A3E', background: '#F3F0E6' },
  azur:      { primary: '#3E7BFA', secondary: '#F0A836', background: '#14161C' },
}

describe('deriveBrandPalette', () => {
  it('marks a dark background as dark mode and a light one as light', () => {
    expect(deriveBrandPalette(PRESETS.azur).mode).toBe('dark')
    expect(deriveBrandPalette(PRESETS.supporter).mode).toBe('light')
  })

  it('onAccent meets WCAG AA (>= 4.5) against primary for every preset', () => {
    for (const p of Object.values(PRESETS)) {
      const pal = deriveBrandPalette(p)
      expect(contrastRatio(pal.onAccent, pal.accent)).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('onLime meets AA against secondary for every preset', () => {
    for (const p of Object.values(PRESETS)) {
      const pal = deriveBrandPalette(p)
      expect(contrastRatio(pal.onLime, pal.lime)).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('ink meets AA against paper for every preset (incl. Azur dark)', () => {
    for (const p of Object.values(PRESETS)) {
      const pal = deriveBrandPalette(p)
      expect(contrastRatio(pal.ink, pal.paper)).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('bestOnColor picks white on a dark colour and ink on a light one', () => {
    expect(bestOnColor('#111111', '#15140F')).toBe('#FFFFFF')
    expect(bestOnColor('#F5EEE3', '#15140F')).toBe('#15140F')
  })
})

describe('readableText (AA-guaranteed contrast)', () => {
  const AA = 4.5
  // Mid-tone surfaces where the soft brand inks alone dip below AA.
  const MIDTONES = ['#808080', '#7A6E5A', '#8A7B33', '#6E7B8A', '#A08050']

  it('always returns a colour meeting AA on any mid-tone surface', () => {
    for (const s of MIDTONES) {
      const c = readableText(s, '#15140F', '#F5F0E8')
      expect(contrastRatio(c, s)).toBeGreaterThanOrEqual(AA)
    }
  })

  it('upgrades to pure black/white when the soft candidates fall short', () => {
    // #767676 (luminance ~0.18) sits in the band where neither soft ink hits AA,
    // so a pure endpoint must be returned to stay legible.
    const c = readableText('#767676', '#15140F', '#F5F0E8')
    expect(['#000000', '#FFFFFF']).toContain(c)
    expect(contrastRatio(c, '#767676')).toBeGreaterThanOrEqual(4.5)
  })

  it('prefers a passing soft candidate over a pure endpoint', () => {
    // On a light paper, the soft dark ink already passes — keep it (warmth).
    expect(readableText('#F5EEE3', '#15140F', '#F5F0E8')).toBe('#15140F')
  })
})

describe('deriveBrandPalette contrast on adversarial custom schemes', () => {
  const AA = 4.5
  const SCHEMES = [
    { primary: '#808080', secondary: '#7A6E5A', background: '#6E7B8A' },
    { primary: '#3E7BFA', secondary: '#F0A836', background: '#14161C' }, // azur
    { primary: '#C8B8A0', secondary: '#9A8C6E', background: '#EDE7DA' }, // muted light
  ]
  it('ink/onAccent/onLime all meet AA against their own surface', () => {
    for (const s of SCHEMES) {
      const p = deriveBrandPalette(s)
      expect(contrastRatio(p.ink, p.paper)).toBeGreaterThanOrEqual(AA)
      expect(contrastRatio(p.onAccent, p.accent)).toBeGreaterThanOrEqual(AA)
      expect(contrastRatio(p.onLime, p.lime)).toBeGreaterThanOrEqual(AA)
    }
  })
})

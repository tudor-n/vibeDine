import { describe, it, expect } from 'vitest'
import {
  deriveBrandPalette, contrastRatio, bestOnColor, relativeLuminance,
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

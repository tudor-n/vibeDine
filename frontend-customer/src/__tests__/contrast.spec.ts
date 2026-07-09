import { describe, it, expect } from 'vitest'
import { bestOn, relativeLuminance } from '@/design/contrast'

describe('contrast.bestOn', () => {
  it('picks dark foreground on a light background', () => {
    expect(bestOn('#D9F54A')).toBe('#15140F') // vibeDine classic secondary (lime)
    expect(bestOn('#F4EEE2')).toBe('#15140F') // warm paper background
  })

  it('picks light foreground on a dark background', () => {
    expect(bestOn('#14161C')).toBe('#FFF4EC') // dark preset background
    expect(bestOn('#3E7BFA')).toBe('#FFF4EC') // dark preset primary
  })

  it('supports 3-digit hex shorthand', () => {
    expect(bestOn('#000')).toBe('#FFF4EC')
    expect(bestOn('#fff')).toBe('#15140F')
  })

  it('relativeLuminance is monotonic between black and white', () => {
    expect(relativeLuminance('#000000')).toBe(0)
    expect(relativeLuminance('#FFFFFF')).toBeCloseTo(1, 5)
  })
})

import { describe, it, expect } from 'vitest'
import { averageLuminance } from '@/utils/logoLuminance'

// RGBA quads.
function px(r: number, g: number, b: number, a = 255) { return [r, g, b, a] }

describe('averageLuminance', () => {
  it('returns a low value for a dark logo', () => {
    const data = new Uint8ClampedArray([...px(10, 10, 10), ...px(20, 20, 20)])
    expect(averageLuminance(data)!).toBeLessThan(0.3)
  })
  it('returns a high value for a light logo', () => {
    const data = new Uint8ClampedArray([...px(240, 240, 240), ...px(255, 255, 255)])
    expect(averageLuminance(data)!).toBeGreaterThan(0.7)
  })
  it('ignores fully transparent pixels (weights by alpha)', () => {
    // one opaque black + one transparent white => should read as dark.
    const data = new Uint8ClampedArray([...px(0, 0, 0, 255), ...px(255, 255, 255, 0)])
    expect(averageLuminance(data)!).toBeLessThan(0.2)
  })
  it('returns null when everything is transparent', () => {
    const data = new Uint8ClampedArray([...px(255, 255, 255, 0)])
    expect(averageLuminance(data)).toBeNull()
  })
})

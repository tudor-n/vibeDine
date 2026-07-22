import { describe, it, expect } from 'vitest'
import {
  VIBEY_TEASERS_RO,
  VIBEY_TEASERS_EN,
  pickTeaser,
  followUpLine,
} from '@/components/vibey/vibeyCopy'

describe('vibeyCopy', () => {
  it('has 5 Romanian and 5 English teasers', () => {
    expect(VIBEY_TEASERS_RO).toHaveLength(5)
    expect(VIBEY_TEASERS_EN).toHaveLength(5)
  })

  it('pickTeaser is deterministic for a given seed + locale', () => {
    const a = pickTeaser('order-123', 'ro')
    const b = pickTeaser('order-123', 'ro')
    expect(a).toBe(b)
    expect(VIBEY_TEASERS_RO).toContain(a)
  })

  it('pickTeaser picks from the locale-appropriate list', () => {
    expect(VIBEY_TEASERS_EN).toContain(pickTeaser('order-123', 'en'))
  })

  it('pickTeaser varies with the seed (not a constant index)', () => {
    const picks = new Set(
      ['order-a', 'order-b', 'order-c', 'order-d', 'order-e', 'order-f'].map((seed) =>
        pickTeaser(seed, 'ro'),
      ),
    )
    expect(picks.size).toBeGreaterThan(1)
  })

  it('followUpLine branches on remaining count (RO)', () => {
    expect(followUpLine('Cartofi wedges', 1, 'ro')).toBe(
      'Bună alegere — Cartofi wedges se potrivește perfect. Mai am o idee și ești gata:',
    )
    expect(followUpLine('IPA Zăganu', 0, 'ro')).toBe(
      'Perfect. IPA Zăganu completează masa — arată echilibrat acum. Poftă bună!',
    )
  })

  it('followUpLine branches on remaining count (EN)', () => {
    expect(followUpLine('Wedges', 1, 'en')).toBe(
      "Good choice — Wedges fits perfectly. I've got one more idea and you're all set:",
    )
    expect(followUpLine('Lava Cake', 0, 'en')).toBe(
      'Perfect. Lava Cake completes the meal — it looks balanced now. Enjoy!',
    )
  })
})

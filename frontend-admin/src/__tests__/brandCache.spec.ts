import { describe, it, expect, beforeEach } from 'vitest'
import { readCachedBrand, writeCachedBrand, parseBrandInput } from '@shared/branding/cache'
import { hydrateBrandFromCache } from '@/composables/useBrandTheme'

const COLORS = { primary: '#E51F4E', secondary: '#E8A33B', background: '#F5EEE3' }

describe('brand cache', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.removeAttribute('style')
  })

  it('round-trips colours for a scope', () => {
    writeCachedBrand('rest-1', COLORS)
    expect(readCachedBrand('rest-1')).toEqual(COLORS)
  })

  it('returns null when nothing is cached for the scope', () => {
    expect(readCachedBrand('rest-1')).toBeNull()
  })

  it('scopes entries per tenant so another restaurant never reads them', () => {
    writeCachedBrand('rest-1', COLORS)
    expect(readCachedBrand('rest-2')).toBeNull()
  })

  it('treats a null/empty scope as no cache rather than a shared bucket', () => {
    writeCachedBrand(null, COLORS)
    expect(readCachedBrand(null)).toBeNull()
    expect(localStorage.length).toBe(0)
  })

  it('writing null removes the entry, so an un-branded tenant stops painting', () => {
    writeCachedBrand('rest-1', COLORS)
    writeCachedBrand('rest-1', null)
    expect(readCachedBrand('rest-1')).toBeNull()
  })

  it('returns null for malformed JSON instead of throwing', () => {
    localStorage.setItem('clarifi.brand.rest-1', '{not json')
    expect(readCachedBrand('rest-1')).toBeNull()
  })

  it('rejects non-hex values that would be injected into CSS', () => {
    localStorage.setItem(
      'clarifi.brand.rest-1',
      JSON.stringify({ primary: 'red; background:url(//evil)', secondary: '#FFF', background: '#000' }),
    )
    expect(readCachedBrand('rest-1')).toBeNull()
  })

  it('rejects a partial entry missing a colour', () => {
    localStorage.setItem('clarifi.brand.rest-1', JSON.stringify({ primary: '#FFFFFF' }))
    expect(readCachedBrand('rest-1')).toBeNull()
  })
})

// parseBrandInput is the single guard every boot-time brand source passes through
// (localStorage cache on both apps, plus the server-injected window.__BRAND__ on
// the customer app), so it carries the whole "no untrusted value reaches CSS" claim.
describe('parseBrandInput', () => {
  it('accepts 3 valid hexes, in long and short form', () => {
    expect(parseBrandInput(COLORS)).toEqual(COLORS)
    const short = { primary: '#fff', secondary: '#000', background: '#ABC' }
    expect(parseBrandInput(short)).toEqual(short)
  })

  it('ignores extra properties rather than passing them through', () => {
    expect(parseBrandInput({ ...COLORS, evil: 'x' })).toEqual(COLORS)
  })

  it.each([
    ['null', null],
    ['a string', '#FFFFFF'],
    ['a number', 42],
    ['an array', ['#FFFFFF']],
    ['a css injection', { primary: 'red; background:url(//evil)', secondary: '#FFF', background: '#000' }],
    ['a script breakout', { primary: '#FFF"></script><script>alert(1)</script>', secondary: '#FFF', background: '#000' }],
    ['a named colour', { primary: 'red', secondary: '#FFF', background: '#000' }],
    ['a hex without #', { primary: 'FFFFFF', secondary: '#FFF', background: '#000' }],
    ['a 4-digit hex', { primary: '#FFFF', secondary: '#FFF', background: '#000' }],
    ['a missing colour', { primary: '#FFF', secondary: '#FFF' }],
  ])('rejects %s', (_label, value) => {
    expect(parseBrandInput(value)).toBeNull()
  })
})

describe('hydrateBrandFromCache', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.removeAttribute('style')
  })

  it('paints the cached palette onto <html> synchronously', () => {
    writeCachedBrand('rest-1', COLORS)
    hydrateBrandFromCache('rest-1')
    expect(document.documentElement.style.getPropertyValue('--accent')).toBe(COLORS.primary)
    expect(document.documentElement.style.getPropertyValue('--paper')).toBe(COLORS.background)
  })

  it('leaves the default palette alone when nothing is cached', () => {
    hydrateBrandFromCache('rest-1')
    expect(document.documentElement.style.getPropertyValue('--accent')).toBe('')
  })
})

import { describe, it, expect, beforeEach } from 'vitest'
import { readCachedBrand, writeCachedBrand } from '@shared/branding/cache'
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

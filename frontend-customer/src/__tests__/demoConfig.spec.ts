import { describe, it, expect, beforeEach } from 'vitest'
import {
  DEMO_TOKEN,
  isDemoToken,
  isDemoSession,
  shouldClearCrossModeStorage,
} from '@/demo/config'

describe('demo config', () => {
  beforeEach(() => {
    sessionStorage.clear()
    window.history.replaceState(null, '', '/')
  })

  it('recognizes the demo token and nothing else', () => {
    expect(isDemoToken(DEMO_TOKEN)).toBe(true)
    expect(isDemoToken('a3f0c9d2-real-guid')).toBe(false)
    expect(isDemoToken(null)).toBe(false)
    expect(isDemoToken(undefined)).toBe(false)
    expect(isDemoToken('')).toBe(false)
  })

  it('isDemoSession is true when the URL path is the demo token', () => {
    window.history.replaceState(null, '', `/${DEMO_TOKEN}`)
    expect(isDemoSession()).toBe(true)
  })

  it('isDemoSession is true when sessionStorage qrToken is the demo token', () => {
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    expect(isDemoSession()).toBe(true)
  })

  it('isDemoSession is false for a normal session', () => {
    window.history.replaceState(null, '', '/some-real-token')
    sessionStorage.setItem('qrToken', 'some-real-token')
    expect(isDemoSession()).toBe(false)
  })

  it('isDemoSession prefers a real URL token over a stale stored demo token', () => {
    window.history.replaceState(null, '', '/some-real-token')
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    expect(isDemoSession()).toBe(false)
  })

  it('isDemoSession falls back to the stored token when the URL has none', () => {
    window.history.replaceState(null, '', '/')
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    expect(isDemoSession()).toBe(true)
  })

  describe('shouldClearCrossModeStorage', () => {
    it('is false when there is no stored token', () => {
      expect(shouldClearCrossModeStorage(DEMO_TOKEN, null)).toBe(false)
      expect(shouldClearCrossModeStorage('real-token', null)).toBe(false)
    })

    it('is false when there is no URL token', () => {
      expect(shouldClearCrossModeStorage(null, DEMO_TOKEN)).toBe(false)
      expect(shouldClearCrossModeStorage(null, 'real-token')).toBe(false)
    })

    it('is true when the URL is demo and the stored token is real', () => {
      expect(shouldClearCrossModeStorage(DEMO_TOKEN, 'real-token')).toBe(true)
    })

    it('is true when the URL is real and the stored token is demo', () => {
      expect(shouldClearCrossModeStorage('real-token', DEMO_TOKEN)).toBe(true)
    })

    it('is false when URL and stored token are both demo or both real', () => {
      expect(shouldClearCrossModeStorage(DEMO_TOKEN, DEMO_TOKEN)).toBe(false)
      expect(shouldClearCrossModeStorage('real-token', 'other-real-token')).toBe(false)
    })
  })
})

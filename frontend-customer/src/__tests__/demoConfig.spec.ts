import { describe, it, expect, beforeEach } from 'vitest'
import { DEMO_TOKEN, isDemoToken, isDemoSession } from '@/demo/config'

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
})

// Link-expiry countdown for the diner's device session (see
// docs/superpowers/sdd task-4 brief). `useLinkExpiry` is a module-level
// singleton (like `useMenu`) so every consumer shares one timer and one
// `expired` flag — these tests exercise that shared module state directly,
// resetting it between cases since it isn't torn down by a component lifecycle.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useLinkExpiry } from '@/composables/useLinkExpiry'

describe('useLinkExpiry', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    const { expired, stop } = useLinkExpiry()
    stop()
    expired.value = false
  })

  afterEach(() => {
    useLinkExpiry().stop()
    vi.useRealTimers()
  })

  it('is not expired before the configured duration elapses', () => {
    const { expired, start } = useLinkExpiry()
    start(30)

    vi.advanceTimersByTime(29_000)

    expect(expired.value).toBe(false)
  })

  it('becomes expired once the configured duration elapses', () => {
    const { expired, start } = useLinkExpiry()
    start(30)

    vi.advanceTimersByTime(30_000)

    expect(expired.value).toBe(true)
  })

  it('start() called again restarts the countdown from zero', () => {
    const { expired, start } = useLinkExpiry()
    start(30)
    vi.advanceTimersByTime(20_000)

    start(30) // restart: the first timer's 20 elapsed seconds must not carry over

    vi.advanceTimersByTime(20_000) // 20s into the new window — old window would already be expired
    expect(expired.value).toBe(false)

    vi.advanceTimersByTime(10_000) // completes the new 30s window
    expect(expired.value).toBe(true)
  })

  it('stop() prevents expiry even after the duration would have elapsed', () => {
    const { expired, start, stop } = useLinkExpiry()
    start(30)

    stop()
    vi.advanceTimersByTime(60_000)

    expect(expired.value).toBe(false)
  })
})

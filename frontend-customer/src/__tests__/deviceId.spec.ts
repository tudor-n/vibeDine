// getDeviceId() gives this phone a stable identity across joins so the backend
// can attach a device session to the table visit. It must never throw — even in
// private-mode browsers where localStorage access itself can throw — falling
// back to an in-memory id for the page's lifetime instead.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const STORAGE_KEY = 'clarifi.deviceId'

async function freshModule() {
  vi.resetModules()
  return import('@/services/deviceId')
}

beforeEach(() => {
  localStorage.clear()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('getDeviceId', () => {
  it('returns a UUID-shaped string', async () => {
    const { getDeviceId } = await freshModule()
    expect(getDeviceId()).toMatch(UUID_RE)
  })

  it('is stable across multiple calls', async () => {
    const { getDeviceId } = await freshModule()
    const first = getDeviceId()
    const second = getDeviceId()
    expect(second).toBe(first)
  })

  it('persists the generated id in localStorage under clarifi.deviceId', async () => {
    const { getDeviceId } = await freshModule()
    const id = getDeviceId()
    expect(localStorage.getItem(STORAGE_KEY)).toBe(id)
  })

  it('reuses an id already stored in localStorage instead of generating a new one', async () => {
    localStorage.setItem(STORAGE_KEY, 'existing-device-id')
    const { getDeviceId } = await freshModule()
    expect(getDeviceId()).toBe('existing-device-id')
  })

  it('falls back to an in-memory id for the page lifetime when localStorage throws', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError: private mode')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('SecurityError: private mode')
    })

    const { getDeviceId } = await freshModule()
    expect(() => getDeviceId()).not.toThrow()
    const first = getDeviceId()
    expect(first).toMatch(UUID_RE)
    // Stable across calls within the same module instance even though storage
    // never succeeds.
    expect(getDeviceId()).toBe(first)
  })
})

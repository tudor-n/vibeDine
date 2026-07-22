// A stable per-phone identity, independent of the per-table `party.guest.<qrToken>`
// id, so the backend can attach a DeviceSession that follows this device across
// every table it visits during its localStorage lifetime. Sent as `deviceId` on
// POST /tables/:qrToken/join.
//
// Must never throw: private-mode browsers can make localStorage reads/writes
// throw a SecurityError rather than just no-op. When that happens we fall back
// to an in-memory id that stays stable for the rest of the page's lifetime
// (lost on next load — acceptable, since private mode has no session to resume
// anyway).
const STORAGE_KEY = 'clarifi.deviceId'

let memoryDeviceId: string | null = null

function generateUuidV4(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }

  // Fallback: build an RFC 4122 v4 UUID from crypto.getRandomValues (or, in the
  // extremely unlikely case that's also unavailable, Math.random).
  const bytes = new Uint8Array(16)
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(bytes)
  } else {
    for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256)
  }
  bytes[6] = (bytes[6]! & 0x0f) | 0x40 // version 4
  bytes[8] = (bytes[8]! & 0x3f) | 0x80 // variant 10

  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

export function getDeviceId(): string {
  if (memoryDeviceId) return memoryDeviceId

  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      memoryDeviceId = stored
      return memoryDeviceId
    }
    const generated = generateUuidV4()
    localStorage.setItem(STORAGE_KEY, generated)
    memoryDeviceId = generated
    return memoryDeviceId
  } catch {
    // private mode / disabled storage — keep an in-memory id for this page load.
    memoryDeviceId = generateUuidV4()
    return memoryDeviceId
  }
}

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick } from 'vue'
import { createPinia } from 'pinia'
import { useWebSocket } from '@/composables/useWebSocket'
import { useSessionStore } from '@/stores/useSessionStore'
import { DEMO_TOKEN } from '@/demo/config'

// useWebSocket registers onUnmounted, so it must run inside a component.
function mountWithWebSocket() {
  const app = createApp(
    defineComponent({
      setup() {
        useWebSocket()
        return () => h('div')
      },
    }),
  )
  app.use(createPinia())
  const el = document.createElement('div')
  app.mount(el)
  return app
}

describe('useWebSocket demo guard', () => {
  let apps: ReturnType<typeof mountWithWebSocket>[]
  beforeEach(() => {
    sessionStorage.clear()
    apps = []
    vi.stubGlobal(
      'WebSocket',
      vi.fn(function () {
        return { close: vi.fn(), readyState: 3 }
      }),
    )
  })
  afterEach(() => {
    apps.forEach((a) => a.unmount())
    vi.unstubAllGlobals()
  })

  it('never opens a socket for the demo session', async () => {
    sessionStorage.setItem('qrToken', DEMO_TOKEN)
    sessionStorage.setItem('tableId', 'demo-table')
    apps.push(mountWithWebSocket())
    await nextTick()
    expect(WebSocket).not.toHaveBeenCalled()
  })

  it('still opens a socket for a real session', async () => {
    sessionStorage.setItem('qrToken', 'real-token')
    sessionStorage.setItem('tableId', 'real-table')
    apps.push(mountWithWebSocket())
    await nextTick()
    expect(WebSocket).toHaveBeenCalledOnce()
  })
})

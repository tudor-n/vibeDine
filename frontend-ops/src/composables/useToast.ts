import { ref } from 'vue'

export interface Toast {
  id: number
  message: string
  kind: 'success' | 'error' | 'info'
}

const toasts = ref<Toast[]>([])
let nextId = 1

export function useToast() {
  function push(message: string, kind: Toast['kind'] = 'info') {
    const id = nextId++
    toasts.value.push({ id, message, kind })
    setTimeout(() => dismiss(id), 4500)
  }

  function dismiss(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  return {
    toasts,
    dismiss,
    success: (m: string) => push(m, 'success'),
    error: (m: string) => push(m, 'error'),
    info: (m: string) => push(m, 'info'),
  }
}

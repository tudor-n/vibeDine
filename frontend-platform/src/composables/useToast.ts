import { ref } from 'vue'

export type ToastVariant = 'success' | 'error' | 'info'

export interface Toast {
  id: number
  variant: ToastVariant
  message: string
}

const toasts = ref<Toast[]>([])
let nextId = 1

function push(variant: ToastVariant, message: string): number {
  const id = nextId++
  toasts.value.push({ id, variant, message })
  setTimeout(() => dismiss(id), variant === 'error' ? 6000 : 4000)
  return id
}

function dismiss(id: number) {
  toasts.value = toasts.value.filter((t) => t.id !== id)
}

export function useToast() {
  return {
    toasts,
    success: (message: string) => push('success', message),
    error: (message: string) => push('error', message),
    info: (message: string) => push('info', message),
    dismiss,
  }
}

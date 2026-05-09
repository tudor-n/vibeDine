import { ref } from 'vue'

export type ToastVariant = 'success' | 'error' | 'info'

export interface Toast {
  id: number
  variant: ToastVariant
  title: string
  message?: string
  duration: number
}

export interface ToastOptions {
  title?: string
  duration?: number
}

const toasts = ref<Toast[]>([])
let nextId = 1

const DEFAULT_DURATION: Record<ToastVariant, number> = {
  success: 4000,
  info: 4000,
  error: 6000,
}

const DEFAULT_TITLE: Record<ToastVariant, string> = {
  success: 'gata · salvat',
  info: 'info',
  error: 'eroare',
}

function push(variant: ToastVariant, message: string, opts: ToastOptions = {}): number {
  const id = nextId++
  const duration = opts.duration ?? DEFAULT_DURATION[variant]
  toasts.value.push({
    id,
    variant,
    title: opts.title ?? DEFAULT_TITLE[variant],
    message,
    duration,
  })
  return id
}

function dismiss(id: number) {
  const idx = toasts.value.findIndex((t) => t.id === id)
  if (idx !== -1) toasts.value.splice(idx, 1)
}

export function useToast() {
  return {
    toasts,
    success: (message: string, opts?: ToastOptions) => push('success', message, opts),
    error: (message: string, opts?: ToastOptions) => push('error', message, opts),
    info: (message: string, opts?: ToastOptions) => push('info', message, opts),
    dismiss,
  }
}

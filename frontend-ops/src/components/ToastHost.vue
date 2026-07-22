<script setup lang="ts">
import { useToast } from '@/composables/useToast'

const { toasts, dismiss } = useToast()

function toastClass(kind: string): string {
  if (kind === 'success') return 'badge-green'
  if (kind === 'error') return 'badge-red'
  return 'badge-blue'
}
</script>

<template>
  <div class="fixed bottom-4 right-4 z-[60] flex flex-col gap-2">
    <div
      v-for="t in toasts"
      :key="t.id"
      class="card px-4 py-3 text-sm flex items-center gap-3 min-w-[260px] max-w-md"
      role="status"
    >
      <span class="badge" :class="toastClass(t.kind)">{{ t.kind }}</span>
      <span class="flex-1">{{ t.message }}</span>
      <button class="text-faint hover:opacity-70" @click="dismiss(t.id)" aria-label="Dismiss">✕</button>
    </div>
  </div>
</template>

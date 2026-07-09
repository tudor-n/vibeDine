<script setup lang="ts">
import { useToast } from '@/composables/useToast'

const { toasts, dismiss } = useToast()
</script>

<template>
  <div class="fixed bottom-5 right-5 z-[60] flex flex-col gap-2 w-[320px]">
    <div
      v-for="t in toasts"
      :key="t.id"
      class="card px-4 py-3 flex items-start gap-3 cursor-pointer"
      :style="{ borderLeft: `4px solid ${t.variant === 'success' ? 'var(--green)' : t.variant === 'error' ? 'var(--accent)' : 'var(--ink40)'}` }"
      @click="dismiss(t.id)"
    >
      <span class="text-sm flex-1">{{ t.message }}</span>
    </div>
  </div>
</template>

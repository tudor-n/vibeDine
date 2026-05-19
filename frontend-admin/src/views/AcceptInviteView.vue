<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import { authApi } from '@/services/api-client'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const state = ref<'working' | 'done' | 'error'>('working')
const message = ref('Se procesează invitația…')

onMounted(async () => {
  const token = route.query.token as string | undefined
  if (!token) {
    state.value = 'error'
    message.value = 'Link de invitație invalid.'
    return
  }
  try {
    await authApi.acceptInvite(token)
    await auth.loadAccessibleRestaurants()
    state.value = 'done'
    message.value = 'Invitație acceptată!'
    setTimeout(() => router.replace('/'), 1200)
  } catch (e) {
    state.value = 'error'
    message.value = e instanceof Error ? e.message : 'Nu am putut accepta invitația.'
  }
})
</script>

<template>
  <div class="min-h-screen grid place-items-center bg-paper text-ink p-6 text-center">
    <div class="max-w-sm">
      <div class="eyebrow mb-2">INVITAȚIE</div>
      <h1 class="display-title text-3xl mb-4">Co-proprietar</h1>
      <p class="text-ink-70" :class="{ 'text-red-600': state === 'error' }">{{ message }}</p>
      <RouterLink v-if="state === 'error'" to="/" class="mt-6 inline-block text-sm underline text-ink-70">
        Înapoi la dashboard
      </RouterLink>
    </div>
  </div>
</template>

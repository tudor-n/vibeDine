<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const state = ref<'working' | 'done' | 'error'>('working')
const message = ref('Se confirmă adresa de email…')

onMounted(async () => {
  const userId = route.query.userId as string | undefined
  const token = route.query.token as string | undefined

  if (!userId || !token) {
    state.value = 'error'
    message.value = 'Link de confirmare invalid.'
    return
  }

  try {
    await auth.confirmEmail(userId, token)
    state.value = 'done'
    message.value = 'Contul a fost confirmat! Te redirecționăm…'
    // Router guard will send the user to /onboarding if they have no org yet.
    setTimeout(() => router.replace('/'), 1000)
  } catch (e) {
    state.value = 'error'
    message.value = e instanceof Error ? e.message : 'Nu am putut confirma emailul.'
  }
})
</script>

<template>
  <div class="min-h-screen grid place-items-center bg-paper text-ink p-6 text-center">
    <div class="max-w-sm">
      <div class="eyebrow mb-2">CONFIRMARE EMAIL</div>
      <h1 class="display-title text-3xl mb-4">
        <template v-if="state === 'done'">Cont activat</template>
        <template v-else-if="state === 'error'">Eroare</template>
        <template v-else>Se procesează…</template>
      </h1>
      <p class="text-ink-70" :class="{ 'text-red-600': state === 'error' }">{{ message }}</p>
      <RouterLink
        v-if="state === 'error'"
        to="/login"
        class="mt-6 inline-block text-sm underline text-ink-70"
      >
        Înapoi la autentificare
      </RouterLink>
    </div>
  </div>
</template>

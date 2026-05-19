<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const mode = ref<'login' | 'register'>('login')
const email = ref('')
const password = ref('')
const fullName = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function submit() {
  error.value = null
  busy.value = true
  try {
    if (mode.value === 'login') {
      await auth.login(email.value.trim(), password.value)
    } else {
      await auth.register(email.value.trim(), password.value, fullName.value.trim())
    }
    const redirect = (route.query.redirect as string) || '/'
    router.replace(redirect)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Autentificare eșuată.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="min-h-screen grid place-items-center bg-paper text-ink p-6">
    <div class="w-full max-w-sm">
      <div class="eyebrow mb-2">SMART RESTAURANT</div>
      <h1 class="display-title text-3xl mb-6">
        {{ mode === 'login' ? 'Autentificare' : 'Cont nou' }}
      </h1>

      <form class="flex flex-col gap-3" @submit.prevent="submit">
        <input
          v-if="mode === 'register'"
          v-model="fullName"
          type="text"
          required
          placeholder="Nume complet"
          class="auth-input"
        />
        <input v-model="email" type="email" required placeholder="Email" class="auth-input" autocomplete="username" />
        <input
          v-model="password"
          type="password"
          required
          placeholder="Parolă"
          class="auth-input"
          autocomplete="current-password"
        />

        <p v-if="error" class="text-sm text-red-600">{{ error }}</p>

        <button type="submit" :disabled="busy" class="auth-btn">
          {{ busy ? 'Se procesează…' : mode === 'login' ? 'Intră în cont' : 'Creează cont' }}
        </button>
      </form>

      <button
        class="mt-4 text-sm underline text-ink-70"
        type="button"
        @click="mode = mode === 'login' ? 'register' : 'login'"
      >
        {{ mode === 'login' ? 'Nu ai cont? Înregistrează-te' : 'Ai deja cont? Autentifică-te' }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.auth-input {
  width: 100%;
  padding: 12px 14px;
  border-radius: 12px;
  border: 1px solid var(--ink12, rgba(0, 0, 0, 0.12));
  background: var(--card, #fff);
  color: var(--ink, #15140f);
  font-size: 15px;
}
.auth-btn {
  width: 100%;
  padding: 12px 14px;
  border-radius: 999px;
  background: var(--ink, #15140f);
  color: var(--paperHi, #fff);
  font-weight: 600;
  cursor: pointer;
}
.auth-btn:disabled {
  opacity: 0.6;
  cursor: progress;
}
</style>

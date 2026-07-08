<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const error = ref<string | null>(null)
const loading = ref(false)

async function submit() {
  error.value = null
  loading.value = true
  try {
    await auth.login(email.value.trim(), password.value)
    const redirect = (route.query.redirect as string) || '/'
    router.push(redirect)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Sign in failed.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="min-h-screen flex items-center justify-center p-5">
    <div class="card w-full max-w-md p-8">
      <div class="mb-6">
        <div class="font-display text-2xl font-extrabold tracking-tight">vibeDine</div>
        <div class="eyebrow mt-1">HQ Console · Platform control plane</div>
      </div>

      <form @submit.prevent="submit" class="space-y-4">
        <div>
          <label class="field-label" for="email">Email</label>
          <input id="email" v-model="email" type="email" autocomplete="username" class="field-input" required />
        </div>
        <div>
          <label class="field-label" for="password">Password</label>
          <input id="password" v-model="password" type="password" autocomplete="current-password" class="field-input" required />
        </div>

        <p v-if="error" class="text-sm" :style="{ color: 'var(--accent)' }">{{ error }}</p>

        <button type="submit" class="pill-btn w-full" :disabled="loading">
          {{ loading ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>

      <p class="text-faint text-xs mt-6 text-center">
        Default dev account: admin@vibedine.local / ChangeMe123!
      </p>
    </div>
  </div>
</template>

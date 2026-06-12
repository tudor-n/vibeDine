<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import { EmailNotConfirmedError, WaiterPendingApprovalError } from '@/services/api-client'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const mode = ref<'login' | 'register'>('login')
const email = ref('')
const password = ref('')
const fullName = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

// Post-register: show "check your email" panel.
const sentToEmail = ref<string | null>(null)
const sentMessage = ref<string | null>(null)
const resending = ref(false)
const resendNotice = ref<string | null>(null)

// Login attempt that failed because the email is unconfirmed.
const unconfirmedEmail = ref<string | null>(null)

async function submit() {
  error.value = null
  resendNotice.value = null
  unconfirmedEmail.value = null
  busy.value = true
  try {
    if (mode.value === 'login') {
      await auth.login(email.value.trim(), password.value)
      const redirect = (route.query.redirect as string) || '/'
      router.replace(redirect)
    } else {
      const result = await auth.register(email.value.trim(), password.value, fullName.value.trim())
      sentToEmail.value = result.email
      sentMessage.value = result.message
    }
  } catch (e) {
    if (e instanceof EmailNotConfirmedError) {
      unconfirmedEmail.value = e.email || email.value.trim()
      error.value = e.message
    } else if (e instanceof WaiterPendingApprovalError) {
      router.replace({ name: 'awaiting-approval', query: { email: e.email || email.value.trim() } })
      return
    } else {
      error.value = e instanceof Error ? e.message : 'Autentificare eșuată.'
    }
  } finally {
    busy.value = false
  }
}

async function resend(target: string) {
  resending.value = true
  resendNotice.value = null
  try {
    await auth.resendConfirmation(target)
    resendNotice.value = 'Ți-am trimis un nou link de confirmare. Verifică inbox-ul.'
  } catch (e) {
    resendNotice.value = e instanceof Error ? e.message : 'Nu am putut trimite emailul.'
  } finally {
    resending.value = false
  }
}

function reset() {
  sentToEmail.value = null
  sentMessage.value = null
  resendNotice.value = null
  unconfirmedEmail.value = null
  error.value = null
  password.value = ''
}
</script>

<template>
  <div class="min-h-screen grid place-items-center bg-paper text-ink p-6">
    <div class="w-full max-w-sm">
      <div class="eyebrow mb-2">SMART RESTAURANT</div>
      <h1 class="display-title text-3xl mb-6">
        <template v-if="sentToEmail">Verifică-ți emailul</template>
        <template v-else>{{ mode === 'login' ? 'Autentificare' : 'Cont nou' }}</template>
      </h1>

      <!-- Post-register: confirmation sent -->
      <div v-if="sentToEmail" class="flex flex-col gap-4">
        <p class="text-ink-70 text-sm leading-relaxed">
          {{ sentMessage }}<br />
          Am trimis un link de confirmare la <strong>{{ sentToEmail }}</strong>. Apasă pe link pentru a-ți activa contul.
        </p>
        <button type="button" class="auth-btn" :disabled="resending" @click="resend(sentToEmail!)">
          {{ resending ? 'Se trimite…' : 'Retrimite emailul' }}
        </button>
        <p v-if="resendNotice" class="text-sm text-ink-70">{{ resendNotice }}</p>
        <button class="text-sm underline text-ink-70" type="button" @click="reset(); mode = 'login'">
          Înapoi la autentificare
        </button>
      </div>

      <!-- Normal login / register form -->
      <template v-else>
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

          <!-- Login blocked because email not confirmed -->
          <div v-if="unconfirmedEmail" class="flex flex-col gap-2">
            <button
              type="button"
              class="auth-btn"
              :disabled="resending"
              @click="resend(unconfirmedEmail!)"
            >
              {{ resending ? 'Se trimite…' : 'Retrimite emailul de confirmare' }}
            </button>
            <p v-if="resendNotice" class="text-sm text-ink-70">{{ resendNotice }}</p>
          </div>

          <button v-else type="submit" :disabled="busy" class="auth-btn">
            {{ busy ? 'Se procesează…' : mode === 'login' ? 'Intră în cont' : 'Creează cont' }}
          </button>
        </form>

        <button
          class="mt-4 text-sm underline text-ink-70"
          type="button"
          @click="reset(); mode = mode === 'login' ? 'register' : 'login'"
        >
          {{ mode === 'login' ? 'Nu ai cont? Înregistrează-te' : 'Ai deja cont? Autentifică-te' }}
        </button>
      </template>
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

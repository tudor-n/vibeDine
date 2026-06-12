<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import { waiterPublicApi, type WaiterSignupPreview } from '@/services/api-client'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const token = route.params.token as string

const preview = ref<WaiterSignupPreview | null>(null)
const loadError = ref<string | null>(null)
const loading = ref(true)

const fullName = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const error = ref<string | null>(null)
const busy = ref(false)
const done = ref<{ email: string; message: string } | null>(null)

function signOutAndStay() {
  auth.logout()
  // Stay on the same URL so the signup form becomes interactive.
  router.go(0)
}

onMounted(async () => {
  try {
    preview.value = await waiterPublicApi.preview(token)
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : 'Link invalid.'
  } finally {
    loading.value = false
  }
})

async function submit() {
  error.value = null
  if (password.value !== confirmPassword.value) {
    error.value = 'Parolele nu coincid.'
    return
  }
  busy.value = true
  try {
    const result = await waiterPublicApi.signup(
      token,
      email.value.trim(),
      password.value,
      fullName.value.trim(),
    )
    done.value = { email: result.email, message: result.message }
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Înscrierea a eșuat.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="min-h-screen grid place-items-center bg-paper text-ink p-6">
    <div class="w-full max-w-md">
      <div v-if="loading" class="text-ink-70 text-center">Se încarcă…</div>

      <div v-else-if="loadError" class="text-center">
        <div class="eyebrow mb-2">LINK INVALID</div>
        <h1 class="display-title text-3xl mb-2">Nu am putut deschide linkul</h1>
        <p class="text-ink-70">{{ loadError }}</p>
      </div>

      <div v-else-if="done" class="text-center">
        <div class="eyebrow mb-2">APROAPE GATA</div>
        <h1 class="display-title text-3xl mb-3">Verifică-ți emailul</h1>
        <p class="text-ink-70">{{ done.message }}</p>
        <p class="text-ink-70 mt-3 font-mono text-sm">{{ done.email }}</p>
      </div>

      <div v-else>
        <div class="eyebrow mb-2">ÎNSCRIE-TE CA OSPĂTAR</div>
        <h1 class="display-title text-3xl mb-1">{{ preview?.restaurantName }}</h1>
        <p class="text-ink-70 mb-6 text-sm">{{ preview?.organizationName }}</p>

        <div v-if="auth.isAuthenticated" class="auth-banner mb-4">
          Ești deja autentificat ca <strong>{{ auth.user?.email }}</strong>. Pentru a crea
          un cont nou de ospătar trebuie să te deconectezi mai întâi.
          <button type="button" class="btn-link" @click="signOutAndStay">Deconectează-mă</button>
        </div>

        <form class="flex flex-col gap-3" :class="{ 'opacity-50 pointer-events-none': auth.isAuthenticated }" @submit.prevent="submit">
          <label class="text-sm font-semibold">Nume complet</label>
          <input v-model="fullName" required placeholder="ex. Andrei Popescu" class="auth-input" />

          <label class="text-sm font-semibold mt-2">Email</label>
          <input v-model="email" type="email" required placeholder="email@exemplu.ro" class="auth-input" />

          <label class="text-sm font-semibold mt-2">Parolă</label>
          <input v-model="password" type="password" required minlength="8" class="auth-input" />

          <label class="text-sm font-semibold mt-2">Confirmă parola</label>
          <input v-model="confirmPassword" type="password" required minlength="8" class="auth-input" />

          <p v-if="error" class="text-sm text-red-600">{{ error }}</p>

          <button type="submit" :disabled="busy" class="auth-btn mt-2">
            {{ busy ? 'Se trimite…' : 'Înscrie-te' }}
          </button>
        </form>

        <p class="text-xs text-ink-70 mt-4">
          După confirmarea emailului, managerul va aproba contul tău. Vei putea apoi să te autentifici.
        </p>
      </div>
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
.auth-banner {
  padding: 12px 14px;
  border-radius: 12px;
  background: #fef3c7;
  color: #92400e;
  border: 1px solid #fcd34d;
  font-size: 13px;
  line-height: 1.5;
}
.btn-link {
  margin-top: 6px;
  display: inline-block;
  background: transparent;
  border: none;
  padding: 0;
  color: #92400e;
  font-weight: 700;
  text-decoration: underline;
  cursor: pointer;
}
.pointer-events-none { pointer-events: none; }
.opacity-50 { opacity: 0.5; }
</style>

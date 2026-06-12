<script setup lang="ts">
import { ref, watch } from 'vue'
import { useAuthStore } from '@/stores/useAuthStore'
import { profileApi } from '@/services/api-client'
import { useToast } from '@/composables/useToast'

const auth = useAuthStore()
const toast = useToast()

const fullName = ref(auth.user?.fullName ?? '')
const busyName = ref(false)
const busyAvatar = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)

watch(
  () => auth.user?.fullName,
  (v: string | undefined) => {
    if (v !== undefined) fullName.value = v
  },
)

async function saveName() {
  if (!fullName.value.trim()) return
  busyName.value = true
  try {
    await profileApi.updateName(fullName.value.trim())
    await auth.fetchMe()
    toast.success('Numele a fost actualizat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la salvarea numelui.')
  } finally {
    busyName.value = false
  }
}

function pickFile() {
  fileInput.value?.click()
}

async function onFile(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return
  busyAvatar.value = true
  try {
    await profileApi.uploadAvatar(file)
    await auth.fetchMe()
    toast.success('Avatar actualizat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea avatarului.')
  } finally {
    busyAvatar.value = false
    if (target) target.value = ''
  }
}

async function removeAvatar() {
  if (!confirm('Sigur vrei să elimini avatarul?')) return
  busyAvatar.value = true
  try {
    await profileApi.deleteAvatar()
    await auth.fetchMe()
    toast.success('Avatar eliminat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la eliminarea avatarului.')
  } finally {
    busyAvatar.value = false
  }
}
</script>

<template>
  <div class="grid gap-6 md:grid-cols-2">
    <section class="card p-6">
      <div class="eyebrow mb-4">Avatar</div>

      <div class="flex items-center gap-4 mb-4">
        <div class="avatar-circle">
          <img v-if="auth.user?.avatarUrl" :src="auth.user.avatarUrl" alt="" />
          <span v-else>{{ (auth.user?.fullName || 'A').charAt(0).toUpperCase() }}</span>
        </div>
        <div class="flex flex-col gap-2">
          <button type="button" class="btn" :disabled="busyAvatar" @click="pickFile">
            {{ auth.user?.avatarUrl ? 'Schimbă' : 'Încarcă' }}
          </button>
          <button v-if="auth.user?.avatarUrl" type="button" class="btn btn-danger" :disabled="busyAvatar" @click="removeAvatar">
            Elimină
          </button>
        </div>
      </div>

      <p class="text-xs text-ink-70">
        PNG, JPG sau WebP, maxim 2 MB. Imaginea va apărea în bara laterală și în lista de angajați.
      </p>

      <input
        ref="fileInput"
        type="file"
        accept="image/png,image/jpeg,image/webp"
        class="hidden"
        @change="onFile"
      />
    </section>

    <section class="card p-6">
      <div class="eyebrow mb-4">Informații cont</div>

      <form class="flex flex-col gap-3" @submit.prevent="saveName">
        <label class="text-sm font-semibold">Nume complet</label>
        <input v-model="fullName" class="auth-input" required />

        <label class="text-sm font-semibold mt-2">Email</label>
        <input :value="auth.user?.email" class="auth-input" readonly />
        <p class="text-xs text-ink-70 -mt-1">Emailul nu poate fi schimbat momentan.</p>

        <button type="submit" :disabled="busyName" class="btn btn-primary mt-3 self-start">
          {{ busyName ? 'Se salvează…' : 'Salvează modificările' }}
        </button>
      </form>
    </section>
  </div>
</template>

<style scoped>
.card {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 18px;
  box-shadow: var(--shadow-soft);
}
.avatar-circle {
  width: 96px;
  height: 96px;
  border-radius: 50%;
  background: var(--ink);
  color: var(--paperHi);
  display: grid;
  place-items: center;
  font-size: 36px;
  font-weight: 700;
  overflow: hidden;
  flex-shrink: 0;
}
.avatar-circle img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: transparent;
  color: var(--ink);
  font-weight: 600;
  cursor: pointer;
}
.btn:hover { background: var(--ink06); }
.btn:disabled { opacity: 0.55; cursor: not-allowed; }
.btn-primary {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
}
.btn-danger {
  border-color: #b91c1c;
  color: #b91c1c;
}
.btn-danger:hover { background: rgba(185, 28, 28, 0.08); }
.auth-input {
  width: 100%;
  padding: 12px 14px;
  border-radius: 12px;
  border: 1px solid var(--ink12);
  background: var(--card, #fff);
  color: var(--ink);
  font-size: 15px;
}
.auth-input[readonly] {
  background: var(--ink06);
  color: var(--ink-70, var(--ink));
}
.hidden { display: none; }
</style>

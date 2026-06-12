<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const email = (route.query.email as string) || ''

function goLogin() {
  router.replace({ name: 'login' })
}
</script>

<template>
  <div class="min-h-screen grid place-items-center bg-paper text-ink p-6 text-center">
    <div class="max-w-md">
      <div class="eyebrow mb-2">CONT ÎN AȘTEPTARE</div>
      <h1 class="display-title text-3xl mb-3">Așteaptă aprobarea managerului</h1>
      <p class="text-ink-70">
        Contul tău {{ email ? `(${email})` : '' }} a fost creat, dar trebuie ca managerul restaurantului
        să îl aprobe înainte să te poți conecta.
      </p>
      <p class="text-ink-70 mt-3">
        Vei putea reveni mai târziu — odată ce ești aprobat, vei vedea panoul de comenzi.
      </p>

      <button type="button" class="auth-btn mt-6" @click="goLogin">Înapoi la autentificare</button>
    </div>
  </div>
</template>

<style scoped>
.auth-btn {
  padding: 12px 22px;
  border-radius: 999px;
  background: var(--ink, #15140f);
  color: var(--paperHi, #fff);
  font-weight: 600;
  cursor: pointer;
  border: none;
}
</style>

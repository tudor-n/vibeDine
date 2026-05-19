<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import QRCode from 'qrcode'
import { useAuthStore } from '@/stores/useAuthStore'
import { tablesApi, type TableSummary } from '@/services/api-client'
import { useToast } from '@/composables/useToast'

const auth = useAuthStore()
const toast = useToast()

// Where the customer app lives. Configurable so prod can point at a real
// domain; defaults to the local dev customer port.
const customerBase = (
  (import.meta.env.VITE_CUSTOMER_URL as string | undefined)?.replace(/\/+$/, '') ||
  'http://localhost:3000'
)

const tables = ref<TableSummary[]>([])
const qrImages = ref<Record<string, string>>({})
const newTableNumber = ref('')
const busy = ref(false)

const activeName = computed(() => auth.activeRestaurant?.name ?? 'locația curentă')

function customerUrl(t: TableSummary) {
  return `${customerBase}/${t.qrToken}`
}

async function renderQr(t: TableSummary) {
  qrImages.value[t.id] = await QRCode.toDataURL(customerUrl(t), {
    width: 240,
    margin: 1,
    errorCorrectionLevel: 'M',
  })
}

async function refresh() {
  tables.value = await tablesApi.list()
  await Promise.all(tables.value.map(renderQr))
}

onMounted(async () => {
  try {
    await refresh()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea meselor.')
  }
})

async function addTable() {
  const n = newTableNumber.value.trim()
  if (!n) return
  busy.value = true
  try {
    await tablesApi.create(n)
    newTableNumber.value = ''
    await refresh()
    toast.success(`Masa ${n} a fost creată.`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la crearea mesei.')
  } finally {
    busy.value = false
  }
}

async function regenerate(t: TableSummary) {
  if (!confirm(`Regenerezi codul QR pentru masa ${t.tableNumber}? Codul tipărit anterior nu va mai funcționa.`)) return
  busy.value = true
  try {
    const updated = await tablesApi.regenerate(t.id)
    const i = tables.value.findIndex((x) => x.id === t.id)
    if (i !== -1) tables.value[i] = updated
    await renderQr(updated)
    toast.success('Cod QR regenerat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la regenerare.')
  } finally {
    busy.value = false
  }
}

async function removeTable(t: TableSummary) {
  if (!confirm(`Ștergi masa ${t.tableNumber}?`)) return
  busy.value = true
  try {
    await tablesApi.remove(t.id)
    await refresh()
    toast.success('Masă ștearsă.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la ștergere.')
  } finally {
    busy.value = false
  }
}

async function copyLink(t: TableSummary) {
  await navigator.clipboard.writeText(customerUrl(t))
  toast.success('Link copiat.')
}

function downloadQr(t: TableSummary) {
  const src = qrImages.value[t.id]
  if (!src) return
  const a = document.createElement('a')
  a.href = src
  a.download = `qr-masa-${t.tableNumber}.png`
  a.click()
}
</script>

<template>
  <div class="max-w-6xl mx-auto relative">
    <div class="mb-8">
      <div class="eyebrow mb-4">Mese &amp; coduri QR</div>
      <h1 class="display-title text-[44px] md:text-[56px]">Coduri QR</h1>
      <p class="text-ink-70 text-base mt-2">
        Coduri pentru <span class="font-semibold text-ink">{{ activeName }}</span>. Fiecare masă are
        propriul cod — clientul îl scanează sau deschide linkul direct.
      </p>
    </div>

    <section class="card-paper p-6 mb-6">
      <div class="eyebrow mb-2">Adaugă</div>
      <h2 class="display-title text-3xl mb-4">Masă nouă</h2>
      <form class="flex flex-col md:flex-row gap-2" @submit.prevent="addTable">
        <input
          v-model="newTableNumber"
          placeholder="Număr masă (ex: 12)"
          class="qr-input flex-1"
          required
        />
        <button type="submit" class="pill-btn" :disabled="busy">Adaugă masă</button>
      </form>
    </section>

    <div v-if="!tables.length" class="card-paper p-6 text-ink italic text-base">
      Nicio masă încă pentru această locație. Adaugă prima masă mai sus.
    </div>

    <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
      <div v-for="t in tables" :key="t.id" class="card-white p-5 flex flex-col items-center text-center">
        <div class="eyebrow mb-3">Masa {{ t.tableNumber }}</div>

        <img
          v-if="qrImages[t.id]"
          :src="qrImages[t.id]"
          :alt="`Cod QR masa ${t.tableNumber}`"
          class="qr-img mb-4"
        />
        <div v-else class="qr-img qr-img-empty mb-4">…</div>

        <a
          :href="customerUrl(t)"
          target="_blank"
          rel="noopener"
          class="qr-link mb-4 break-all"
        >{{ customerUrl(t) }}</a>

        <div class="flex flex-wrap gap-2 justify-center">
          <a
            :href="customerUrl(t)"
            target="_blank"
            rel="noopener"
            class="pill-btn pill-btn-ghost"
          >Deschide</a>
          <button type="button" class="pill-btn pill-btn-ghost" @click="copyLink(t)">
            Copiază link
          </button>
          <button type="button" class="pill-btn pill-btn-ghost" @click="downloadQr(t)">
            Descarcă QR
          </button>
        </div>
        <div class="flex flex-wrap gap-2 justify-center mt-2">
          <button type="button" class="pill-btn pill-btn-ghost" :disabled="busy" @click="regenerate(t)">
            Regenerează
          </button>
          <button type="button" class="pill-btn pill-btn-danger" :disabled="busy" @click="removeTable(t)">
            Șterge
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.qr-input {
  padding: 12px 14px;
  border-radius: 12px;
  border: 1px solid var(--ink12, rgba(0, 0, 0, 0.12));
  background: var(--card, #fff);
  color: var(--ink, #15140f);
  font-size: 15px;
}
.qr-img {
  width: 200px;
  height: 200px;
  border-radius: 16px;
  background: #fff;
  padding: 10px;
  box-shadow: var(--shadow-soft);
}
.qr-img-empty {
  display: grid;
  place-items: center;
  color: var(--ink40);
  font-size: 24px;
}
.qr-link {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  color: var(--ink70, var(--ink));
  text-decoration: underline;
}
.qr-link:hover {
  color: var(--ink);
}
</style>

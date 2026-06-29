<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import QRCode from 'qrcode'
import { useAuthStore } from '@/stores/useAuthStore'
import { useFloorStore } from '@/stores/useFloorStore'
import { tablesApi, type TableSummary } from '@/services/api-client'
import { useToast } from '@/composables/useToast'

const auth = useAuthStore()
const store = useFloorStore()
const toast = useToast()

// Where the customer app lives. Configurable so prod can point at a real
// domain; defaults to the local dev customer port.
const customerBase = (
  (import.meta.env.VITE_CUSTOMER_URL as string | undefined)?.replace(/\/+$/, '') ||
  'http://localhost:3000'
)

const qrImages = ref<Record<string, string>>({})
const busy = ref(false)

const activeName = computed(() => auth.activeRestaurant?.name ?? 'locația curentă')

// Zone filter — which area within this location to show. 'all' shows every zone
// group; otherwise it matches a TableGroup id (a zone id, or null for the
// "Fără zonă" group).
const selectedGroupId = ref<string | null | 'all'>('all')

// Tables grouped by zone (zones in sortOrder), with a trailing "Fără zonă" group
// for tables that haven't been assigned to any zone yet.
interface TableGroup {
  id: string | null
  name: string
  tables: TableSummary[]
}
const groups = computed<TableGroup[]>(() => {
  const result: TableGroup[] = []
  const sortedZones = [...store.zones].sort((a, b) => a.sortOrder - b.sortOrder)
  for (const z of sortedZones) {
    result.push({ id: z.id, name: z.name, tables: store.tables.filter((t) => t.zoneId === z.id) })
  }
  const noZone = store.tables.filter((t) => t.zoneId === null)
  if (noZone.length) result.push({ id: null, name: 'Fără zonă', tables: noZone })
  return result
})

// Groups actually rendered: all of them, or just the one matching the zone chip.
const visibleGroups = computed<TableGroup[]>(() =>
  selectedGroupId.value === 'all'
    ? groups.value
    : groups.value.filter((g) => g.id === selectedGroupId.value),
)

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

async function renderAllQrs() {
  await Promise.all(store.tables.map(renderQr))
}

onMounted(async () => {
  try {
    await Promise.all([store.fetchZones(), store.fetchTables()])
    await renderAllQrs()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea meselor.')
  }
})

async function regenerate(t: TableSummary) {
  if (!confirm(`Regenerezi codul QR pentru masa ${t.tableNumber}? Codul tipărit anterior nu va mai funcționa.`)) return
  busy.value = true
  try {
    const updated = await tablesApi.regenerate(t.id)
    await store.fetchTables()
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
    await store.deleteTable(t.id)
    delete qrImages.value[t.id]
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
    <div class="mb-6">
      <div class="eyebrow mb-4">Mese &amp; coduri QR</div>
      <h1 class="display-title text-[44px] md:text-[56px]">Coduri QR</h1>
      <p class="hidden md:block text-ink-70 text-base mt-2">
        Coduri pentru <span class="font-semibold text-ink">{{ activeName }}</span>. Fiecare masă are
        propriul cod — clientul îl scanează sau deschide linkul direct.
      </p>
      <p class="hidden md:block text-ink-70 text-sm mt-2">
        Mesele se creează și se aranjează în tabul <span class="font-semibold text-ink">Sala</span>.
        Aici gestionezi codurile lor QR.
      </p>
    </div>

    <!-- Zone filter — show all areas, or narrow to a single zone of this location. -->
    <div v-if="groups.length > 1" class="mb-8" role="group" aria-label="Filtrează după zonă">
      <div class="eyebrow mb-3">Zonă</div>
      <div class="zone-chips">
        <button
          type="button"
          class="zone-chip"
          :class="{ 'zone-chip--active': selectedGroupId === 'all' }"
          :aria-pressed="selectedGroupId === 'all'"
          @click="selectedGroupId = 'all'"
        >
          Toate
          <span class="zone-chip-count">{{ store.tables.length }}</span>
        </button>
        <button
          v-for="g in groups"
          :key="g.id ?? 'no-zone'"
          type="button"
          class="zone-chip"
          :class="{ 'zone-chip--active': selectedGroupId === g.id }"
          :aria-pressed="selectedGroupId === g.id"
          @click="selectedGroupId = g.id"
        >
          {{ g.name }}
          <span class="zone-chip-count">{{ g.tables.length }}</span>
        </button>
      </div>
    </div>

    <div v-if="!store.tables.length" class="card-paper p-6 text-ink italic text-base">
      Nicio masă încă pentru această locație. Creează mesele în tabul <span class="font-semibold not-italic">Sala</span>.
    </div>

    <section v-for="g in visibleGroups" :key="g.id ?? 'no-zone'" class="mb-8">
      <div class="eyebrow mb-3">{{ g.name }}</div>

      <div v-if="!g.tables.length" class="card-paper p-4 text-ink italic text-sm">
        Nicio masă în această zonă.
      </div>

      <div v-else class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
        <div v-for="t in g.tables" :key="t.id" class="card-white p-5 flex flex-col items-center text-center">
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
    </section>
  </div>
</template>

<style scoped>
/* Zone filter chips */
.zone-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.zone-chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 9px 16px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: var(--card, var(--paperHi));
  color: var(--ink);
  font-weight: 600;
  font-size: 14px;
  cursor: pointer;
  transition: background 0.16s ease, color 0.16s ease, border-color 0.16s ease;
}
.zone-chip:hover { background: var(--ink06); }
.zone-chip--active {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
  box-shadow: var(--shadow-soft);
}
.zone-chip-count {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  font-weight: 700;
  padding: 1px 8px;
  border-radius: 999px;
  background: var(--ink06);
  color: var(--ink70, var(--ink));
}
.zone-chip--active .zone-chip-count {
  background: rgba(255, 255, 255, 0.18);
  color: var(--paperHi);
}
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

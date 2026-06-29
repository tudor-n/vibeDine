<script setup lang="ts">
import { ref, watch, computed, onMounted } from 'vue'
import { apiClient } from '@/services/api-client'
import type { HappyHourDeal } from '@shared/types/models'
import type { HappyHourPayload } from '@/stores/useHappyHourStore'

const props = defineProps<{
  config: HappyHourDeal | null
  isLoading: boolean
}>()

const emit = defineEmits<{
  (e: 'save', payload: HappyHourPayload): void
  (e: 'clear'): void
}>()

interface ProductOption {
  id: string
  name: string
  category: string
}

const products = ref<ProductOption[]>([])
const productsLoading = ref(false)

const productId = ref('')
const discountPercent = ref<number>(20)
const startTime = ref('14:00')
const endTime = ref('16:00')
const isEnabled = ref(true)
const error = ref<string | null>(null)

async function loadProducts() {
  try {
    productsLoading.value = true
    const menu = await apiClient.getMenu()
    products.value = (menu?.categories ?? []).flatMap((c) =>
      (c.products ?? []).map((p) => ({ id: p.id, name: p.name, category: c.name })),
    )
  } catch (e) {
    console.error('Eroare la încărcarea meniului:', e)
  } finally {
    productsLoading.value = false
  }
}

// Hydrate the form from an existing config whenever it loads/changes.
watch(
  () => props.config,
  (c) => {
    if (c) {
      productId.value = c.productId
      discountPercent.value = c.discountPercent
      startTime.value = c.startTime
      endTime.value = c.endTime
      isEnabled.value = c.isEnabled
    }
  },
  { immediate: true },
)

onMounted(loadProducts)

// A window whose end is at or before its start wraps past midnight (e.g. 22:00–02:00).
const isOvernight = computed(
  () => !!startTime.value && !!endTime.value && endTime.value <= startTime.value,
)

const canSubmit = computed(
  () =>
    !!productId.value &&
    discountPercent.value >= 1 &&
    discountPercent.value <= 99 &&
    !!startTime.value &&
    !!endTime.value &&
    startTime.value !== endTime.value &&
    !props.isLoading,
)

function onSubmit() {
  error.value = null
  if (!productId.value) {
    error.value = 'Selectează un produs.'
    return
  }
  if (discountPercent.value < 1 || discountPercent.value > 99) {
    error.value = 'Reducerea trebuie să fie între 1 și 99%.'
    return
  }
  if (!startTime.value || !endTime.value) {
    error.value = 'Setează ora de început și de sfârșit.'
    return
  }
  if (startTime.value === endTime.value) {
    error.value = 'Ora de început și cea de sfârșit nu pot fi identice.'
    return
  }
  emit('save', {
    productId: productId.value,
    discountPercent: discountPercent.value,
    startTime: startTime.value,
    endTime: endTime.value,
    isEnabled: isEnabled.value,
  })
}
</script>

<template>
  <div class="hh-card mb-8 relative overflow-hidden">
    <div class="bloom bloom-accent" style="top: -100px; left: -80px; width: 280px; height: 280px; opacity: 0.28;" />

    <div class="relative z-10">
      <div class="flex items-center justify-between mb-5">
        <div>
          <div class="hh-eyebrow mb-2">Happy hour</div>
          <h2 class="hh-title">Ce reducere oferim și când?</h2>
        </div>
        <span class="hh-tag hh-tag-lime">⏰ automat zilnic</span>
      </div>
      <p class="hh-lede mb-6 leading-relaxed max-w-xl">
        Alege un produs, reducerea și intervalul orar. Oferta pornește automat în
        fiecare zi la ora de început și se oprește singură după ora de sfârșit — nu
        trebuie să o ștergi manual.
      </p>

      <form @submit.prevent="onSubmit" class="hh-form">
        <div class="hh-field">
          <label for="hh-product" class="hh-label">Produs</label>
          <select
            id="hh-product"
            v-model="productId"
            class="hh-input"
            :disabled="productsLoading || isLoading"
          >
            <option value="" disabled>
              {{ productsLoading ? 'Se încarcă meniul...' : 'Selectează un produs' }}
            </option>
            <option v-for="p in products" :key="p.id" :value="p.id">
              {{ p.name }} — {{ p.category }}
            </option>
          </select>
        </div>

        <div class="hh-grid">
          <div class="hh-field">
            <label for="hh-discount" class="hh-label">Reducere (%)</label>
            <input
              id="hh-discount"
              v-model.number="discountPercent"
              type="number"
              min="1"
              max="99"
              class="hh-input"
              :disabled="isLoading"
            />
          </div>

          <div class="hh-field">
            <label for="hh-start" class="hh-label">De la</label>
            <input
              id="hh-start"
              v-model="startTime"
              type="time"
              class="hh-input"
              :disabled="isLoading"
            />
          </div>

          <div class="hh-field">
            <label for="hh-end" class="hh-label">Până la</label>
            <input
              id="hh-end"
              v-model="endTime"
              type="time"
              class="hh-input"
              :disabled="isLoading"
            />
          </div>
        </div>

        <p v-if="isOvernight" class="hh-hint">
          Intervalul trece de miezul nopții — se încheie a doua zi la {{ endTime }}.
        </p>

        <label class="hh-toggle">
          <input type="checkbox" v-model="isEnabled" :disabled="isLoading" />
          <span>Activ (debifează pentru a pune oferta pe pauză fără a o șterge)</span>
        </label>

        <p v-if="error" class="hh-error">{{ error }}</p>

        <div class="flex items-center justify-end gap-3 mt-2">
          <button
            v-if="config"
            type="button"
            class="hh-btn-ghost"
            :disabled="isLoading"
            @click="emit('clear')"
          >
            Șterge happy hour
          </button>
          <button type="submit" class="hh-btn" :disabled="!canSubmit">
            <span v-if="isLoading" class="animate-spin" aria-hidden="true">↻</span>
            <span>{{ isLoading ? 'Se salvează...' : 'Salvează happy hour' }}</span>
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<style scoped>
.hh-card {
  background: var(--ink);
  color: var(--paperHi);
  border-radius: 28px;
  padding: 32px;
  box-shadow: var(--shadow-pop);
  border: 1px solid var(--ink12);
}
.hh-title {
  font-family: 'Inter Tight', sans-serif;
  font-weight: 600;
  letter-spacing: -0.04em;
  line-height: 0.95;
  font-size: 30px;
  color: var(--paperHi);
}
.hh-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 13px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--paperHi);
  opacity: 0.78;
}
.hh-lede {
  color: var(--paperHi);
  opacity: 0.85;
  font-size: 17px;
}
.hh-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 14px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.18);
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 12.5px;
  font-weight: 600;
  color: var(--paperHi);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.hh-tag-lime {
  background: var(--lime);
  border-color: var(--ink12);
  color: var(--onLime);
  font-weight: 700;
}
.hh-form {
  display: flex;
  flex-direction: column;
  gap: 18px;
}
.hh-grid {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 14px;
}
@media (max-width: 560px) {
  .hh-grid {
    grid-template-columns: 1fr;
  }
}
.hh-field {
  display: flex;
  flex-direction: column;
  gap: 7px;
}
.hh-label {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 12.5px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--paperHi);
  opacity: 0.82;
}
.hh-input {
  width: 100%;
  background: var(--paperHi);
  color: var(--ink);
  border: 1px solid var(--ink12);
  border-radius: 14px;
  padding: 13px 15px;
  font-family: 'Inter Tight', -apple-system, system-ui, sans-serif;
  font-size: 16px;
  outline: none;
  transition: box-shadow 0.18s ease, border-color 0.18s ease;
}
.hh-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 4px var(--accentSoft);
}
.hh-input:disabled {
  opacity: 0.6;
}
.hh-hint {
  font-size: 14px;
  color: var(--paperHi);
  opacity: 0.8;
}
.hh-toggle {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 15px;
  color: var(--paperHi);
  opacity: 0.92;
  cursor: pointer;
}
.hh-toggle input {
  width: 18px;
  height: 18px;
  accent-color: var(--lime);
}
.hh-error {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: #ffb4a8;
}
.hh-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  background: var(--lime);
  color: var(--onLime);
  border: 1px solid var(--ink12);
  border-radius: 999px;
  padding: 12px 22px;
  font-weight: 700;
  font-size: 15px;
  cursor: pointer;
  transition: transform 0.12s ease, opacity 0.18s ease;
}
.hh-btn:hover:not(:disabled) {
  transform: translateY(-1px);
}
.hh-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}
.hh-btn-ghost {
  background: transparent;
  color: var(--paperHi);
  border: 1px solid rgba(255, 255, 255, 0.25);
  border-radius: 999px;
  padding: 12px 18px;
  font-weight: 600;
  font-size: 15px;
  cursor: pointer;
  opacity: 0.85;
}
.hh-btn-ghost:hover:not(:disabled) {
  opacity: 1;
}
.hh-btn-ghost:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>

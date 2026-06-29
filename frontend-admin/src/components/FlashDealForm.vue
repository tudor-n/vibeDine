<script setup lang="ts">
import { ref, watch, computed, onMounted } from 'vue'
import { apiClient } from '@/services/api-client'
import type { FlashDealRule } from '@shared/types/models'
import type { FlashDealPayload } from '@/stores/useFlashDealRuleStore'
import ProductCombobox from '@/components/ProductCombobox.vue'

const props = defineProps<{
  config: FlashDealRule | null
  isLoading: boolean
}>()

const emit = defineEmits<{
  (e: 'save', payload: FlashDealPayload): void
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
const minOrderTotalInput = ref<string>('') // empty = no minimum
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

watch(
  () => props.config,
  (c) => {
    if (c) {
      productId.value = c.productId
      discountPercent.value = c.discountPercent
      minOrderTotalInput.value = c.minOrderTotal != null ? String(c.minOrderTotal) : ''
      isEnabled.value = c.isEnabled
    }
  },
  { immediate: true },
)

onMounted(loadProducts)

const canSubmit = computed(
  () =>
    !!productId.value &&
    discountPercent.value >= 1 &&
    discountPercent.value <= 99 &&
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
  let minOrderTotal: number | null = null
  const raw = minOrderTotalInput.value.trim()
  if (raw !== '') {
    const n = Number(raw)
    if (Number.isNaN(n) || n < 0) {
      error.value = 'Suma minimă trebuie să fie un număr pozitiv (sau lasă gol).'
      return
    }
    minOrderTotal = n
  }
  emit('save', {
    productId: productId.value,
    discountPercent: discountPercent.value,
    minOrderTotal,
    isEnabled: isEnabled.value,
  })
}
</script>

<template>
  <div class="hh-card mb-8 relative">
    <div class="hh-bloom-clip">
      <div class="bloom bloom-accent" style="top: -100px; left: -80px; width: 280px; height: 280px; opacity: 0.28;" />
    </div>

    <div class="relative z-10">
      <div class="flex items-center justify-between mb-5">
        <div>
          <div class="hh-eyebrow mb-2">Flash deal</div>
          <h2 class="hh-title">Ce ofertă apare după comandă?</h2>
        </div>
        <span class="hh-tag hh-tag-lime">după comandă</span>
      </div>
      <p class="hh-lede mb-6 leading-relaxed max-w-xl">
        După ce clientul trimite comanda, îi apare o ofertă-fulger de 2 minute pentru
        produsul ales, la reducerea setată. Opțional, oferta apare doar dacă nota
        depășește o sumă minimă.
      </p>

      <form @submit.prevent="onSubmit" class="hh-form">
        <div class="hh-field">
          <label for="fd-product" class="hh-label">Produs</label>
          <ProductCombobox
            v-model="productId"
            input-id="fd-product"
            :products="products"
            :loading="productsLoading"
            :disabled="isLoading"
            :fallback-name="config?.productName ?? null"
          />
        </div>

        <div class="hh-grid">
          <div class="hh-field">
            <label for="fd-discount" class="hh-label">Reducere (%)</label>
            <input
              id="fd-discount"
              v-model.number="discountPercent"
              type="number"
              min="1"
              max="99"
              class="hh-input"
              :disabled="isLoading"
            />
          </div>

          <div class="hh-field hh-field-wide">
            <label for="fd-min" class="hh-label">Sumă minimă comandă (lei)</label>
            <input
              id="fd-min"
              v-model="minOrderTotalInput"
              type="number"
              min="0"
              step="0.01"
              inputmode="decimal"
              placeholder="Fără minim"
              class="hh-input"
              :disabled="isLoading"
            />
          </div>
        </div>

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
            Șterge flash deal
          </button>
          <button type="submit" class="hh-btn" :disabled="!canSubmit">
            <span v-if="isLoading" class="animate-spin" aria-hidden="true">↻</span>
            <span>{{ isLoading ? 'Se salvează...' : 'Salvează flash deal' }}</span>
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
.hh-bloom-clip {
  position: absolute;
  inset: 0;
  overflow: hidden;
  border-radius: 28px;
  pointer-events: none;
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
  grid-template-columns: 1fr 2fr;
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

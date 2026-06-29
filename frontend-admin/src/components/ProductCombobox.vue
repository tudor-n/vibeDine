<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'

interface ProductOption {
  id: string
  name: string
  category: string
}

const props = defineProps<{
  modelValue: string
  products: ProductOption[]
  disabled?: boolean
  loading?: boolean
  fallbackName?: string | null
  inputId?: string
}>()

const emit = defineEmits<{ (e: 'update:modelValue', id: string): void }>()

const search = ref('')
const open = ref(false)
const highlighted = ref(0)
const inputEl = ref<HTMLInputElement | null>(null)

const selected = computed(
  () => props.products.find((p) => p.id === props.modelValue) ?? null,
)
// Closed: show the selected product's name (or a fallback if the product is no
// longer in the list). Open: show what the user is typing.
const displayName = computed(() => selected.value?.name ?? props.fallbackName ?? '')

// Diacritic-insensitive so "limonada" matches "Limonadă cu mentă".
function norm(s: string) {
  return s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
}

const allFiltered = computed(() => {
  const q = norm(search.value.trim())
  if (!q) return props.products
  return props.products.filter(
    (p) => norm(p.name).includes(q) || norm(p.category).includes(q),
  )
})
// Cap the rendered list so a 300-item menu stays snappy; typing narrows it.
const filtered = computed(() => allFiltered.value.slice(0, 50))
const truncatedBy = computed(() => allFiltered.value.length - filtered.value.length)

function onFocus() {
  if (props.disabled) return
  search.value = ''
  open.value = true
  highlighted.value = 0
}
function onInput(e: Event) {
  search.value = (e.target as HTMLInputElement).value
  open.value = true
  highlighted.value = 0
}
function select(p: ProductOption) {
  emit('update:modelValue', p.id)
  search.value = ''
  open.value = false
}
function clearSelection() {
  emit('update:modelValue', '')
  search.value = ''
  open.value = true
  nextTick(() => inputEl.value?.focus())
}
function move(delta: number) {
  if (!open.value) {
    open.value = true
    return
  }
  const n = filtered.value.length
  if (n === 0) return
  highlighted.value = (highlighted.value + delta + n) % n
}
function commit() {
  const choice = filtered.value[highlighted.value]
  if (open.value && choice) select(choice)
}
</script>

<template>
  <div class="cb" role="combobox" :aria-expanded="open" aria-haspopup="listbox">
    <div class="cb-inputwrap">
      <input
        :id="inputId"
        ref="inputEl"
        class="cb-input"
        type="text"
        autocomplete="off"
        spellcheck="false"
        aria-label="Caută un produs"
        :value="open ? search : displayName"
        :placeholder="loading ? 'Se încarcă meniul...' : 'Caută un produs...'"
        :disabled="disabled"
        @focus="onFocus"
        @blur="open = false"
        @input="onInput"
        @keydown.down.prevent="move(1)"
        @keydown.up.prevent="move(-1)"
        @keydown.enter.prevent="commit"
        @keydown.esc="open = false"
      />
      <button
        v-if="modelValue && !disabled"
        type="button"
        class="cb-clear"
        aria-label="Șterge selecția"
        @mousedown.prevent="clearSelection"
      >×</button>
    </div>

    <ul v-if="open" class="cb-list" role="listbox">
      <li v-if="filtered.length === 0" class="cb-empty">
        {{ loading ? 'Se încarcă...' : 'Niciun produs găsit' }}
      </li>
      <li
        v-for="(p, i) in filtered"
        :key="p.id"
        class="cb-option"
        :class="{ 'is-active': i === highlighted, 'is-selected': p.id === modelValue }"
        role="option"
        :aria-selected="p.id === modelValue"
        @mousedown.prevent="select(p)"
        @mouseenter="highlighted = i"
      >
        <span class="cb-name">{{ p.name }}</span>
        <span class="cb-cat">{{ p.category }}</span>
      </li>
      <li v-if="truncatedBy > 0" class="cb-more">
        +{{ truncatedBy }} rezultate — continuă să scrii pentru a filtra
      </li>
    </ul>
  </div>
</template>

<style scoped>
.cb {
  position: relative;
  width: 100%;
}
.cb-inputwrap {
  position: relative;
  display: flex;
  align-items: center;
}
.cb-input {
  width: 100%;
  background: var(--paperHi);
  color: var(--ink);
  border: 1px solid var(--ink12);
  border-radius: 14px;
  padding: 13px 38px 13px 15px;
  font-family: 'Inter Tight', -apple-system, system-ui, sans-serif;
  font-size: 16px;
  outline: none;
  transition: box-shadow 0.18s ease, border-color 0.18s ease;
}
.cb-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 4px var(--accentSoft);
}
.cb-input:disabled {
  opacity: 0.6;
}
.cb-clear {
  position: absolute;
  right: 8px;
  width: 26px;
  height: 26px;
  border: none;
  background: var(--ink06, rgba(0, 0, 0, 0.06));
  color: var(--ink);
  border-radius: 50%;
  font-size: 16px;
  line-height: 1;
  cursor: pointer;
  display: grid;
  place-items: center;
}
.cb-clear:hover {
  background: var(--ink12);
}
.cb-list {
  position: absolute;
  z-index: 40;
  top: calc(100% + 6px);
  left: 0;
  right: 0;
  margin: 0;
  padding: 6px;
  list-style: none;
  max-height: 260px;
  overflow-y: auto;
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 14px;
  box-shadow: 0 16px 40px rgba(0, 0, 0, 0.18);
}
.cb-option {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 12px;
  border-radius: 10px;
  cursor: pointer;
  color: var(--ink);
}
.cb-option.is-active {
  background: var(--accentSoft, rgba(0, 0, 0, 0.05));
}
.cb-option.is-selected {
  outline: 1px solid var(--lime);
}
.cb-name {
  font-size: 15px;
  font-weight: 600;
}
.cb-cat {
  font-size: 12.5px;
  opacity: 0.6;
  white-space: nowrap;
}
.cb-empty,
.cb-more {
  padding: 10px 12px;
  font-size: 13px;
  color: var(--ink);
  opacity: 0.65;
}
.cb-more {
  border-top: 1px solid var(--ink06, rgba(0, 0, 0, 0.06));
  margin-top: 4px;
}
</style>

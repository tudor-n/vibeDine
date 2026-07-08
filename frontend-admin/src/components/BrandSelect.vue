<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

export interface BrandSelectOption {
    value: string
    label: string
    disabled?: boolean
}
export interface BrandSelectGroup {
    label: string
    options: BrandSelectOption[]
}

const props = defineProps<{
    modelValue: string
    options?: BrandSelectOption[]
    groups?: BrandSelectGroup[]
    placeholder?: string
    ariaLabel?: string
    disabled?: boolean
}>()

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const open = ref(false)
const triggerEl = ref<HTMLButtonElement | null>(null)
const panelEl = ref<HTMLDivElement | null>(null)
const activeIndex = ref(-1)
const uid = `bs-${Math.random().toString(36).slice(2, 9)}`

// Flatten groups + options into a single navigable list, preserving group boundaries for rendering.
interface Row {
    kind: 'option' | 'group'
    option?: BrandSelectOption
    label?: string
    flatIndex?: number
}
const rows = computed<Row[]>(() => {
    const out: Row[] = []
    if (props.groups?.length) {
        for (const g of props.groups) {
            out.push({ kind: 'group', label: g.label })
            for (const o of g.options) out.push({ kind: 'option', option: o })
        }
    }
    if (props.options?.length) {
        for (const o of props.options) out.push({ kind: 'option', option: o })
    }
    let i = 0
    for (const r of out) if (r.kind === 'option') r.flatIndex = i++
    return out
})

const optionsFlat = computed<BrandSelectOption[]>(() =>
    rows.value.filter((r) => r.kind === 'option').map((r) => r.option!),
)

const selectedLabel = computed(() => {
    const match = optionsFlat.value.find((o) => o.value === props.modelValue)
    return match?.label ?? ''
})

function toggle() {
    if (props.disabled) return
    open.value ? close() : openPanel()
}

function openPanel() {
    open.value = true
    // Highlight the currently-selected option when opening, or the first enabled option.
    const selectedIdx = optionsFlat.value.findIndex((o) => o.value === props.modelValue)
    activeIndex.value = selectedIdx >= 0 ? selectedIdx : firstEnabledIndex()
    void nextTick(() => scrollActiveIntoView())
}

function close() {
    open.value = false
    activeIndex.value = -1
}

function firstEnabledIndex(): number {
    return optionsFlat.value.findIndex((o) => !o.disabled)
}

function moveActive(delta: number) {
    const list = optionsFlat.value
    if (list.length === 0) return
    let idx = activeIndex.value
    for (let step = 0; step < list.length; step++) {
        idx = (idx + delta + list.length) % list.length
        if (!list[idx].disabled) {
            activeIndex.value = idx
            void nextTick(() => scrollActiveIntoView())
            return
        }
    }
}

function scrollActiveIntoView() {
    if (!panelEl.value) return
    const el = panelEl.value.querySelector<HTMLElement>(
        `[data-bs-option-index="${activeIndex.value}"]`,
    )
    if (el) el.scrollIntoView({ block: 'nearest' })
}

function selectByIndex(idx: number) {
    const opt = optionsFlat.value[idx]
    if (!opt || opt.disabled) return
    emit('update:modelValue', opt.value)
    close()
    triggerEl.value?.focus()
}

function onKeydown(e: KeyboardEvent) {
    if (props.disabled) return
    if (!open.value) {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            openPanel()
        }
        return
    }
    switch (e.key) {
        case 'ArrowDown':
            e.preventDefault()
            moveActive(1)
            break
        case 'ArrowUp':
            e.preventDefault()
            moveActive(-1)
            break
        case 'Home':
            e.preventDefault()
            activeIndex.value = firstEnabledIndex()
            void nextTick(() => scrollActiveIntoView())
            break
        case 'End': {
            e.preventDefault()
            for (let i = optionsFlat.value.length - 1; i >= 0; i--) {
                if (!optionsFlat.value[i].disabled) {
                    activeIndex.value = i
                    break
                }
            }
            void nextTick(() => scrollActiveIntoView())
            break
        }
        case 'Enter':
        case ' ':
            e.preventDefault()
            if (activeIndex.value >= 0) selectByIndex(activeIndex.value)
            break
        case 'Escape':
        case 'Tab':
            close()
            break
    }
}

function onDocumentClick(e: MouseEvent) {
    if (!open.value) return
    const target = e.target as Node
    if (triggerEl.value?.contains(target) || panelEl.value?.contains(target)) return
    close()
}

onMounted(() => document.addEventListener('mousedown', onDocumentClick))
onBeforeUnmount(() => document.removeEventListener('mousedown', onDocumentClick))

// Keep the highlighted row in sight if the model changes externally while open.
watch(
    () => props.modelValue,
    () => {
        if (!open.value) return
        const idx = optionsFlat.value.findIndex((o) => o.value === props.modelValue)
        if (idx >= 0) activeIndex.value = idx
    },
)
</script>

<template>
    <div class="brand-select" :class="{ 'is-open': open, 'is-disabled': disabled }">
        <button
            ref="triggerEl"
            type="button"
            class="brand-select-trigger"
            :aria-haspopup="'listbox'"
            :aria-expanded="open"
            :aria-controls="`${uid}-list`"
            :aria-label="ariaLabel"
            :disabled="disabled"
            @click="toggle"
            @keydown="onKeydown"
        >
            <span class="brand-select-value" :class="{ 'is-placeholder': !selectedLabel }">
                {{ selectedLabel || placeholder || '—' }}
            </span>
            <span class="brand-select-caret" aria-hidden="true">▾</span>
        </button>

        <div
            v-if="open"
            ref="panelEl"
            class="brand-select-panel"
            role="listbox"
            :id="`${uid}-list`"
            :aria-activedescendant="activeIndex >= 0 ? `${uid}-opt-${activeIndex}` : undefined"
        >
            <template v-for="(row, rIdx) in rows" :key="rIdx">
                <div v-if="row.kind === 'group'" class="brand-select-group">{{ row.label }}</div>
                <div
                    v-else
                    :id="`${uid}-opt-${row.flatIndex}`"
                    :data-bs-option-index="row.flatIndex"
                    class="brand-select-option"
                    role="option"
                    :class="{
                        'is-active': row.flatIndex === activeIndex,
                        'is-selected': row.option!.value === modelValue,
                        'is-disabled': row.option!.disabled,
                    }"
                    :aria-selected="row.option!.value === modelValue"
                    :aria-disabled="row.option!.disabled || undefined"
                    @mouseenter="row.option!.disabled || (activeIndex = row.flatIndex!)"
                    @mousedown.prevent
                    @click="selectByIndex(row.flatIndex!)"
                >
                    <span class="brand-select-option-label">{{ row.option!.label }}</span>
                    <span
                        v-if="row.option!.value === modelValue"
                        class="brand-select-check"
                        aria-hidden="true"
                    >✓</span>
                </div>
            </template>
        </div>
    </div>
</template>

<style scoped>
.brand-select {
    position: relative;
    width: 100%;
    min-width: 0;
}

.brand-select-trigger {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    width: 100%;
    padding: 10px 14px;
    min-height: var(--tap-min);
    background: var(--paper);
    color: var(--ink);
    border: 1px solid var(--ink12);
    border-radius: 12px;
    font-family: inherit;
    font-size: 14px;
    cursor: pointer;
    text-align: left;
    transition: border-color 0.15s ease, background 0.15s ease;
    min-width: 0;
}
.brand-select-trigger:hover:not(:disabled) {
    border-color: var(--ink40);
}
.is-open .brand-select-trigger {
    border-color: var(--ink);
    background: var(--paperHi);
}
.brand-select-trigger:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.brand-select-value {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}
.brand-select-value.is-placeholder { color: var(--ink40); }

.brand-select-caret {
    flex-shrink: 0;
    font-size: 11px;
    color: var(--ink40);
    transition: transform 0.15s ease;
}
.is-open .brand-select-caret { transform: rotate(180deg); color: var(--ink); }

.brand-select-panel {
    position: absolute;
    top: calc(100% + 6px);
    left: 0;
    right: 0;
    z-index: 80;
    background: var(--paperHi);
    border: 1px solid var(--ink12);
    border-radius: 12px;
    box-shadow: var(--shadow-soft), 0 0 0 4px color-mix(in srgb, var(--paper) 60%, transparent);
    padding: 6px;
    max-height: 280px;
    overflow-y: auto;
    overscroll-behavior: contain;
}

.brand-select-group {
    padding: 8px 10px 4px;
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.14em;
    color: var(--ink40);
}

.brand-select-option {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 8px 10px;
    border-radius: 8px;
    font-size: 14px;
    color: var(--ink);
    cursor: pointer;
    transition: background 0.1s ease, color 0.1s ease;
    min-width: 0;
}
@media (hover: none) {
    .brand-select-option { min-height: var(--tap-min); }
}
.brand-select-option.is-active {
    background: var(--accent);
    color: var(--onAccent);
}
.brand-select-option.is-selected:not(.is-active) {
    background: var(--accentSoft);
    color: var(--ink);
}
.brand-select-option.is-active.is-selected {
    background: var(--accent);
    color: var(--onAccent);
}
.brand-select-option.is-disabled {
    color: var(--ink40);
    cursor: not-allowed;
    background: transparent;
}

.brand-select-option-label {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}
.brand-select-check {
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 12px;
    font-weight: 700;
    color: inherit;
    opacity: 0.8;
}
</style>

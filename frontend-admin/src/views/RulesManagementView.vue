<script setup lang="ts">
import { onMounted, computed, ref } from 'vue'
import { useSimilarityRulesStore } from '@/stores/useSimilarityRulesStore'
import { useRecommendationDirectivesStore } from '@/stores/useRecommendationDirectivesStore'
import SimilarityRuleTable from '@/components/SimilarityRuleTable.vue'
import RecommendationRuleInput from '@/components/RecommendationRuleInput.vue'
import { apiClient } from '@/services/api-client'
import type { ParsedRecommendationDirective, RecommendationDirective } from '@shared/types/models'

const rulesStore = useSimilarityRulesStore()
const recRulesStore = useRecommendationDirectivesStore()

const isInputOpen = ref(false)
const productNamesById = ref<Record<string, string>>({})

onMounted(async () => {
    rulesStore.fetchRules()
    recRulesStore.fetchDirectives()

    try {
        const menu = await apiClient.getMenu()
        const map: Record<string, string> = {}
        for (const cat of menu.categories) {
            for (const p of cat.products) {
                map[p.id] = p.name
            }
        }
        productNamesById.value = map
    } catch (err) {
        console.warn('Nu s-a putut încărca meniul pentru rezolvarea numelor produselor.', err)
    }
})

function parseDirective(d: RecommendationDirective): ParsedRecommendationDirective | null {
    if (!d.parsedRules) return null
    try {
        return JSON.parse(d.parsedRules) as ParsedRecommendationDirective
    } catch {
        return null
    }
}

function nameFor(id: string): string {
    return productNamesById.value[id] ?? id.slice(0, 8) + '…'
}

const handleSubmitRule = async (prompt: string) => {
    try {
        await recRulesStore.postDirective(prompt)
        isInputOpen.value = false
    } catch (err) {
        alert('Eroare la salvarea regulii.')
    }
}

const handleToggleRule = async (id: string, current: boolean | null) => {
    try {
        await recRulesStore.toggleActive(id, !current)
    } catch (err) {
        alert('Eroare la actualizarea regulii.')
    }
}

const handleDeleteRecRule = async (id: string) => {
    if (!window.confirm('Ești sigur că vrei să ștergi această regulă scrisă?')) return
    try {
        await recRulesStore.deleteDirective(id)
    } catch (err) {
        alert('Eroare la ștergerea regulii.')
    }
}

const handleDeleteRule = async (id: string) => {
    if (window.confirm('Ești sigur că vrei să ștergi această regulă de recomandare?')) {
        try {
            await rulesStore.deleteRule(id)
        } catch (error) {
            alert('Eroare la ștergerea regulii.')
        }
    }
}

const ruleCount = computed(
    () => (rulesStore.rules?.length ?? 0) + recRulesStore.activeDirectives.length
)
</script>

<template>
    <div class="max-w-6xl mx-auto relative">
        <!-- Status row -->
        <div class="flex items-center justify-between mb-8">
            <div class="tag-pill" role="status" aria-label="Modul de recomandări încrucișate este activ">
                <span
                    class="live-dot"
                    style="background: var(--lime); box-shadow: 0 0 10px var(--lime);"
                    aria-hidden="true"
                ></span>
                <span>recomandări · activ</span>
            </div>
            <div class="tag-pill" aria-label="Reguli active">{{ ruleCount }} reguli active</div>
        </div>

        <header
            class="mb-10 flex flex-col md:flex-row md:items-end md:justify-between gap-6"
        >
            <div class="max-w-2xl">
                <div class="eyebrow mb-4">Recomandări · perechi de produse</div>
                <h1 class="display-title text-[44px] md:text-[56px]">
                    Reguli<br />
                    <span class="text-accent">recomandări.</span>
                </h1>
                <p class="text-ink text-lg mt-5 leading-relaxed">
                    Legăturile dintre produse care alimentează sugestiile și
                    recomandările în sală — fie generate de AI, fie scrise manual.
                </p>
            </div>

            <button
                class="pill-btn whitespace-nowrap"
                type="button"
                :aria-expanded="isInputOpen"
                aria-controls="manual-rule-form"
                @click="isInputOpen = !isInputOpen"
            >
                <span aria-hidden="true">{{ isInputOpen ? '×' : '+' }}</span>
                <span>{{ isInputOpen ? 'Închide formularul' : 'Adaugă regulă manuală' }}</span>
            </button>
        </header>

        <div
            v-if="rulesStore.error || recRulesStore.error"
            class="card-paper p-5 mb-6"
            style="border-color: var(--accentBorder); background: var(--accentSoft);"
            role="alert"
        >
            <span class="font-mono text-sm uppercase tracking-[0.06em] font-bold" style="color: var(--accentDk);">
                <span aria-hidden="true">⚠</span> {{ rulesStore.error || recRulesStore.error }}
            </span>
        </div>

        <!-- Manual prompt input (toggle) -->
        <RecommendationRuleInput
            v-if="isInputOpen"
            id="manual-rule-form"
            :is-loading="recRulesStore.isLoading"
            @submit="handleSubmitRule"
        />

        <!-- Hand-written recommendation rules section -->
        <section class="mb-12">
            <div class="flex items-center justify-between mb-5">
                <div>
                    <div class="eyebrow mb-2">Reguli scrise · de la directivă la regulă</div>
                    <h2 class="display-title text-3xl">Reguli scrise de tine</h2>
                </div>
                <span class="tag-pill">
                    {{ recRulesStore.directives.length }} totale ·
                    {{ recRulesStore.activeDirectives.length }} active
                </span>
            </div>

            <div
                v-if="recRulesStore.isLoading && recRulesStore.directives.length === 0"
                class="card-paper p-6 space-y-3"
            >
                <div class="h-4 bg-ink-12 rounded-full w-3/4 animate-pulse"></div>
                <div class="h-4 bg-ink-12 rounded-full w-1/2 animate-pulse"></div>
            </div>

            <div
                v-else-if="recRulesStore.directives.length === 0"
                class="card-paper p-6 text-ink italic text-base"
            >
                Nicio regulă scrisă încă. Apasă „Adaugă regulă manuală" și
                scrie-ți prima regulă în limbaj natural.
            </div>

            <div v-else class="space-y-4">
                <article
                    v-for="d in recRulesStore.directives"
                    :key="d.id"
                    class="card-paper p-6 relative"
                    :style="d.isActive ? '' : 'opacity: 0.55;'"
                >
                    <div class="flex items-start justify-between gap-4 mb-4">
                        <div class="flex-1">
                            <div class="eyebrow mb-2">
                                {{ d.isActive ? 'în direct · regulă activă' : 'pe pauză · inactivă' }}
                            </div>
                            <p class="text-ink text-lg leading-relaxed font-medium">
                                "{{ d.rawPrompt }}"
                            </p>
                        </div>

                        <div class="flex items-center gap-2 shrink-0">
                            <button
                                class="pill-btn pill-btn-ghost"
                                type="button"
                                :aria-label="d.isActive ? 'Pune regula pe pauză' : 'Activează regula'"
                                :aria-pressed="!d.isActive"
                                @click="handleToggleRule(d.id, d.isActive)"
                            >
                                {{ d.isActive ? 'Pauză' : 'Activează' }}
                            </button>
                            <button
                                class="pill-btn pill-btn-danger"
                                type="button"
                                aria-label="Șterge regula scrisă"
                                @click="handleDeleteRecRule(d.id)"
                            >
                                Șterge
                            </button>
                        </div>
                    </div>

                    <div v-if="parseDirective(d) as ParsedRecommendationDirective | null">
                        <div
                            v-for="(rule, idx) in (parseDirective(d)?.rules ?? [])"
                            :key="idx"
                            class="card-white p-4 mb-2"
                        >
                            <div class="text-sm text-ink mb-2 font-mono uppercase tracking-[0.06em] font-semibold">
                                regulă {{ idx + 1 }} · încredere
                                {{ Math.round((rule.confidence ?? 0) * 100) }}%
                            </div>
                            <div class="text-base text-ink mb-2 flex flex-wrap items-center gap-2">
                                <span class="tag-pill">
                                    când:
                                    <span
                                        v-for="(tid, ti) in rule.trigger_product_ids"
                                        :key="tid"
                                    >
                                        {{ nameFor(tid) }}<span v-if="ti < rule.trigger_product_ids.length - 1">, </span>
                                    </span>
                                </span>
                                <span class="text-ink text-xl" aria-hidden="true">→</span>
                                <span
                                    class="tag-pill"
                                    style="background: var(--lime); color: var(--onLime); border-color: var(--ink12);"
                                >
                                    recomandă:
                                    <span
                                        v-for="(rid, ri) in rule.recommend_product_ids"
                                        :key="rid"
                                    >
                                        {{ nameFor(rid) }}<span v-if="ri < rule.recommend_product_ids.length - 1">, </span>
                                    </span>
                                </span>
                            </div>
                            <p v-if="rule.reason" class="text-ink italic text-base">
                                „{{ rule.reason }}"
                            </p>
                        </div>

                        <div
                            v-if="(parseDirective(d)?.unresolved?.length ?? 0) > 0"
                            class="card-white p-4 mt-2"
                            style="border-color: var(--accentBorder); background: var(--accentSoft);"
                            role="alert"
                        >
                            <div class="font-mono text-sm uppercase tracking-[0.06em] font-bold mb-1" style="color: var(--accentDk);">
                                <span aria-hidden="true">⚠</span> AI nu a putut potrivi
                            </div>
                            <div class="text-ink text-base">
                                {{ parseDirective(d)?.unresolved?.join(', ') }}
                            </div>
                            <div class="text-ink text-sm mt-1">
                                Verifică numele în meniu sau reformulează regula.
                            </div>
                        </div>
                    </div>

                    <div v-else class="card-white p-4 text-ink italic text-base">
                        AI-ul nu a procesat această regulă (poate să fi fost indisponibil
                        la salvare). Reactivează AI-ul și recreează regula.
                    </div>
                </article>
            </div>
        </section>

        <!-- Auto-generated similarity rules section -->
        <section>
            <div class="mb-5">
                <div class="eyebrow mb-2">Sugestii · produse cumpărate împreună</div>
                <h2 class="display-title text-3xl">Sugestii AI generate automat</h2>
                <p class="text-ink mt-2 text-base">
                    Reguli derivate automat din istoricul comenzilor.
                </p>
            </div>

            <SimilarityRuleTable
                :rules="rulesStore.rules"
                :is-loading="rulesStore.isLoading"
                @delete="handleDeleteRule"
            />
        </section>
    </div>
</template>

<style scoped>
.pill-btn-ghost {
    background: transparent;
    color: var(--ink);
    border: 1px solid var(--ink12);
    box-shadow: none;
}
.pill-btn-danger {
    background: transparent;
    color: var(--accentDk);
    border: 1px solid var(--accentBorder);
    box-shadow: none;
}
</style>

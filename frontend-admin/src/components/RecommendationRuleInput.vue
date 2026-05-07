<script setup lang="ts">
import { ref } from 'vue'

const emit = defineEmits<{
    (e: 'submit', prompt: string): void
}>()

const props = defineProps<{
    isLoading: boolean
}>()

const promptText = ref('')

const onSubmit = () => {
    if (promptText.value.trim() === '') return
    emit('submit', promptText.value)
    promptText.value = ''
}
</script>

<template>
    <div class="prompt-card mb-8 relative overflow-hidden">
        <div
            class="bloom bloom-accent"
            style="top: -100px; left: -80px; width: 280px; height: 280px; opacity: 0.30;"
        />

        <div class="relative z-10">
            <div class="flex items-center justify-between mb-5">
                <div>
                    <div class="prompt-eyebrow mb-2">Regulă scrisă · prompt</div>
                    <h2 class="prompt-title">Spune AI-ului ce să recomande</h2>
                </div>
                <span class="prompt-tag prompt-tag-lime">✦ pairing · AI</span>
            </div>
            <p class="prompt-lede mb-6 leading-relaxed max-w-xl">
                Scrie o frază care leagă un produs de o recomandare. AI-ul rezolvă numele
                la produsele exacte din meniu. Mai multe reguli pot rula în paralel.
            </p>

            <form @submit.prevent="onSubmit">
                <div class="textarea-wrap">
                    <textarea
                        v-model="promptText"
                        rows="4"
                        placeholder="Ex: pentru tiramisu oferă limonadă cu mentă"
                        class="prompt-textarea"
                        :disabled="isLoading"
                    ></textarea>
                    <div class="textarea-foot">
                        <span class="textarea-meta">{{ promptText.length }} caractere</span>
                    </div>
                </div>

                <div class="flex items-center justify-between mt-5">
                    <div class="hidden md:flex items-center gap-2">
                        <span class="prompt-tag">tone · sugestiv</span>
                        <span class="prompt-tag">scope · meniu live</span>
                    </div>
                    <button
                        type="submit"
                        :disabled="isLoading || promptText.trim() === ''"
                        class="pill-btn"
                    >
                        <span v-if="isLoading" class="animate-spin">↻</span>
                        <span v-else>✦</span>
                        <span>{{ isLoading ? 'AI procesează...' : 'Salvează regula' }}</span>
                        <span v-if="!isLoading" class="opacity-70">→</span>
                    </button>
                </div>
            </form>
        </div>
    </div>
</template>

<style scoped>
.prompt-card {
    background: var(--ink);
    color: var(--paperHi);
    border-radius: 28px;
    padding: 32px;
    box-shadow: var(--shadow-pop);
    border: 1px solid var(--ink12);
}
.prompt-title {
    font-family: 'Inter Tight', sans-serif;
    font-weight: 600;
    letter-spacing: -0.04em;
    line-height: 0.95;
    font-size: 30px;
    color: var(--paperHi);
}
.prompt-eyebrow {
    font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
    font-size: 11px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.18em;
    color: var(--paperHi);
    opacity: 0.6;
}
.prompt-lede {
    color: var(--paperHi);
    opacity: 0.65;
    font-size: 16px;
}
.prompt-tag {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 12px;
    border-radius: 999px;
    background: rgba(255, 255, 255, 0.08);
    border: 1px solid rgba(255, 255, 255, 0.14);
    font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
    font-size: 11px;
    font-weight: 500;
    color: var(--paperHi);
    opacity: 0.85;
    text-transform: uppercase;
    letter-spacing: 0.12em;
}
.prompt-tag-lime {
    background: var(--lime);
    border-color: var(--ink12);
    color: var(--onLime);
    opacity: 1;
    font-weight: 700;
}
.textarea-wrap {
    background: var(--paperHi);
    color: var(--ink);
    border-radius: 20px;
    padding: 18px 20px 14px;
    border: 1px solid var(--ink12);
    transition: box-shadow 0.18s ease, border-color 0.18s ease;
}
.textarea-wrap:focus-within {
    border-color: var(--accent);
    box-shadow: 0 0 0 4px var(--accentSoft);
}
.prompt-textarea {
    width: 100%;
    background: transparent;
    border: none;
    outline: none;
    resize: none;
    font-family: 'Inter Tight', -apple-system, system-ui, sans-serif;
    font-size: 17px;
    line-height: 1.5;
    color: var(--ink);
}
.prompt-textarea::placeholder {
    color: var(--ink40);
}
.textarea-foot {
    display: flex;
    justify-content: flex-end;
    border-top: 1px solid var(--ink06);
    padding-top: 8px;
    margin-top: 8px;
}
.textarea-meta {
    font-family: 'JetBrains Mono', monospace;
    font-size: 10px;
    text-transform: uppercase;
    letter-spacing: 0.18em;
    color: var(--ink40);
}
</style>

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { apiClient } from '@/services/api-client'
import type { RecommendationDirective } from '@shared/types/models'

export const useRecommendationDirectivesStore = defineStore('recommendationDirectives', () => {
    const directives = ref<RecommendationDirective[]>([])
    const isLoading = ref(false)
    const error = ref<string | null>(null)

    const activeDirectives = computed(() =>
        directives.value.filter((d) => d.isActive)
    )

    async function fetchDirectives() {
        try {
            isLoading.value = true
            error.value = null
            directives.value = await apiClient.getRecommendationDirectives()
        } catch (err) {
            console.error('Eroare la aducerea directivelor de recomandare:', err)
            error.value = err instanceof Error ? err.message : 'Eroare necunoscută'
        } finally {
            isLoading.value = false
        }
    }

    async function postDirective(prompt: string) {
        try {
            isLoading.value = true
            const created = await apiClient.postRecommendationDirective(prompt)
            directives.value.unshift(created)
            return created
        } catch (err) {
            console.error('Eroare la salvarea directivei de recomandare:', err)
            throw err
        } finally {
            isLoading.value = false
        }
    }

    async function toggleActive(id: string, isActive: boolean) {
        try {
            await apiClient.toggleRecommendationDirective(id, isActive)
            const target = directives.value.find((d) => d.id === id)
            if (target) target.isActive = isActive
        } catch (err) {
            console.error('Eroare la modificarea statusului directivei:', err)
            throw err
        }
    }

    async function deleteDirective(id: string) {
        try {
            await apiClient.deleteRecommendationDirective(id)
            directives.value = directives.value.filter((d) => d.id !== id)
        } catch (err) {
            console.error('Eroare la ștergerea directivei:', err)
            throw err
        }
    }

    return {
        directives,
        activeDirectives,
        isLoading,
        error,
        fetchDirectives,
        postDirective,
        toggleActive,
        deleteDirective,
    }
})
